using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>CO2: audiencia por segmentos, destinatarios fijados al publicar.</summary>
public sealed class AnnouncementAudienceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    // ---------- filtros ----------

    [Fact]
    public async Task Status_filter_adds_values_and_tag_filter_combines_with_and()
    {
        var w = await SeedAsync();

        // Vencidos o en gracia (suman): Gracia, Vencido y NaturalExpired... sin el al día ni el sin plan.
        var statusOnly = await PreviewAsync(w.GymId, new AnnouncementAudienceDto(["Expired", "Grace"], [], [], []));
        Assert.Equal(Set(w.Grace, w.ExpiredSwimmer, w.Expired), Set(statusOnly));

        // "vencidos o en gracia" y con etiqueta natación → la intersección.
        var both = await PreviewAsync(w.GymId, new AnnouncementAudienceDto(["Expired", "Grace"], [], [], ["Natación"]));
        Assert.Equal(Set(w.ExpiredSwimmer), Set(both));

        var noPlan = await PreviewAsync(w.GymId, new AnnouncementAudienceDto(["NoPlan"], [], [], []));
        Assert.Equal(Set(w.NoPlan), Set(noPlan));
    }

    [Fact]
    public async Task Plan_and_class_filters_use_the_current_plan_and_the_last_thirty_days()
    {
        var w = await SeedAsync();

        var byPlan = await PreviewAsync(w.GymId, new AnnouncementAudienceDto([], [w.GoldPlanId], [], []));
        Assert.Equal(Set(w.Current), Set(byPlan));

        // Reserva reciente cuenta; la de hace 40 días no.
        var byClass = await PreviewAsync(w.GymId, new AnnouncementAudienceDto([], [], [w.ClassId], []));
        Assert.Equal(Set(w.Current, w.Grace), Set(byClass));
    }

    [Fact]
    public async Task The_email_count_before_publishing_uses_the_same_segment_as_the_announcement()
    {
        var w = await SeedAsync();
        using var scope = Staff(w.GymId);
        var handler = scope.ServiceProvider.GetRequiredService<GetEmailAudienceHandler>();

        // CO2-4: antes contaba a todos los activos con email (5) aunque el anuncio fuera solo al plan Oro.
        var everyone = await handler.HandleAsync(new EmailAudienceRequest(AnnouncementCategory.GymNews), default);
        Assert.Equal(w.ActiveCount, everyone.Recipients);

        var gold = await handler.HandleAsync(
            new EmailAudienceRequest(AnnouncementCategory.GymNews, new AnnouncementAudienceDto([], [w.GoldPlanId], [], [])),
            default);
        Assert.Equal(1, gold.Recipients);

        var expiredOrGrace = await handler.HandleAsync(
            new EmailAudienceRequest(AnnouncementCategory.GymNews, new AnnouncementAudienceDto(["Expired", "Grace"], [], [], [])),
            default);
        Assert.Equal(3, expiredOrGrace.Recipients);
    }

    [Fact]
    public async Task Inactive_members_are_never_in_the_audience_not_even_for_everyone()
    {
        var w = await SeedAsync();

        var everyone = await PreviewAsync(w.GymId, new AnnouncementAudienceDto([], [], [], []));

        Assert.DoesNotContain(w.Inactive, everyone.Members.Select(m => m.Id));
        Assert.Equal(w.ActiveCount, everyone.Count);
        Assert.Contains(everyone.Members, m => m.Name.StartsWith("Al día"));
    }

    [Fact]
    public async Task An_unknown_status_is_rejected_with_a_domain_error()
    {
        var w = await SeedAsync();

        await Assert.ThrowsAsync<ValidationDomainException>(
            () => PreviewAsync(w.GymId, new AnnouncementAudienceDto(["Vencidos"], [], [], [])));
    }

    // ---------- destinatarios fijados al publicar ----------

    [Fact]
    public async Task Recipients_are_fixed_at_publish_even_if_the_member_renews_or_a_new_one_joins()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, new AnnouncementAudienceDto(["Expired"], [], [], []));

        AnnouncementResponse published;
        using (var scope = Staff(w.GymId))
        {
            published = await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
        }

        Assert.Equal(2, published.RecipientCount);
        Assert.Equal(["Expired"], published.Audience.Statuses);

        // El vencido renueva al día siguiente y entra un miembro nuevo también vencido.
        await RenewAsync(w.GymId, w.Expired);
        var late = await AddMemberAsync(w.GymId, "Tarde", expiredMembership: true);

        using (var scope = Staff(w.GymId))
        {
            var recipients = await scope.ServiceProvider.GetRequiredService<GetAnnouncementRecipientsHandler>().HandleAsync(id, default);
            Assert.Equal(2, recipients.Count);
            Assert.Equal(Set(w.Expired, w.ExpiredSwimmer), recipients.Members.Select(m => m.Id).ToHashSet());
            Assert.DoesNotContain(late.MemberId, recipients.Members.Select(m => m.Id));
        }

        // El que renovó lo sigue viendo en su feed; el nuevo, no.
        Assert.Contains("Segmento", await FeedTitlesAsync(w.GymId, w.UserOf[w.Expired], "Segmento"));
        Assert.DoesNotContain("Segmento", await FeedTitlesAsync(w.GymId, late.UserId, "Segmento"));
        // Quien nunca estuvo en el segmento tampoco lo ve.
        Assert.DoesNotContain("Segmento", await FeedTitlesAsync(w.GymId, w.UserOf[w.Current], "Segmento"));
    }

    [Fact]
    public async Task An_announcement_for_everyone_is_seen_by_all_including_those_who_join_later()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, audience: null, title: "Para todos");
        using (var scope = Staff(w.GymId))
        {
            var published = await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
            Assert.Null(published.RecipientCount);
            Assert.False(published.Audience.Statuses.Count > 0);
        }

        var late = await AddMemberAsync(w.GymId, "Recién llegado", expiredMembership: false);

        Assert.Contains("Para todos", await FeedTitlesAsync(w.GymId, w.UserOf[w.Current], "Para todos"));
        Assert.Contains("Para todos", await FeedTitlesAsync(w.GymId, late.UserId, "Para todos"));
        Assert.Equal(0, await CountRecipientRowsAsync(id));
    }

    [Fact]
    public async Task Only_the_recipients_get_a_notice_and_a_non_recipient_cannot_react()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, new AnnouncementAudienceDto([], [w.GoldPlanId], [], []));
        using (var scope = Staff(w.GymId))
        {
            await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
        }

        var noticed = await NoticeMemberIdsAsync(w.GymId, id);
        Assert.Equal(Set(w.Current), noticed);

        using var other = MemberScope(w.GymId, w.UserOf[w.Grace]);
        await Assert.ThrowsAsync<NotFoundException>(() => other.ServiceProvider
            .GetRequiredService<ReactToAnnouncementHandler>().HandleAsync(id, "Like", default));
        using var mine = MemberScope(w.GymId, w.UserOf[w.Current]);
        var reacted = await mine.ServiceProvider.GetRequiredService<ReactToAnnouncementHandler>().HandleAsync(id, "Like", default);
        Assert.Equal("Like", reacted.MyReaction);
    }

    [Fact]
    public async Task Unread_count_only_counts_announcements_the_member_can_see()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, new AnnouncementAudienceDto([], [w.GoldPlanId], [], []));
        using (var scope = Staff(w.GymId))
        {
            await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
        }

        using (var scope = MemberScope(w.GymId, w.UserOf[w.Current]))
        {
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<GetUnreadCountHandler>().HandleAsync(default)).Announcements);
        }

        using (var scope = MemberScope(w.GymId, w.UserOf[w.Grace]))
        {
            Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<GetUnreadCountHandler>().HandleAsync(default)).Announcements);
        }
    }

    // ---------- reglas del anuncio ----------

    [Fact]
    public async Task Publishing_a_segment_without_members_is_rejected_and_nothing_is_published()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, new AnnouncementAudienceDto([], [], [], ["no-existe"]));

        using (var scope = Staff(w.GymId))
        {
            await Assert.ThrowsAsync<ValidationDomainException>(
                () => scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default));
        }

        using (var scope = Staff(w.GymId))
        {
            var announcement = await scope.ServiceProvider.GetRequiredService<GetAnnouncementHandler>().HandleAsync(id, default);
            Assert.Equal("Draft", announcement.Status);
        }

        Assert.Equal(0, await CountRecipientRowsAsync(id));
    }

    [Fact]
    public async Task The_audience_can_change_in_draft_but_not_after_publishing()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, new AnnouncementAudienceDto(["Expired"], [], [], []));

        using (var scope = Staff(w.GymId))
        {
            var updated = await scope.ServiceProvider.GetRequiredService<UpdateAnnouncementHandler>().HandleAsync(
                id,
                new UpdateAnnouncementRequest("Segmento", "Texto", null, false, Audience: new AnnouncementAudienceDto([], [w.GoldPlanId], [], [])),
                default);
            Assert.Empty(updated.Audience.Statuses);
            Assert.Equal([w.GoldPlanId], updated.Audience.PlanIds);
            await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
        }

        using (var scope = Staff(w.GymId))
        {
            var after = await scope.ServiceProvider.GetRequiredService<UpdateAnnouncementHandler>().HandleAsync(
                id,
                new UpdateAnnouncementRequest("Segmento", "Texto nuevo", null, false, Audience: new AnnouncementAudienceDto(["Grace"], [], [], [])),
                default);
            Assert.Equal([w.GoldPlanId], after.Audience.PlanIds); // la audiencia ya no se tocó
            Assert.Empty(after.Audience.Statuses);
            Assert.Equal("Texto nuevo", after.Body);
            Assert.Equal(1, after.RecipientCount);
        }
    }

    [Fact]
    public async Task Tags_available_for_the_filter_are_the_ones_in_use_by_active_members()
    {
        var w = await SeedAsync();

        using var scope = Staff(w.GymId);
        var tags = await scope.ServiceProvider.GetRequiredService<GetAudienceTagsHandler>().HandleAsync(default);

        Assert.Contains("Natación", tags.Tags);
        Assert.DoesNotContain("solo-inactivo", tags.Tags);
    }

    // ---------- helpers ----------

    private static HashSet<Guid> Set(params Guid[] ids) => ids.ToHashSet();

    private static HashSet<Guid> Set(AudiencePreviewResponse preview) => preview.Members.Select(m => m.Id).ToHashSet();

    private async Task<AudiencePreviewResponse> PreviewAsync(Guid gymId, AnnouncementAudienceDto audience)
    {
        using var scope = Staff(gymId);
        return await scope.ServiceProvider.GetRequiredService<PreviewAnnouncementAudienceHandler>().HandleAsync(audience, default);
    }

    private async Task<Guid> CreateAsync(Guid gymId, AnnouncementAudienceDto? audience, string title = "Segmento")
    {
        using var scope = Staff(gymId);
        return (await scope.ServiceProvider.GetRequiredService<CreateAnnouncementHandler>().HandleAsync(
            new CreateAnnouncementRequest(title, $"Texto de {title}.", false, null, false, Audience: audience), default)).Id;
    }

    private async Task<IReadOnlyList<string>> FeedTitlesAsync(Guid gymId, Guid userId, string _)
    {
        using var scope = MemberScope(gymId, userId);
        var feed = await scope.ServiceProvider.GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 50, default);
        return feed.Items.Select(i => i.Title).ToList();
    }

    private async Task<int> CountRecipientRowsAsync(Guid announcementId)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<AnnouncementRecipient>().CountAsync(r => r.AnnouncementId == announcementId);
    }

    private async Task<HashSet<Guid>> NoticeMemberIdsAsync(Guid gymId, Guid announcementId)
    {
        using var scope = System();
        var ids = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .Where(n => n.GymId == gymId && n.Type == NotificationType.Announcement && n.ReferenceId == announcementId)
            .Select(n => n.RecipientMemberId)
            .ToListAsync();
        return ids.Where(m => m.HasValue).Select(m => m!.Value).ToHashSet();
    }

    private async Task RenewAsync(Guid gymId, Guid memberId)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var plan = await db.Set<MembershipPlan>().FirstAsync(p => p.GymId == gymId);
        db.Add(Membership.Create(gymId, memberId, plan.Id, Today, 30));
        await db.SaveChangesAsync();
    }

    private async Task<(Guid MemberId, Guid UserId)> AddMemberAsync(Guid gymId, string name, bool expiredMembership)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var userId = Guid.CreateVersion7();
        var member = Member.Create(gymId, name, "X", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-400));
        member.LinkUser(userId);
        db.Add(member);
        if (expiredMembership)
        {
            var plan = await db.Set<MembershipPlan>().FirstAsync(p => p.GymId == gymId);
            db.Add(Membership.Create(gymId, member.Id, plan.Id, Today.AddDays(-300), 100));
        }

        await db.SaveChangesAsync();
        return (member.Id, userId);
    }

    /// <summary>
    /// Gym con miembros en cada situación (hoy = 2026-10-06, gracia 3 días, "por vencer" 7):
    /// Current (plan Oro, al día, reservó la clase hace 5 días), Grace (venció ayer, reservó la clase),
    /// Expired (vencida hace mucho), ExpiredSwimmer (vencida + etiqueta Natación; reservó la clase hace 40 días),
    /// NoPlan (sin membresía) e Inactive (de baja, con etiqueta propia).
    /// </summary>
    private async Task<World> SeedAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Audience Gym", $"au-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));

        var gold = MembershipPlan.Create(gym.Id, "Oro", null, 100_000m, 60);
        var basic = MembershipPlan.Create(gym.Id, "Básico", null, 50_000m, 100);
        db.AddRange(gold, basic);

        var userOf = new Dictionary<Guid, Guid>();
        Member Add(string first, string[]? tags = null)
        {
            var userId = Guid.CreateVersion7();
            var member = Member.Create(
                gym.Id,
                new MemberProfile(
                    first, "Test", DocumentType.Cc, $"doc-{Guid.NewGuid():N}"[..12], null,
                    Email: $"aud-{Guid.NewGuid():N}@test.local", Tags: tags),
                Today.AddDays(-400), Today.AddDays(-400));
            member.LinkUser(userId);
            db.Add(member);
            userOf[member.Id] = userId;
            return member;
        }

        var current = Add("Al día");
        var grace = Add("En gracia");
        var expired = Add("Vencido");
        var swimmer = Add("Nadador", ["Natación"]);
        var noPlan = Add("Sin plan");
        var inactive = Add("De baja", ["solo-inactivo"]);
        inactive.Deactivate();

        db.Add(Membership.Create(gym.Id, current.Id, gold.Id, Today.AddDays(-10), 60));
        db.Add(Membership.Create(gym.Id, grace.Id, basic.Id, Today.AddDays(-100), 100)); // termina ayer
        db.Add(Membership.Create(gym.Id, expired.Id, basic.Id, Today.AddDays(-300), 100));
        db.Add(Membership.Create(gym.Id, swimmer.Id, basic.Id, Today.AddDays(-300), 100));

        var gymClass = GymClass.Create(gym.Id, Today.AddDays(-90), new GymClassDraft(
            "Spinning", null, null, 10, new TimeOnly(7, 0), 60, true,
            [ClassDayOfWeek.Monday, ClassDayOfWeek.Tuesday, ClassDayOfWeek.Wednesday, ClassDayOfWeek.Thursday,
             ClassDayOfWeek.Friday, ClassDayOfWeek.Saturday, ClassDayOfWeek.Sunday], null));
        db.Add(gymClass);

        var now = DateTimeOffset.UtcNow;
        ClassSession Session(DateOnly day)
        {
            var s = ClassSession.Materialize(gym.Id, gymClass.Id, day, new TimeOnly(7, 0), 60, null, 10);
            db.Add(s);
            return s;
        }

        var recent = Session(Today.AddDays(-5));
        var old = Session(Today.AddDays(-40));
        db.Add(ClassBooking.Book(gym.Id, recent.Id, current.Id, now));
        db.Add(ClassBooking.Book(gym.Id, recent.Id, grace.Id, now));
        db.Add(ClassBooking.AttendDirectly(gym.Id, old.Id, swimmer.Id, now));

        await db.SaveChangesAsync();
        return new World(
            gym.Id, gold.Id, gymClass.Id, current.Id, grace.Id, expired.Id, swimmer.Id, noPlan.Id, inactive.Id, 5, userOf);
    }

    private sealed record World(
        Guid GymId, Guid GoldPlanId, Guid ClassId, Guid Current, Guid Grace, Guid Expired, Guid ExpiredSwimmer,
        Guid NoPlan, Guid Inactive, int ActiveCount, IReadOnlyDictionary<Guid, Guid> UserOf);

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private IServiceScope Staff(Guid gymId) => Scope(gymId, Guid.CreateVersion7(), UserRole.Staff);

    private IServiceScope MemberScope(Guid gymId, Guid userId) => Scope(gymId, userId, UserRole.Member);

    private IServiceScope Scope(Guid gymId, Guid userId, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }
}
