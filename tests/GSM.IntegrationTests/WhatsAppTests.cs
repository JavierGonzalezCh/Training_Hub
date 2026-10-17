using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Members;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>CO3: WhatsApp por enlace (plantillas por gym, mensaje armado y registro de contacto).</summary>
public sealed class WhatsAppTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private static readonly DateOnly Today = new(2026, 10, 10);
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    [Fact]
    public async Task Templates_are_the_defaults_until_the_owner_edits_one_and_other_gyms_do_not_see_it()
    {
        var w = await SeedAsync();

        using (var scope = Scope(w.GymId, w.StaffId, UserRole.Staff))
        {
            var all = await scope.ServiceProvider.GetRequiredService<ListWhatsAppTemplatesHandler>().HandleAsync(default);
            Assert.Equal(4, all.Templates.Count);
            Assert.All(all.Templates, t => Assert.True(t.IsDefault));
            Assert.Contains("{gym}", all.Variables);
            Assert.Equal(
                "Hola {nombre}, tu plan en {gym} vence el {vence}. ¿Te lo renovamos?",
                all.Templates.Single(t => t.Kind == "ExpiringSoon").Text);
        }

        using (var scope = Scope(w.GymId, w.OwnerId, UserRole.Owner))
        {
            var updated = await scope.ServiceProvider.GetRequiredService<UpdateWhatsAppTemplateHandler>()
                .HandleAsync("ExpiringSoon", new UpdateWhatsAppTemplateRequest("  ¡Ey {nombre}! Se acaba el {vence}  "), default);
            Assert.False(updated.IsDefault);
            Assert.Equal("¡Ey {nombre}! Se acaba el {vence}", updated.Text);
        }

        using (var scope = Scope(w.GymId, w.StaffId, UserRole.Staff))
        {
            var all = await scope.ServiceProvider.GetRequiredService<ListWhatsAppTemplatesHandler>().HandleAsync(default);
            var edited = all.Templates.Single(t => t.Kind == "ExpiringSoon");
            Assert.False(edited.IsDefault);
            Assert.Contains("Se acaba", edited.Text);
            Assert.Equal(3, all.Templates.Count(t => t.IsDefault));
        }

        using (var scope = Scope(w.OtherGymId, w.OtherOwnerId, UserRole.Owner))
        {
            var all = await scope.ServiceProvider.GetRequiredService<ListWhatsAppTemplatesHandler>().HandleAsync(default);
            Assert.All(all.Templates, t => Assert.True(t.IsDefault));
        }

        // Editarla de nuevo actualiza la fila (no duplica) y un texto vacío o un tipo desconocido se rechazan.
        using (var scope = Scope(w.GymId, w.OwnerId, UserRole.Owner))
        {
            var handler = scope.ServiceProvider.GetRequiredService<UpdateWhatsAppTemplateHandler>();
            await handler.HandleAsync("expiringsoon", new UpdateWhatsAppTemplateRequest("Otra {nombre}"), default);
            await Assert.ThrowsAsync<ValidationDomainException>(
                () => handler.HandleAsync("ExpiringSoon", new UpdateWhatsAppTemplateRequest("   "), default));
            await Assert.ThrowsAsync<ValidationDomainException>(
                () => handler.HandleAsync("Nada", new UpdateWhatsAppTemplateRequest("x"), default));
        }
    }

    [Fact]
    public async Task Only_the_owner_edits_templates_over_http_but_staff_can_read_them()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var otherOwner = await LoggedInAsync(host, w.OtherOwnerEmail);
        var body = new UpdateWhatsAppTemplateRequest("Hola {nombre} de {gym}");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PutAsJsonAsync("/api/communications/whatsapp/templates/Birthday", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PutAsJsonAsync("/api/communications/whatsapp/templates/Birthday", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PutAsJsonAsync("/api/communications/whatsapp/templates/Birthday",
                new UpdateWhatsAppTemplateRequest(string.Empty))).StatusCode);

        var seenByStaff = (await staff.GetFromJsonAsync<WhatsAppTemplatesResponse>("/api/communications/whatsapp/templates"))!;
        Assert.Equal("Hola {nombre} de {gym}", seenByStaff.Templates.Single(t => t.Kind == "Birthday").Text);

        var seenByOther = (await otherOwner.GetFromJsonAsync<WhatsAppTemplatesResponse>("/api/communications/whatsapp/templates"))!;
        Assert.True(seenByOther.Templates.Single(t => t.Kind == "Birthday").IsDefault);
    }

    [Fact]
    public async Task The_message_resolves_every_variable_with_real_data_and_a_well_encoded_wa_me_url()
    {
        var w = await SeedAsync();

        using var scope = Scope(w.GymId, w.StaffId, UserRole.Staff);
        var build = scope.ServiceProvider.GetRequiredService<BuildWhatsAppMessageHandler>();

        var soon = await build.HandleAsync(w.MemberWithPhone, "ExpiringSoon", default);
        var endsOn = w.MembershipEnd.ToString("d 'de' MMMM 'de' yyyy", Colombia);
        Assert.Equal($"Hola Ana, tu plan en Gym WhatsApp vence el {endsOn}. ¿Te lo renovamos?", soon.Message);
        Assert.Equal("+573001234567", soon.Phone);
        Assert.StartsWith("https://wa.me/573001234567?text=", soon.Url);
        Assert.Contains("%C2%BF", soon.Url); // "¿" va codificado
        Assert.DoesNotContain(" ", soon.Url);
        Assert.Equal(soon.Message, Uri.UnescapeDataString(soon.Url["https://wa.me/573001234567?text=".Length..]));

        var expired = await build.HandleAsync(w.MemberWithPhone, "Expired", default);
        var graceUntil = w.MembershipEnd.AddDays(3).ToString("d 'de' MMMM 'de' yyyy", Colombia);
        Assert.Equal(
            $"Hola Ana, tu plan venció el {endsOn}. Si renuevas antes del {graceUntil} conservas tu fecha.",
            expired.Message);

        var birthday = await build.HandleAsync(w.MemberWithPhone, "Birthday", default);
        Assert.StartsWith("¡Feliz cumpleaños, Ana!", birthday.Message);
    }

    [Fact]
    public async Task A_member_without_a_valid_phone_cannot_get_a_message_and_a_foreign_member_is_not_found()
    {
        var w = await SeedAsync();

        using (var scope = Scope(w.GymId, w.StaffId, UserRole.Staff))
        {
            var ex = await Assert.ThrowsAsync<ValidationDomainException>(() => scope.ServiceProvider
                .GetRequiredService<BuildWhatsAppMessageHandler>().HandleAsync(w.MemberWithoutPhone, "Birthday", default));
            Assert.Contains("celular", ex.Message);

            var states = await scope.ServiceProvider.GetRequiredService<GetWhatsAppStatesHandler>()
                .HandleAsync([w.MemberWithPhone, w.MemberWithoutPhone], default);
            Assert.True(states.Single(s => s.MemberId == w.MemberWithPhone).HasValidPhone);
            Assert.False(states.Single(s => s.MemberId == w.MemberWithoutPhone).HasValidPhone);
        }

        using (var scope = Scope(w.OtherGymId, w.OtherOwnerId, UserRole.Owner))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
                .GetRequiredService<BuildWhatsAppMessageHandler>().HandleAsync(w.MemberWithPhone, "Birthday", default));
            await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
                .GetRequiredService<RegisterWhatsAppContactHandler>()
                .HandleAsync(new RegisterWhatsAppContactRequest(w.MemberWithPhone, "Birthday"), default));
        }
    }

    [Fact]
    public async Task The_app_invitation_needs_a_valid_invitation_and_carries_the_registration_link()
    {
        var w = await SeedAsync();

        using (var scope = Scope(w.GymId, w.StaffId, UserRole.Staff))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<BuildWhatsAppMessageHandler>().HandleAsync(w.MemberWithPhone, "AppInvitation", default));
        }

        string code;
        using (var scope = Scope(w.GymId, w.StaffId, UserRole.Staff))
        {
            code = (await scope.ServiceProvider.GetRequiredService<InviteMemberHandler>()
                .HandleAsync(w.MemberWithPhone, default)).Code;
        }

        using (var scope = Scope(w.GymId, w.StaffId, UserRole.Staff))
        {
            var message = await scope.ServiceProvider.GetRequiredService<BuildWhatsAppMessageHandler>()
                .HandleAsync(w.MemberWithPhone, "AppInvitation", default);
            Assert.Contains($"/app/registro?code={code}&gym=", message.Message);
            Assert.StartsWith("Hola Ana, activa tu cuenta de Gym WhatsApp: ", message.Message);

            // Vencida (8 días después), ya no sirve.
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today.AddDays(8));
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<BuildWhatsAppMessageHandler>().HandleAsync(w.MemberWithPhone, "AppInvitation", default));
        }
    }

    [Fact]
    public async Task A_registered_contact_shows_who_contacted_the_member_two_days_ago()
    {
        var w = await SeedAsync();

        using (var scope = Scope(w.GymId, w.StaffId, UserRole.Staff))
        {
            var states = await scope.ServiceProvider.GetRequiredService<GetWhatsAppStatesHandler>()
                .HandleAsync([w.MemberWithPhone], default);
            Assert.Null(states.Single().LastContact);

            var contact = await scope.ServiceProvider.GetRequiredService<RegisterWhatsAppContactHandler>()
                .HandleAsync(new RegisterWhatsAppContactRequest(w.MemberWithPhone, "ExpiringSoon"), default);
            Assert.Equal("Ana Staff", contact.ContactedByName);
        }

        using (var scope = Scope(w.GymId, w.OwnerId, UserRole.Owner, Today.AddDays(2)))
        {
            // Un segundo contacto, más reciente, reemplaza al anterior en el estado.
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var before = await scope.ServiceProvider.GetRequiredService<GetWhatsAppStatesHandler>()
                .HandleAsync([w.MemberWithPhone], default);
            var last = before.Single().LastContact!;
            Assert.Equal("ExpiringSoon", last.Kind);
            Assert.Equal("Ana Staff", last.ContactedByName);
            Assert.Equal(2, (clock.UtcNow - last.ContactedAt).Days);

            await scope.ServiceProvider.GetRequiredService<RegisterWhatsAppContactHandler>()
                .HandleAsync(new RegisterWhatsAppContactRequest(w.MemberWithPhone, "Expired"), default);
            var after = (await scope.ServiceProvider.GetRequiredService<GetWhatsAppStatesHandler>()
                .HandleAsync([w.MemberWithPhone], default)).Single().LastContact!;
            Assert.Equal("Expired", after.Kind);
            Assert.Equal("Owner WhatsApp", after.ContactedByName);
        }
    }

    // ---- Semilla y ayudas ----

    private IServiceScope Scope(Guid gymId, Guid userId, UserRole role, DateOnly? on = null)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(on ?? Today);
        return scope;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Gym WhatsApp", $"wa-{suffix}");
        var other = Gym.Create("Otro Gym", $"wb-{suffix}");
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), other, GymFeatureFlags.Default(other.Id), plan);
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var otherEmail = $"ownerb-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Owner WhatsApp", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Ana Staff", Password, UserRole.Staff, gym.Id);
        var otherOwnerId = await provisioning.CreateGymUserAsync(otherEmail, "Owner B", Password, UserRole.Owner, other.Id);
        gym.AssignOwner(ownerId);
        other.AssignOwner(otherOwnerId);

        var withPhone = Member.Create(gym.Id, "Ana", "Gómez", $"d{suffix}1", Today.AddDays(-100), phone: "300 123 4567");
        var withoutPhone = Member.Create(gym.Id, "Luis", "Sin", $"d{suffix}2", Today.AddDays(-100));
        db.AddRange(withPhone, withoutPhone);
        await db.SaveChangesAsync();

        var membership = Membership.Create(gym.Id, withPhone.Id, plan.Id, Today.AddDays(-25), 30);
        db.Add(membership);
        await db.SaveChangesAsync();

        return new World(
            gym.Id, other.Id, ownerId, ownerEmail, staffId, staffEmail, otherOwnerId, otherEmail,
            withPhone.Id, withoutPhone.Id, membership.EndDate);
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email)
    {
        var login = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Tokens!;
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private sealed record World(
        Guid GymId,
        Guid OtherGymId,
        Guid OwnerId,
        string OwnerEmail,
        Guid StaffId,
        string StaffEmail,
        Guid OtherOwnerId,
        string OtherOwnerEmail,
        Guid MemberWithPhone,
        Guid MemberWithoutPhone,
        DateOnly MembershipEnd);
}
