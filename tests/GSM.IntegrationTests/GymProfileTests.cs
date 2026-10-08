using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// G2: datos nuevos del gym (razón social, ubicación, redes) por HTTP con la identidad real, y la
/// lectura "Mi gimnasio" del miembro.
/// </summary>
public sealed class GymProfileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static object ValidBody(Action<Dictionary<string, object?>>? tweak = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = "Iron Perfil",
            ["nit"] = "900123456-7",
            ["phone"] = "300 123 4567",
            ["email"] = "hola@ironperfil.test",
            ["logoUrl"] = null,
            ["primaryColor"] = "#B50016",
            ["secondaryColor"] = "#0AAFCA",
            ["capacity"] = 80,
            ["legalName"] = "Iron Perfil S.A.S.",
            ["address"] = "Calle 10 # 5-20",
            ["city"] = "Medellín",
            ["latitude"] = 6.2442,
            ["longitude"] = -75.5812,
            ["geofenceRadiusMeters"] = 200,
            ["instagramUrl"] = "https://instagram.com/ironperfil",
            ["facebookUrl"] = "https://facebook.com/ironperfil",
            ["tikTokUrl"] = "https://tiktok.com/@ironperfil",
            ["websiteUrl"] = "https://ironperfil.test",
            ["whatsAppPhone"] = "(300) 765-4321",
        };
        tweak?.Invoke(body);
        return body;
    }

    [Fact]
    public async Task Owner_edits_every_new_field_and_reads_it_back_with_e164_phones()
    {
        var gymId = await SeedGymAsync();
        await using var host = RealIdentityHost();
        var owner = Client(host, gymId, UserRole.Owner);

        var put = await owner.PutAsJsonAsync("/api/gym", ValidBody());
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var gym = await owner.GetFromJsonAsync<JsonElement>("/api/gym");
        Assert.Equal("+573001234567", gym.GetProperty("phone").GetString());
        Assert.Equal("+573007654321", gym.GetProperty("whatsAppPhone").GetString());
        Assert.Equal("Iron Perfil S.A.S.", gym.GetProperty("legalName").GetString());
        Assert.Equal("Calle 10 # 5-20", gym.GetProperty("address").GetString());
        Assert.Equal("Medellín", gym.GetProperty("city").GetString());
        Assert.Equal(6.2442, gym.GetProperty("latitude").GetDouble());
        Assert.Equal(-75.5812, gym.GetProperty("longitude").GetDouble());
        Assert.Equal(200, gym.GetProperty("geofenceRadiusMeters").GetInt32());
        Assert.Equal("https://instagram.com/ironperfil", gym.GetProperty("instagramUrl").GetString());
        Assert.Equal("https://facebook.com/ironperfil", gym.GetProperty("facebookUrl").GetString());
        Assert.Equal("https://tiktok.com/@ironperfil", gym.GetProperty("tikTokUrl").GetString());
        Assert.Equal("https://ironperfil.test", gym.GetProperty("websiteUrl").GetString());
    }

    [Fact]
    public async Task A_gym_that_was_never_given_a_location_keeps_the_default_radius_and_works()
    {
        var gymId = await SeedGymAsync();
        await using var host = RealIdentityHost();
        var owner = Client(host, gymId, UserRole.Owner);

        var gym = await owner.GetFromJsonAsync<JsonElement>("/api/gym");

        Assert.Equal(150, gym.GetProperty("geofenceRadiusMeters").GetInt32());
        Assert.Equal(JsonValueKind.Null, gym.GetProperty("address").ValueKind);
        // El resto de endpoints del gym siguen respondiendo sin dirección.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/gym/feature-flags")).StatusCode);
    }

    [Theory]
    [InlineData("latitude", 91.0)]
    [InlineData("latitude", -90.5)]
    [InlineData("longitude", 181.0)]
    [InlineData("geofenceRadiusMeters", 5)]
    [InlineData("geofenceRadiusMeters", 99999)]
    [InlineData("instagramUrl", "instagram.com/sin-esquema")]
    [InlineData("websiteUrl", "javascript:alert(1)")]
    [InlineData("tikTokUrl", "no es una url")]
    [InlineData("address", "")]
    [InlineData("city", null)]
    public async Task Invalid_form_values_get_400(string field, object? value)
    {
        var gymId = await SeedGymAsync();
        await using var host = RealIdentityHost();
        var owner = Client(host, gymId, UserRole.Owner);

        var response = await owner.PutAsJsonAsync("/api/gym", ValidBody(b => b[field] = value));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Latitude_without_longitude_is_rejected()
    {
        var gymId = await SeedGymAsync();
        await using var host = RealIdentityHost();
        var owner = Client(host, gymId, UserRole.Owner);

        var response = await owner.PutAsJsonAsync("/api/gym", ValidBody(b => b["longitude"] = null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("whatsAppPhone")]
    public async Task A_phone_that_is_not_a_number_is_a_business_rule_422(string field)
    {
        var gymId = await SeedGymAsync();
        await using var host = RealIdentityHost();
        var owner = Client(host, gymId, UserRole.Owner);

        var response = await owner.PutAsJsonAsync("/api/gym", ValidBody(b => b[field] = "abc"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Coordinates_out_of_range_are_also_a_422_when_the_domain_is_called_directly()
    {
        var gym = Gym.Create("Dominio", "dominio-gym");
        Assert.Throws<GSM.Domain.Common.Exceptions.ValidationDomainException>(
            () => gym.UpdateLocation("Calle 1", "Cali", 95, 0));
        Assert.Throws<GSM.Domain.Common.Exceptions.ValidationDomainException>(
            () => gym.UpdateSocial("ftp://x.test", null, null, null, null));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Member_reads_their_own_gym_and_not_another()
    {
        var a = await SeedGymAsync(address: "Carrera A # 1", whatsApp: "+573001111111");
        var b = await SeedGymAsync(address: "Carrera B # 2", whatsApp: "+573002222222");
        await using var host = RealIdentityHost();

        var asA = await Client(host, a, UserRole.Member).GetFromJsonAsync<JsonElement>("/api/me/gym");
        var asB = await Client(host, b, UserRole.Member).GetFromJsonAsync<JsonElement>("/api/me/gym");

        Assert.Equal("Carrera A # 1", asA.GetProperty("address").GetString());
        Assert.Equal("+573001111111", asA.GetProperty("whatsAppPhone").GetString());
        Assert.Equal("Carrera B # 2", asB.GetProperty("address").GetString());
        Assert.Equal("+573002222222", asB.GetProperty("whatsAppPhone").GetString());
        // El miembro no ve datos comerciales.
        Assert.False(asA.TryGetProperty("nit", out _));
        Assert.False(asA.TryGetProperty("legalName", out _));
    }

    [Fact]
    public async Task Staff_cannot_use_the_member_endpoint_and_member_cannot_edit_the_gym()
    {
        var gymId = await SeedGymAsync();
        await using var host = RealIdentityHost();

        Assert.Equal(HttpStatusCode.Forbidden, (await Client(host, gymId, UserRole.Owner).GetAsync("/api/me/gym")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client(host, gymId, UserRole.Member).PutAsJsonAsync("/api/gym", ValidBody())).StatusCode);
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host, Guid gymId, UserRole role)
    {
        var token = host.Services.GetRequiredService<IJwtTokenService>().CreateAccessToken(
            new AuthenticatedUser(Guid.CreateVersion7(), "perfil@test.local", "Perfil", role, gymId));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    private async Task<Guid> SeedGymAsync(string? address = null, string? whatsApp = null)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Perfil Gym", $"perfil-{Guid.NewGuid():N}"[..20]);
        if (address is not null)
        {
            gym.UpdateLocation(address, "Bogotá", 4.711, -74.0721);
            gym.UpdateSocial(null, null, null, null, whatsApp);
        }

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();
        return gym.Id;
    }
}
