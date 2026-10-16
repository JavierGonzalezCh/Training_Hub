using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.Application.Common.Abstractions;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>Etapa 13.1: anuncios del gym (panel) y feed del miembro.</summary>
public sealed class AnnouncementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    /// <summary>Usuario del miembro sembrado en cada gym: desde 13.2 el feed exige una ficha vinculada.</summary>
    private readonly Dictionary<Guid, Guid> _memberUsers = [];

    [Fact]
    public async Task Publishing_twice_or_editing_an_archived_announcement_is_a_conflict()
    {
        var gymId = await SeedGymAsync();
        var id = await CreateAsync(gymId, "Horario de festivo");

        using (var scope = Staff(gymId))
        {
            var published = await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>()
                .HandleAsync(id, default);
            Assert.Equal("Published", published.Status);
            Assert.NotNull(published.PublishedAt);
        }

        using (var scope = Staff(gymId))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default));
        }

        using (var scope = Staff(gymId))
        {
            await scope.ServiceProvider.GetRequiredService<ArchiveAnnouncementHandler>().HandleAsync(id, default);
        }

        using (var scope = Staff(gymId))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<UpdateAnnouncementHandler>()
                .HandleAsync(id, new UpdateAnnouncementRequest("Nuevo título", "Texto", null, false), default));
        }
    }

    [Fact]
    public async Task Publishing_with_a_past_expiry_is_rejected_and_the_domain_guards_the_title()
    {
        var gymId = await SeedGymAsync();
        var id = await CreateAsync(gymId, "Promoción", expiresOn: Today.AddDays(-1));

        using (var scope = Staff(gymId))
        {
            await Assert.ThrowsAsync<ValidationDomainException>(() => scope.ServiceProvider
                .GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default));
        }

        // La red de seguridad del dominio (por HTTP corta antes FluentValidation con 400).
        using (var scope = Staff(gymId))
        {
            await Assert.ThrowsAsync<ValidationDomainException>(() => scope.ServiceProvider
                .GetRequiredService<CreateAnnouncementHandler>()
                .HandleAsync(new CreateAnnouncementRequest("Hi", "Texto", false, null, false), default));
        }
    }

    [Fact]
    public async Task A_fourth_pinned_announcement_is_a_conflict_and_archiving_frees_a_slot()
    {
        var gymId = await SeedGymAsync();
        var first = await CreateAsync(gymId, "Fijado uno", pinned: true);
        await CreateAsync(gymId, "Fijado dos", pinned: true);
        await CreateAsync(gymId, "Fijado tres", pinned: true);
        var fourth = await CreateAsync(gymId, "Cuarto sin fijar");

        using (var scope = Staff(gymId))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<PinAnnouncementHandler>().HandleAsync(fourth, pinned: true, default));
        }

        // Crear ya fijado también cuenta para el límite.
        using (var scope = Staff(gymId))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<CreateAnnouncementHandler>()
                .HandleAsync(new CreateAnnouncementRequest("Quinto fijado", "Texto", true, null, false), default));
        }

        using (var scope = Staff(gymId))
        {
            await scope.ServiceProvider.GetRequiredService<ArchiveAnnouncementHandler>().HandleAsync(first, default);
        }

        using (var scope = Staff(gymId))
        {
            var pinned = await scope.ServiceProvider.GetRequiredService<PinAnnouncementHandler>()
                .HandleAsync(fourth, pinned: true, default);
            Assert.True(pinned.Pinned);
        }
    }

    [Fact]
    public async Task Member_feed_shows_only_published_and_current_announcements_pinned_first()
    {
        var gymId = await SeedGymAsync();
        await CreateAsync(gymId, "Borrador oculto");
        var older = await CreateAsync(gymId, "Publicado antiguo");
        var pinned = await CreateAsync(gymId, "Fijado", pinned: true);
        var expiring = await CreateAsync(gymId, "Caduca hoy", expiresOn: Today);
        var archived = await CreateAsync(gymId, "Archivado");

        await PublishAsync(gymId, older, Today.AddDays(-3));
        await PublishAsync(gymId, pinned, Today.AddDays(-5));
        await PublishAsync(gymId, expiring, Today.AddDays(-1));
        await PublishAsync(gymId, archived, Today.AddDays(-2));
        using (var scope = Staff(gymId))
        {
            await scope.ServiceProvider.GetRequiredService<ArchiveAnnouncementHandler>().HandleAsync(archived, default);
        }

        using (var scope = Member(gymId, Today))
        {
            var feed = await scope.ServiceProvider.GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 20, default);
            Assert.Equal(new[] { "Fijado", "Caduca hoy", "Publicado antiguo" }, feed.Items.Select(i => i.Title));
            Assert.Equal(3, feed.Total);
        }

        // Un día después, el que caducaba hoy ya no se ve.
        using (var scope = Member(gymId, Today.AddDays(1)))
        {
            var feed = await scope.ServiceProvider.GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 20, default);
            Assert.DoesNotContain(feed.Items, i => i.Title == "Caduca hoy");
        }
    }

    [Fact]
    public async Task Feed_is_forbidden_when_communications_are_off_but_staff_can_still_prepare()
    {
        var gymId = await SeedGymAsync(showCommunity: false);
        var id = await CreateAsync(gymId, "Preparado antes de encender");
        await PublishAsync(gymId, id, Today);

        using var scope = Member(gymId, Today);
        await Assert.ThrowsAsync<ForbiddenDomainException>(() => scope.ServiceProvider
            .GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 20, default));
    }

    [Fact]
    public async Task Announcements_are_isolated_between_gyms()
    {
        var gymA = await SeedGymAsync();
        var gymB = await SeedGymAsync();
        var fromA = await CreateAsync(gymA, "Solo del gym A");
        await PublishAsync(gymA, fromA, Today);

        using (var scope = Staff(gymB))
        {
            var list = await scope.ServiceProvider.GetRequiredService<ListAnnouncementsHandler>()
                .HandleAsync(new ListAnnouncementsQuery(), default);
            Assert.Empty(list.Items);
            await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider
                .GetRequiredService<GetAnnouncementHandler>().HandleAsync(fromA, default));
        }

        using (var scope = Member(gymB, Today))
        {
            var feed = await scope.ServiceProvider.GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 20, default);
            Assert.Empty(feed.Items);
        }
    }

    [Fact]
    public async Task Http_validates_the_title_and_members_cannot_manage_announcements()
    {
        var gymId = await SeedGymAsync();
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, GSM.API.Auth.CurrentUser>();
        }));

        HttpClient ClientFor(UserRole role)
        {
            var userId = role == UserRole.Member ? _memberUsers[gymId] : Guid.CreateVersion7();
            var token = host.Services.GetRequiredService<IJwtTokenService>().CreateAccessToken(
                new AuthenticatedUser(userId, "u@test.local", "U", role, gymId));
            var client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            return client;
        }

        var staff = ClientFor(UserRole.Staff);
        var tooShort = await staff.PostAsJsonAsync("/api/announcements",
            new { title = "Hi", body = "Texto", pinned = false, sendEmail = false });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var created = await staff.PostAsJsonAsync("/api/announcements",
            new { title = "Nueva máquina", body = "Llegó la prensa de piernas.", pinned = false, sendEmail = false });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var member = ClientFor(UserRole.Member);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/announcements")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/me/feed")).StatusCode);
    }

    private async Task<Guid> CreateAsync(
        Guid gymId, string title, bool pinned = false, DateOnly? expiresOn = null)
    {
        using var scope = Staff(gymId);
        var created = await scope.ServiceProvider.GetRequiredService<CreateAnnouncementHandler>()
            .HandleAsync(new CreateAnnouncementRequest(title, $"Texto de {title}.", pinned, expiresOn, false), default);
        return created.Id;
    }

    private async Task PublishAsync(Guid gymId, Guid id, DateOnly on)
    {
        using var scope = Staff(gymId, on);
        await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
    }

    private IServiceScope Staff(Guid gymId, DateOnly? on = null)
        => Scope(gymId, UserRole.Staff, on ?? Today);

    private IServiceScope Member(Guid gymId, DateOnly on)
        => Scope(gymId, UserRole.Member, on);

    private IServiceScope Scope(Guid gymId, UserRole role, DateOnly on)
    {
        var scope = factory.CreateScopeWithMigration();
        var userId = role == UserRole.Member ? _memberUsers[gymId] : Guid.CreateVersion7();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(on);
        return scope;
    }

    private async Task<Guid> SeedGymAsync(bool showCommunity = true)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("Comms Gym", $"cm-{Guid.NewGuid():N}"[..16]);
        var flags = GymFeatureFlags.Default(gym.Id);
        if (!showCommunity)
        {
            flags.Update(true, true, true, true, false, true, false);
        }

        var memberUserId = Guid.CreateVersion7();
        var member = GSM.Domain.Members.Member.Create(gym.Id, "Miembro", "Feed", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-30));
        member.LinkUser(memberUserId);

        db.AddRange(gym, flags, member);
        await db.SaveChangesAsync();
        _memberUsers[gym.Id] = memberUserId;
        return gym.Id;
    }
}
