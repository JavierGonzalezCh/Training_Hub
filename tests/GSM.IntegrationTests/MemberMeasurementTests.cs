using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// M4 — mediciones de peso y altura con historial: IMC calculado, altura heredada, rangos, candado de la
/// autorización de datos, permiso <c>ViewMemberHealth</c> y borrado con huella en el historial.
/// </summary>
public sealed class MemberMeasurementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private const string Data = "data_processing";

    [Fact]
    public async Task Adding_measurements_computes_bmi_and_a_missing_height_uses_the_last_one()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m4-1"));
        await AcceptDataAsync(owner, id);

        var day1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
        var day2 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));

        var first = await owner.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(day1, 80m, 180m));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstItem = (await first.Content.ReadFromJsonAsync<MemberMeasurementItem>())!;
        Assert.Equal(24.7m, firstItem.Bmi); // 80 / 1.8^2
        Assert.Equal("owner", firstItem.RecordedByRole);

        // Sin altura: vale la última registrada (180).
        var second = await owner.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(day2, 72m, null));
        var secondItem = (await second.Content.ReadFromJsonAsync<MemberMeasurementItem>())!;
        Assert.Null(secondItem.HeightCm);
        Assert.Equal(180m, secondItem.EffectiveHeightCm);
        Assert.Equal(22.2m, secondItem.Bmi);

        // La lista sale de la fecha más reciente a la más antigua.
        var view = (await owner.GetFromJsonAsync<MemberMeasurementsView>($"/api/members/{id}/measurements"))!;
        Assert.False(view.ConsentRequired);
        Assert.Equal([secondItem.Id, firstItem.Id], view.Items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task The_first_measurement_without_height_has_no_bmi_and_the_date_defaults_to_today()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m4-2"));
        await AcceptDataAsync(owner, id);

        var response = await owner.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(null, 70m, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<MemberMeasurementItem>())!;
        Assert.Null(item.Bmi);
        Assert.Null(item.EffectiveHeightCm);
        // La fecha es "hoy" en la zona del gym: puede diferir un día de la UTC.
        Assert.InRange(item.MeasuredOn.DayNumber, DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - 1, DateOnly.FromDateTime(DateTime.UtcNow).DayNumber + 1);
    }

    [Theory]
    [InlineData(19.9, null)]
    [InlineData(400.1, null)]
    [InlineData(70, 49.9)]
    [InlineData(70, 250.1)]
    public async Task Weight_and_height_out_of_range_are_422(double weight, double? height)
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m4-3-" + weight + height));
        await AcceptDataAsync(owner, id);

        var response = await owner.PostAsJsonAsync(
            $"/api/members/{id}/measurements",
            new AddMemberMeasurementRequest(null, (decimal)weight, (decimal?)height));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_future_date_is_422_and_a_non_positive_weight_is_400()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m4-4"));
        await AcceptDataAsync(owner, id);

        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await owner.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(future, 70m, null))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(null, 0m, null))).StatusCode);
    }

    [Fact]
    public async Task Without_the_data_authorization_the_section_is_blocked_and_nothing_is_saved()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m4-5"));

        var view = (await owner.GetFromJsonAsync<MemberMeasurementsView>($"/api/members/{id}/measurements"))!;
        Assert.True(view.ConsentRequired);
        Assert.Empty(view.Items);

        var post = await owner.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(null, 70m, 170m));
        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);

        // Aceptada la autorización se puede guardar; revocada, vuelve a bloquearse y no muestra lo guardado.
        await AcceptDataAsync(owner, id);
        Assert.Equal(
            HttpStatusCode.Created,
            (await owner.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(null, 70m, 170m))).StatusCode);

        await owner.PostAsync($"/api/members/{id}/consents/{Data}/revoke", null);
        var blocked = (await owner.GetFromJsonAsync<MemberMeasurementsView>($"/api/members/{id}/measurements"))!;
        Assert.True(blocked.ConsentRequired);
        Assert.Empty(blocked.Items);
    }

    [Fact]
    public async Task Staff_without_the_permission_gets_403_and_with_it_can_read_add_and_delete()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m4-6"));
        await AcceptDataAsync(owner, id);
        var added = (await (await owner.PostAsJsonAsync(
            $"/api/members/{id}/measurements", new AddMemberMeasurementRequest(null, 70m, 170m)))
            .Content.ReadFromJsonAsync<MemberMeasurementItem>())!;

        await SetPermissionsAsync(owner, w.StaffId); // sin permisos especiales
        var staff = await LoggedInAsync(host, w.StaffEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/members/{id}/measurements")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await staff.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(null, 71m, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"/api/members/{id}/measurements/{added.Id}")).StatusCode);

        await SetPermissionsAsync(owner, w.StaffId, "view_member_health");
        var allowed = await LoggedInAsync(host, w.StaffEmail);
        var post = await allowed.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(null, 71m, null));
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        Assert.Equal("staff", (await post.Content.ReadFromJsonAsync<MemberMeasurementItem>())!.RecordedByRole);
        Assert.Equal(2, (await allowed.GetFromJsonAsync<MemberMeasurementsView>($"/api/members/{id}/measurements"))!.Items.Count);
    }

    [Fact]
    public async Task Deleting_removes_the_row_and_leaves_a_sensitive_trace_in_the_history()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m4-7"));
        await AcceptDataAsync(owner, id);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
        var added = (await (await owner.PostAsJsonAsync(
            $"/api/members/{id}/measurements", new AddMemberMeasurementRequest(day, 81.5m, 175m)))
            .Content.ReadFromJsonAsync<MemberMeasurementItem>())!;

        // Añadir no genera fila de historial: ella misma es el historial.
        var before = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes"))!;
        Assert.DoesNotContain(before.Items, c => c.Field == "measurement");

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/members/{id}/measurements/{added.Id}")).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<MemberMeasurementsView>($"/api/members/{id}/measurements"))!.Items);

        var after = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes"))!;
        var row = Assert.Single(after.Items, c => c.Field == "measurement");
        Assert.True(row.IsSensitive);
        Assert.Contains("81.5 kg", row.OldValue);
        Assert.Contains("175 cm", row.OldValue);
        Assert.Null(row.NewValue);

        // Borrar de nuevo: 404.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/members/{id}/measurements/{added.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_measurement_of_another_member_cannot_be_deleted_through_this_one()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var a = await CreateAsync(owner, Body("m4-8a"));
        var b = await CreateAsync(owner, Body("m4-8b"));
        await AcceptDataAsync(owner, a);
        await AcceptDataAsync(owner, b);
        var added = (await (await owner.PostAsJsonAsync(
            $"/api/members/{a}/measurements", new AddMemberMeasurementRequest(null, 70m, 170m)))
            .Content.ReadFromJsonAsync<MemberMeasurementItem>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/members/{b}/measurements/{added.Id}")).StatusCode);
        Assert.Single((await owner.GetFromJsonAsync<MemberMeasurementsView>($"/api/members/{a}/measurements"))!.Items);
    }

    [Fact]
    public async Task Measurements_of_another_gym_are_invisible()
    {
        var w1 = await SeedAsync();
        var w2 = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner1 = await LoggedInAsync(host, w1.OwnerEmail);
        var owner2 = await LoggedInAsync(host, w2.OwnerEmail);
        var id = await CreateAsync(owner1, Body("m4-9"));
        await AcceptDataAsync(owner1, id);
        await owner1.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(null, 70m, 170m));

        Assert.Equal(HttpStatusCode.NotFound, (await owner2.GetAsync($"/api/members/{id}/measurements")).StatusCode);
    }

    // --- Infraestructura de la prueba (misma que MemberHealthTests) ---

    private static async Task AcceptDataAsync(HttpClient api, Guid memberId)
    {
        var response = await api.PostAsJsonAsync($"/api/members/{memberId}/consents", new AcceptConsentRequest(Data, null));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task SetPermissionsAsync(HttpClient owner, Guid staffId, params string[] permissions)
    {
        var profile = permissions.Length == 0 ? "reception" : "trainer";
        var response = await owner.PutAsJsonAsync(
            $"/api/gym/staff/{staffId}/permissions", new UpdateStaffPermissionsRequest(profile, permissions));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, object?> Body(string document) => new()
    {
        ["firstName"] = "Ana",
        ["lastName"] = "Medidas",
        ["documentType"] = "cc",
        ["documentNumber"] = document,
        ["birthDate"] = "1990-05-05",
    };

    private static async Task<Guid> CreateAsync(HttpClient api, Dictionary<string, object?> body)
    {
        var response = await api.PostAsJsonAsync("/api/members", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateMemberResponse>())!.Id;
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();

        var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.Tokens!.AccessToken);
        return client;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Measure Gym", $"measure-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Owner Dueño", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Staff Uno", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerId, ownerEmail, staffId, staffEmail);
    }

    private sealed record World(Guid GymId, Guid OwnerId, string OwnerEmail, Guid StaffId, string StaffEmail);
}
