using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// El filtro de bloqueo (etapa 11.2) de punta a punta: petición HTTP real, JWT real y el
/// <see cref="CurrentUser"/> de producción leyendo los claims. <see cref="SubscriptionGateTests"/>
/// prueba la decisión pura; aquí se prueba que el filtro está registrado en el pipeline y que la
/// identidad del token le llega bien. Un 403 del filtro trae <c>detail</c>; uno de la autorización, no.
/// </summary>
public sealed class SubscriptionGateHttpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    [Fact]
    public async Task Disabled_gym_reads_but_every_write_gets_403()
    {
        var w = await SeedAsync(GymStatus.Active, paidThroughDaysAgo: 30);   // más de 15 días de gracia
        await using var host = RealIdentityHost();
        var client = await LoggedInAsync(host, w.OwnerEmail);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/gym")).StatusCode);

        var write = await client.PostAsJsonAsync("/api/members", new { });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Contains("inhabilitada", (await write.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Gym_without_subscription_is_not_blocked()
    {
        var w = await SeedAsync(GymStatus.Active, paidThroughDaysAgo: null);
        await using var host = RealIdentityHost();
        var client = await LoggedInAsync(host, w.OwnerEmail);

        var gym = await GetGymAsync(client);
        Assert.True(!gym.TryGetProperty("subscription", out var block) || block.ValueKind == JsonValueKind.Null);

        // El mismo cuerpo vacío que al gym inhabilitado le vale un 403 aquí pasa el filtro y
        // llega a la validación del request.
        var write = await client.PostAsJsonAsync("/api/members", new { });
        Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
    }

    [Fact]
    public async Task Gym_response_carries_the_subscription_block_with_days_until_disabled()
    {
        var w = await SeedAsync(GymStatus.Active, paidThroughDaysAgo: 3);   // en mora: 15 - 3 = 12 días
        await using var host = RealIdentityHost();
        var client = await LoggedInAsync(host, w.OwnerEmail);

        var block = (await GetGymAsync(client)).GetProperty("subscription");
        Assert.Equal("Overdue", block.GetProperty("status").GetString());
        Assert.Equal(12, block.GetProperty("daysUntilDisabled").GetInt32());
    }

    [Fact]
    public async Task Suspended_gym_cannot_even_log_in()
    {
        var w = await SeedAsync(GymStatus.Suspended, paidThroughDaysAgo: -20);   // al día, pero suspendido
        await using var host = RealIdentityHost();

        var login = await host.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(w.OwnerEmail, Password));

        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Contains("suspendida", (await login.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Impersonation_token_reads_but_every_write_gets_403()
    {
        var w = await SeedAsync(GymStatus.Active, paidThroughDaysAgo: -20);
        await using var host = RealIdentityHost();

        var token = host.Services.GetRequiredService<IJwtTokenService>().CreateImpersonationToken(
            new ImpersonationTokenRequest(Guid.CreateVersion7(), "Soporte", w.GymId, Guid.CreateVersion7()));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/gyms")).StatusCode);

        var write = await client.PostAsJsonAsync($"/api/admin/gyms/{w.GymId}/renew", new { });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Contains("solo lectura", (await write.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    /// <summary>
    /// Host con la identidad real: sin <see cref="MutableCurrentUser"/>, el <see cref="CurrentUser"/>
    /// de producción lee <c>sub</c>, <c>gym_id</c> y <c>role</c> del JWT de cada petición.
    /// </summary>
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

        var tokens = await login.Content.ReadFromJsonAsync<AuthTokensResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private static async Task<JsonElement> GetGymAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/gym");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <param name="paidThroughDaysAgo">Días desde el vencimiento (negativo = aún cubierto); <c>null</c> = sin suscripción.</param>
    private async Task<World> SeedAsync(GymStatus manual, int? paidThroughDaysAgo)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();

        // El host HTTP usa el reloj real, así que las fechas se siembran contra el mismo.
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        clock.Unpin();
        var today = clock.Today;

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, superadmin.gym_subscriptions restart identity cascade;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"http-{suffix}@test.local";
        var gym = Gym.Create("Http Gate Gym", $"http-{suffix}");
        if (manual != GymStatus.Trial)
        {
            gym.SetCommercialStatus(manual);
        }

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        if (paidThroughDaysAgo is { } ago)
        {
            db.Add(GymSubscription.Create(gym.Id, GymPlan.Basic, 100_000m, today.AddDays(-60), today.AddDays(-ago)));
        }

        await db.SaveChangesAsync();

        var ownerId = await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
            .CreateGymUserAsync(email, "Http Owner", Password, UserRole.Owner, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(gym.Id, email);
    }

    private sealed record World(Guid GymId, string OwnerEmail);
}
