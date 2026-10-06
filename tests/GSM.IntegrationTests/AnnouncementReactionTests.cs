using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>Etapa 13.2: reacciones a los anuncios y no leídos del feed.</summary>
public sealed class AnnouncementReactionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public async Task Changing_the_reaction_keeps_one_row_and_repeating_it_removes_it()
    {
        var world = await SeedAsync(members: 1);
        var id = await PublishedAsync(world.GymId, "Nueva máquina");
        var ana = world.MemberUsers[0];

        var first = await ReactAsync(world.GymId, ana, id, "Like");
        Assert.Equal("Like", first.MyReaction);
        Assert.Equal(new ReactionCounts(1, 0, 0), first.Reactions);

        var changed = await ReactAsync(world.GymId, ana, id, "Strong");
        Assert.Equal("Strong", changed.MyReaction);
        Assert.Equal(new ReactionCounts(0, 0, 1), changed.Reactions);

        Assert.Equal(1, await CountRowsAsync(id));

        var toggledOff = await ReactAsync(world.GymId, ana, id, "Strong");
        Assert.Null(toggledOff.MyReaction);
        Assert.Equal(ReactionCounts.None, toggledOff.Reactions);
        Assert.Equal(0, await CountRowsAsync(id));
    }

    [Fact]
    public async Task Feed_and_panel_totals_match_the_rows_and_carry_only_the_own_reaction()
    {
        var world = await SeedAsync(members: 3);
        var id = await PublishedAsync(world.GymId, "Evento del sábado");

        await ReactAsync(world.GymId, world.MemberUsers[0], id, "Like");
        await ReactAsync(world.GymId, world.MemberUsers[1], id, "Like");
        await ReactAsync(world.GymId, world.MemberUsers[2], id, "Celebrate");

        using (var scope = Member(world.GymId, world.MemberUsers[2]))
        {
            var item = Assert.Single((await scope.ServiceProvider.GetRequiredService<GetMemberFeedHandler>()
                .HandleAsync(1, 20, default)).Items);
            Assert.Equal(new ReactionCounts(2, 1, 0), item.Reactions);
            Assert.Equal("Celebrate", item.MyReaction);
        }

        using (var scope = Staff(world.GymId))
        {
            var panel = await scope.ServiceProvider.GetRequiredService<GetAnnouncementHandler>().HandleAsync(id, default);
            Assert.Equal(new ReactionCounts(2, 1, 0), panel.Reactions);

            var list = await scope.ServiceProvider.GetRequiredService<ListAnnouncementsHandler>()
                .HandleAsync(new ListAnnouncementsQuery(), default);
            Assert.Equal(new ReactionCounts(2, 1, 0), Assert.Single(list.Items).Reactions);
        }

        // El contrato del feed no expone quién reaccionó: solo totales y la propia.
        var feedProperties = typeof(FeedItem).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(feedProperties, n => n.Contains("Member", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reacting_to_a_draft_an_archived_or_an_expired_announcement_is_not_found()
    {
        var world = await SeedAsync(members: 1);
        var ana = world.MemberUsers[0];

        var draft = await CreateAsync(world.GymId, "Borrador");
        var archived = await PublishedAsync(world.GymId, "Archivado");
        using (var scope = Staff(world.GymId))
        {
            await scope.ServiceProvider.GetRequiredService<ArchiveAnnouncementHandler>().HandleAsync(archived, default);
        }

        var expired = await PublishedAsync(world.GymId, "Caducado", expiresOn: Today);

        await Assert.ThrowsAsync<NotFoundException>(() => ReactAsync(world.GymId, ana, draft, "Like"));
        await Assert.ThrowsAsync<NotFoundException>(() => ReactAsync(world.GymId, ana, archived, "Like"));
        await Assert.ThrowsAsync<NotFoundException>(() => ReactAsync(world.GymId, ana, expired, "Like", Today.AddDays(1)));
        await Assert.ThrowsAsync<ValidationDomainException>(() => ReactAsync(world.GymId, ana, draft, "Love"));
    }

    [Fact]
    public async Task Unread_counts_everything_at_first_then_resets_with_seen_and_counts_newer_ones()
    {
        var world = await SeedAsync(members: 1);
        var ana = world.MemberUsers[0];
        await PublishedAsync(world.GymId, "Uno", publishedOn: Today.AddDays(-2));
        await PublishedAsync(world.GymId, "Dos", publishedOn: Today.AddDays(-1));

        Assert.Equal(2, (await UnreadAsync(world.GymId, ana, Today.AddDays(-1))).Announcements);

        using (var scope = Member(world.GymId, ana, Today.AddDays(-1)))
        {
            await scope.ServiceProvider.GetRequiredService<MarkAnnouncementsSeenHandler>().HandleAsync(default);
        }

        Assert.Equal(0, (await UnreadAsync(world.GymId, ana, Today)).Announcements);

        await PublishedAsync(world.GymId, "Tres", publishedOn: Today);
        var unread = await UnreadAsync(world.GymId, ana, Today);
        Assert.Equal(1, unread.Announcements);
        Assert.Equal(0, unread.Notifications);
    }

    [Fact]
    public async Task Members_of_another_gym_cannot_react_and_a_user_without_member_is_forbidden()
    {
        var world = await SeedAsync(members: 1);
        var other = await SeedAsync(members: 1);
        var id = await PublishedAsync(world.GymId, "Solo de este gym");

        // El Global Query Filter oculta el anuncio al miembro del otro gym.
        await Assert.ThrowsAsync<NotFoundException>(() => ReactAsync(other.GymId, other.MemberUsers[0], id, "Like"));

        // Un usuario con rol miembro sin ficha vinculada no puede reaccionar.
        await Assert.ThrowsAsync<ForbiddenDomainException>(() => ReactAsync(world.GymId, Guid.CreateVersion7(), id, "Like"));
    }

    private async Task<ReactionResponse> ReactAsync(
        Guid gymId, Guid memberUserId, Guid announcementId, string? kind, DateOnly? on = null)
    {
        using var scope = Member(gymId, memberUserId, on ?? Today);
        return await scope.ServiceProvider.GetRequiredService<ReactToAnnouncementHandler>()
            .HandleAsync(announcementId, kind, default);
    }

    private async Task<UnreadCountResponse> UnreadAsync(Guid gymId, Guid memberUserId, DateOnly on)
    {
        using var scope = Member(gymId, memberUserId, on);
        return await scope.ServiceProvider.GetRequiredService<GetUnreadCountHandler>().HandleAsync(default);
    }

    private async Task<int> CountRowsAsync(Guid announcementId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<AnnouncementReaction>().CountAsync(r => r.AnnouncementId == announcementId);
    }

    private async Task<Guid> CreateAsync(Guid gymId, string title, DateOnly? expiresOn = null)
    {
        using var scope = Staff(gymId);
        return (await scope.ServiceProvider.GetRequiredService<CreateAnnouncementHandler>()
            .HandleAsync(new CreateAnnouncementRequest(title, $"Texto de {title}.", false, expiresOn, false), default)).Id;
    }

    private async Task<Guid> PublishedAsync(
        Guid gymId, string title, DateOnly? expiresOn = null, DateOnly? publishedOn = null)
    {
        var id = await CreateAsync(gymId, title, expiresOn);
        using var scope = Staff(gymId, publishedOn ?? Today);
        await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
        return id;
    }

    private IServiceScope Staff(Guid gymId, DateOnly? on = null)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(on ?? Today);
        return scope;
    }

    private IServiceScope Member(Guid gymId, Guid memberUserId, DateOnly? on = null)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(memberUserId, gymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(on ?? Today);
        return scope;
    }

    private async Task<World> SeedAsync(int members)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Reactions Gym", $"rx-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));

        var users = new List<Guid>();
        for (var i = 0; i < members; i++)
        {
            var userId = Guid.CreateVersion7();
            var member = GSM.Domain.Members.Member.Create(gym.Id, "Socio", $"{i}", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-30));
            member.LinkUser(userId);
            db.Add(member);
            users.Add(userId);
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, users);
    }

    private sealed record World(Guid GymId, IReadOnlyList<Guid> MemberUsers);
}
