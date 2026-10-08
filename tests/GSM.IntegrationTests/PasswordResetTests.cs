using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class PasswordResetTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Forgot_then_reset_lets_the_user_log_in_with_the_new_password()
    {
        const string email = "owner@reset.local";
        await SeedOwnerAsync(email, "OldPass_12345");

        var emails = factory.Services.GetRequiredService<CapturingEmailSender>();
        emails.Clear();

        // Forgot → se "envía" un email con el token.
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            await scope.ServiceProvider.GetRequiredService<ForgotPasswordHandler>()
                .HandleAsync(new ForgotPasswordRequest(email), default);
        }

        var message = emails.LastTo(email);
        Assert.NotNull(message);
        var link = EmailLinks.Parse(message!);
        Assert.Equal("/restablecer", link.Path);
        Assert.Equal(email, link.Email);
        var token = link.Code!;

        // Reset con el token.
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            await scope.ServiceProvider.GetRequiredService<ResetPasswordHandler>()
                .HandleAsync(new ResetPasswordRequest(email, token, "NewPass_67890"), default);
        }

        // La nueva contraseña funciona, la vieja no.
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var creds = scope.ServiceProvider.GetRequiredService<IUserCredentialsService>();

            Assert.NotNull(await creds.ValidateCredentialsAsync(email, "NewPass_67890"));
            Assert.Null(await creds.ValidateCredentialsAsync(email, "OldPass_12345"));
        }
    }

    [Fact]
    public async Task Forgot_for_unknown_email_does_not_throw_and_sends_nothing()
    {
        var emails = factory.Services.GetRequiredService<CapturingEmailSender>();
        emails.Clear();

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();

        await scope.ServiceProvider.GetRequiredService<ForgotPasswordHandler>()
            .HandleAsync(new ForgotPasswordRequest("nobody@nowhere.local"), default);

        Assert.Empty(emails.Sent);
    }

    private async Task SeedOwnerAsync(string email, string password)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags restart identity cascade;");

        var gym = Gym.Create("Reset Gym", $"reset-{Guid.NewGuid():N}"[..18]);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var provisioning = scope.ServiceProvider
            .GetRequiredService<GSM.Application.Identity.Public.IUserProvisioning>();
        var ownerId = await provisioning.CreateGymUserAsync(email, "Reset Owner", password, UserRole.Owner, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();
    }
}
