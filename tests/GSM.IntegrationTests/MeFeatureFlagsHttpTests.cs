using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Hueco H4: la app del socio decide si ofrece "crear mi rutina" leyendo
/// <c>members_can_create_routines</c> de <c>GET /api/auth/me</c>, porque
/// <c>GET /api/gym/feature-flags</c> es solo para staff.
/// </summary>
public sealed class MeFeatureFlagsHttpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Me_carries_members_can_create_routines(bool enabled)
    {
        var email = await SeedAsync(enabled);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokensResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.Equal(enabled, me.GetProperty("featureFlags").GetProperty("membersCanCreateRoutines").GetBoolean());
    }

    private async Task<string> SeedAsync(bool membersCanCreateRoutines)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"meflags-{suffix}@test.local";
        var gym = Gym.Create("Me Flags Gym", $"meflags-{suffix}");
        var flags = GymFeatureFlags.Default(gym.Id);
        flags.Update(true, true, true, true, false, true, false, membersCanCreateRoutines);
        db.AddRange(gym, flags);
        await db.SaveChangesAsync();

        var ownerId = await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
            .CreateGymUserAsync(email, "Me Flags Owner", Password, UserRole.Owner, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return email;
    }
}
