using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Communications;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Common;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// CO4: programar la publicación de un anuncio (la tarea periódica lo publica, sin usuario, cuando llega la hora)
/// e imagen del anuncio detrás de <c>IFileStorage</c> (R2 sin configurar: aquí un almacenamiento falso).
/// </summary>
public sealed class AnnouncementSchedulingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);

    private static readonly SystemClock BogotaClock = new(
        Options.Create(new ClockOptions { TimeZone = "America/Bogota" }));

    // ---------- programar, editar y cancelar ----------

    [Fact]
    public async Task A_draft_can_be_scheduled_rescheduled_edited_and_cancelled_before_it_goes_out()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, "Programado");

        AnnouncementResponse scheduled;
        using (var scope = Staff(w.GymId, T0))
        {
            scheduled = await scope.ServiceProvider.GetRequiredService<ScheduleAnnouncementHandler>()
                .HandleAsync(id, new ScheduleAnnouncementRequest(T0.AddHours(2)), default);
        }

        Assert.Equal("Scheduled", scheduled.Status);
        Assert.Equal(T0.AddHours(2), scheduled.ScheduledFor);
        Assert.Null(scheduled.PublishedAt);

        // Editable mientras está programado: contenido y audiencia; reprogramar mueve la hora.
        using (var scope = Staff(w.GymId, T0))
        {
            var edited = await scope.ServiceProvider.GetRequiredService<UpdateAnnouncementHandler>().HandleAsync(
                id,
                new UpdateAnnouncementRequest(
                    "Programado editado", "Texto nuevo", null, false,
                    Audience: new AnnouncementAudienceDto([], [], [], ["vip"])),
                default);
            Assert.Equal("Scheduled", edited.Status);
            Assert.Equal("Programado editado", edited.Title);
            Assert.Equal(["vip"], edited.Audience.Tags);

            var moved = await scope.ServiceProvider.GetRequiredService<ScheduleAnnouncementHandler>()
                .HandleAsync(id, new ScheduleAnnouncementRequest(T0.AddHours(5)), default);
            Assert.Equal(T0.AddHours(5), moved.ScheduledFor);
        }

        // Cancelar: vuelve a borrador, sin hora.
        using (var scope = Staff(w.GymId, T0))
        {
            var cancelled = await scope.ServiceProvider.GetRequiredService<UnscheduleAnnouncementHandler>()
                .HandleAsync(id, default);
            Assert.Equal("Draft", cancelled.Status);
            Assert.Null(cancelled.ScheduledFor);
        }

        // Cancelado, la tarea no lo publica aunque pase la hora.
        Assert.Equal(0, await RunJobAtAsync(T0.AddHours(6)));
        Assert.Equal("Draft", await StatusAsync(id));
    }

    [Fact]
    public async Task Scheduling_rejects_a_past_time_a_published_announcement_and_a_caducity_before_the_time()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, "Reglas");

        using (var scope = Staff(w.GymId, T0))
        {
            var schedule = scope.ServiceProvider.GetRequiredService<ScheduleAnnouncementHandler>();
            await Assert.ThrowsAsync<ValidationDomainException>(
                () => schedule.HandleAsync(id, new ScheduleAnnouncementRequest(T0.AddMinutes(-1)), default));
        }

        var expiring = await CreateAsync(w.GymId, "Caduca antes", expiresOn: DateOnly.FromDateTime(BogotaClock.ToBusinessLocal(T0)));
        using (var scope = Staff(w.GymId, T0))
        {
            // Dos días después de la caducidad.
            await Assert.ThrowsAsync<ValidationDomainException>(
                () => scope.ServiceProvider.GetRequiredService<ScheduleAnnouncementHandler>()
                    .HandleAsync(expiring, new ScheduleAnnouncementRequest(T0.AddDays(2)), default));
        }

        using (var scope = Staff(w.GymId, T0))
        {
            await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
        }

        using (var scope = Staff(w.GymId, T0))
        {
            await Assert.ThrowsAsync<ConflictException>(
                () => scope.ServiceProvider.GetRequiredService<ScheduleAnnouncementHandler>()
                    .HandleAsync(id, new ScheduleAnnouncementRequest(T0.AddHours(1)), default));
            await Assert.ThrowsAsync<ConflictException>(
                () => scope.ServiceProvider.GetRequiredService<UnscheduleAnnouncementHandler>().HandleAsync(id, default));
        }
    }

    // ---------- la tarea publica a la hora ----------

    [Fact]
    public async Task The_job_publishes_at_the_time_and_never_before_exactly_once()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, "A la hora");
        await ScheduleAsync(w.GymId, id, T0.AddHours(2));

        Assert.Equal(0, await RunJobAtAsync(T0.AddHours(2).AddSeconds(-1)));
        Assert.Equal("Scheduled", await StatusAsync(id));

        var late = T0.AddHours(2).AddMinutes(7);
        Assert.Equal(1, await RunJobAtAsync(late));
        var published = await GetAsync(w.GymId, id);
        Assert.Equal("Published", published.Status);
        Assert.Equal(late, published.PublishedAt);
        Assert.Null(published.ScheduledFor);

        // Segunda ejecución: nada nuevo, un solo aviso por miembro.
        Assert.Equal(0, await RunJobAtAsync(late.AddMinutes(5)));
        var notices = await NoticeMemberIdsAsync(w.GymId, id);
        Assert.Equal(w.Active.ToHashSet(), notices);
        Assert.Equal(w.Active.Count, await NoticeCountAsync(id));
    }

    [Fact]
    public async Task The_audience_is_calculated_when_it_goes_out_not_when_it_was_scheduled()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, "Segmento", new AnnouncementAudienceDto([], [], [], ["vip"]));
        await ScheduleAsync(w.GymId, id, T0.AddHours(2));

        // Entra otro miembro vip después de programar y antes de la hora.
        var late = await AddMemberAsync(w.GymId, "Tardío", ["vip"]);

        await RunJobAtAsync(T0.AddHours(3));

        var published = await GetAsync(w.GymId, id);
        Assert.Equal(2, published.RecipientCount);
        using var scope = SystemScope(T0.AddHours(3));
        var recipients = await scope.ServiceProvider.GetRequiredService<IAnnouncementRepository>()
            .GetRecipientIdsAsync(id, default);
        Assert.Equal(new HashSet<Guid>([w.Vip, late]), recipients.ToHashSet());
        Assert.Equal(new HashSet<Guid>([w.Vip, late]), await NoticeMemberIdsAsync(w.GymId, id));
    }

    [Fact]
    public async Task An_announcement_from_another_gym_is_never_mixed_in_when_the_job_runs_without_user()
    {
        var a = await SeedAsync();
        var b = await SeedAsync();
        var idA = await CreateAsync(a.GymId, "Gym A", new AnnouncementAudienceDto([], [], [], ["vip"]));
        var idB = await CreateAsync(b.GymId, "Gym B", new AnnouncementAudienceDto([], [], [], ["vip"]));
        var idAll = await CreateAsync(b.GymId, "Gym B todos");
        await ScheduleAsync(a.GymId, idA, T0.AddHours(1));
        await ScheduleAsync(b.GymId, idB, T0.AddHours(1));
        await ScheduleAsync(b.GymId, idAll, T0.AddHours(1));

        Assert.Equal(3, await RunJobAtAsync(T0.AddHours(2)));

        Assert.Equal(new HashSet<Guid>([a.Vip]), await NoticeMemberIdsAsync(a.GymId, idA));
        Assert.Equal(new HashSet<Guid>([b.Vip]), await NoticeMemberIdsAsync(b.GymId, idB));
        Assert.Equal(b.Active.ToHashSet(), await NoticeMemberIdsAsync(b.GymId, idAll));
        Assert.Equal(1, (await GetAsync(a.GymId, idA)).RecipientCount);
        Assert.Equal(1, (await GetAsync(b.GymId, idB)).RecipientCount);

        // Y el feed de un miembro de A no ve lo de B.
        using var member = MemberScope(a.GymId, a.UserOf[a.Vip], T0.AddHours(2));
        var feed = await member.ServiceProvider.GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 50, default);
        Assert.Equal(["Gym A"], feed.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task A_scheduled_announcement_that_can_no_longer_go_out_returns_to_draft_without_blocking_the_rest()
    {
        var w = await SeedAsync();
        var empty = await CreateAsync(w.GymId, "Sin nadie", new AnnouncementAudienceDto([], [], [], ["vip"]));
        var fine = await CreateAsync(w.GymId, "Sale igual");
        await ScheduleAsync(w.GymId, empty, T0.AddHours(1));
        await ScheduleAsync(w.GymId, fine, T0.AddHours(1));

        // Antes de la hora, el único vip se da de baja: la audiencia queda vacía.
        await DeactivateAsync(w.Vip);

        Assert.Equal(1, await RunJobAtAsync(T0.AddHours(2)));

        Assert.Equal("Draft", await StatusAsync(empty));
        Assert.Equal("Published", await StatusAsync(fine));
        // Ya no está programado: no se reintenta en cada ejecución.
        Assert.Equal(0, await RunJobAtAsync(T0.AddHours(3)));
    }

    [Fact]
    public async Task A_scheduled_announcement_that_goes_out_inside_the_quiet_hours_publishes_now_but_its_notices_wait_until_7h()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, "De noche");
        // T0 = 10:00 en Bogotá; +12 h son las 22:00 locales.
        var chosen = T0.AddHours(12);
        await ScheduleAsync(w.GymId, id, chosen);

        // La tarea lo recoge unos minutos tarde: el anuncio se publica de verdad...
        var jobAt = chosen.AddMinutes(4);
        Assert.Equal(1, await RunJobAtAsync(jobAt));
        Assert.Equal("Published", await StatusAsync(id));

        // ...pero sus avisos (y con ellos el push y el correo) quedan retenidos hasta las 7:00 locales.
        await RunDispatcherAtAsync(jobAt, w.GymId);
        var sevenNextDay = BogotaClock
            .BusinessDayRange(DateOnly.FromDateTime(BogotaClock.ToBusinessLocal(chosen)).AddDays(1)).Start.AddHours(7);
        using (var scope = SystemScope(jobAt))
        {
            var notices = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
                .Where(n => n.GymId == w.GymId && n.Type == NotificationType.Announcement && n.ReferenceId == id)
                .ToListAsync();
            Assert.Equal(w.Active.Count, notices.Count);
            Assert.All(notices, n => Assert.Equal(NotificationStatus.Held, n.Status));
            Assert.All(notices, n => Assert.Equal(sevenNextDay, n.ReleaseAt));
        }

        await RunDispatcherAtAsync(sevenNextDay, w.GymId);
        using (var scope = SystemScope(sevenNextDay))
        {
            var statuses = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
                .Where(n => n.GymId == w.GymId && n.Type == NotificationType.Announcement && n.ReferenceId == id)
                .Select(n => n.Status).ToListAsync();
            Assert.Equal(w.Active.Count, statuses.Count);
            Assert.All(statuses, st => Assert.Equal(NotificationStatus.Sent, st));
        }
    }

    private async Task RunDispatcherAtAsync(DateTimeOffset instant, Guid gymId)
    {
        using var scope = SystemScope(instant);
        await scope.ServiceProvider.GetRequiredService<GSM.Application.Notifications.NotificationDispatchRunner>()
            .RunOnceAsync(onlyGymId: gymId);
    }

    // ---------- imagen ----------

    [Fact]
    public async Task The_image_is_stored_replaced_removed_and_shown_in_the_member_feed()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, "Con imagen");
        var storage = new FakeStorage();

        string first;
        using (var scope = Staff(w.GymId, T0))
        {
            var upload = UploadHandler(scope, storage);
            var uploaded = await upload.HandleAsync(id, Png(), default);
            first = uploaded.ImageUrl!;
            Assert.StartsWith("https://files.test/announcements/", first);
        }

        using (var scope = Staff(w.GymId, T0))
        {
            await scope.ServiceProvider.GetRequiredService<PublishAnnouncementHandler>().HandleAsync(id, default);
        }

        using (var member = MemberScope(w.GymId, w.UserOf[w.Active[0]], T0))
        {
            var feed = await member.ServiceProvider.GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 50, default);
            Assert.Equal(first, Assert.Single(feed.Items).ImageUrl);
        }

        // Reemplazar borra la anterior (también publicado: cambia lo que se ve, nada más).
        string second;
        using (var scope = Staff(w.GymId, T0))
        {
            second = (await UploadHandler(scope, storage).HandleAsync(id, Png(), default)).ImageUrl!;
        }

        Assert.NotEqual(first, second);
        Assert.Equal([first], storage.Deleted);

        using (var scope = Staff(w.GymId, T0))
        {
            var removed = await scope.ServiceProvider.GetRequiredService<RemoveAnnouncementImageHandler>().HandleAsync(id, default);
            Assert.Null(removed.ImageUrl);
        }

        // (el handler real usa IFileStorage deshabilitado en tests; aquí solo importa que quite la referencia)
        using (var scope = Staff(w.GymId, T0))
        {
            Assert.Null((await scope.ServiceProvider.GetRequiredService<GetAnnouncementHandler>().HandleAsync(id, default)).ImageUrl);
        }
    }

    [Fact]
    public async Task The_image_rejects_bad_files_and_archived_announcements()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, "Imagen mala");
        var storage = new FakeStorage();

        using (var scope = Staff(w.GymId, T0))
        {
            var upload = UploadHandler(scope, storage);
            await Assert.ThrowsAsync<ValidationDomainException>(() => upload.HandleAsync(
                id, new FileToStore(new MemoryStream([1, 2, 3]), "x.gif", "image/gif", 3), default));
            await Assert.ThrowsAsync<ValidationDomainException>(() => upload.HandleAsync(
                id, new FileToStore(new MemoryStream([1]), "big.png", "image/png", ImageUploadRules.MaxBytes + 1), default));
            Assert.Empty(storage.Saved);
        }

        using (var scope = Staff(w.GymId, T0))
        {
            await scope.ServiceProvider.GetRequiredService<ArchiveAnnouncementHandler>().HandleAsync(id, default);
        }

        using (var scope = Staff(w.GymId, T0))
        {
            await Assert.ThrowsAsync<ConflictException>(() => UploadHandler(scope, storage).HandleAsync(id, Png(), default));
        }
    }

    [Fact]
    public async Task A_scheduled_announcement_keeps_its_image_when_it_goes_out()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w.GymId, "Programado con imagen");
        var storage = new FakeStorage();
        using (var scope = Staff(w.GymId, T0))
        {
            await UploadHandler(scope, storage).HandleAsync(id, Png(), default);
        }

        await ScheduleAsync(w.GymId, id, T0.AddHours(1));
        await RunJobAtAsync(T0.AddHours(2));

        using var member = MemberScope(w.GymId, w.UserOf[w.Active[0]], T0.AddHours(2));
        var feed = await member.ServiceProvider.GetRequiredService<GetMemberFeedHandler>().HandleAsync(1, 50, default);
        Assert.StartsWith("https://files.test/announcements/", Assert.Single(feed.Items).ImageUrl);
    }

    // ---------- helpers ----------

    private static UploadAnnouncementImageHandler UploadHandler(IServiceScope scope, FakeStorage storage)
        => new(
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            scope.ServiceProvider.GetRequiredService<IAnnouncementRepository>(),
            scope.ServiceProvider.GetRequiredService<IAnnouncementReactionRepository>(),
            storage,
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

    private static FileToStore Png() => new(new MemoryStream([0x89, 0x50, 0x4E, 0x47]), "a.png", "image/png", 4);

    private async Task<int> RunJobAtAsync(DateTimeOffset instant)
    {
        using var scope = SystemScope(instant);
        return (await scope.ServiceProvider.GetRequiredService<RunCommunicationsJobHandler>().HandleAsync(default)).Announcements;
    }

    private async Task<Guid> CreateAsync(
        Guid gymId, string title, AnnouncementAudienceDto? audience = null, DateOnly? expiresOn = null)
    {
        using var scope = Staff(gymId, T0);
        return (await scope.ServiceProvider.GetRequiredService<CreateAnnouncementHandler>().HandleAsync(
            new CreateAnnouncementRequest(title, $"Texto de {title}.", false, expiresOn, false, Audience: audience), default)).Id;
    }

    private async Task ScheduleAsync(Guid gymId, Guid id, DateTimeOffset at)
    {
        using var scope = Staff(gymId, T0);
        await scope.ServiceProvider.GetRequiredService<ScheduleAnnouncementHandler>()
            .HandleAsync(id, new ScheduleAnnouncementRequest(at), default);
    }

    private async Task<AnnouncementResponse> GetAsync(Guid gymId, Guid id)
    {
        using var scope = Staff(gymId, T0);
        return await scope.ServiceProvider.GetRequiredService<GetAnnouncementHandler>().HandleAsync(id, default);
    }

    private async Task<string> StatusAsync(Guid id)
    {
        using var scope = SystemScope(T0);
        return (await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Announcement>()
            .IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == id)).Status.ToString();
    }

    private async Task<HashSet<Guid>> NoticeMemberIdsAsync(Guid gymId, Guid announcementId)
    {
        using var scope = SystemScope(T0);
        var ids = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .Where(n => n.GymId == gymId && n.Type == NotificationType.Announcement && n.ReferenceId == announcementId)
            .Select(n => n.RecipientMemberId)
            .ToListAsync();
        return ids.Where(m => m.HasValue).Select(m => m!.Value).ToHashSet();
    }

    private async Task<int> NoticeCountAsync(Guid announcementId)
    {
        using var scope = SystemScope(T0);
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .CountAsync(n => n.Type == NotificationType.Announcement && n.ReferenceId == announcementId);
    }

    private async Task DeactivateAsync(Guid memberId)
    {
        using var scope = SystemScope(T0);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        (await db.Set<Member>().IgnoreQueryFilters().FirstAsync(m => m.Id == memberId)).Deactivate();
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AddMemberAsync(Guid gymId, string name, string[]? tags = null)
    {
        using var scope = SystemScope(T0);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var joined = new DateOnly(2026, 1, 5);
        var member = Member.Create(
            gymId,
            new MemberProfile(name, "Test", DocumentType.Cc, $"doc-{Guid.NewGuid():N}"[..12], null, Tags: tags),
            joined, joined);
        member.LinkUser(Guid.CreateVersion7());
        db.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    /// <summary>Un gym con tres miembros activos: uno con la etiqueta vip y dos sin ella.</summary>
    private async Task<World> SeedAsync()
    {
        Guid gymId;
        using (var scope = SystemScope(T0))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var gym = Gym.Create("CO4 Gym", $"co4-{Guid.NewGuid():N}"[..16]);
            db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
            await db.SaveChangesAsync();
            gymId = gym.Id;
        }

        var vip = await AddMemberAsync(gymId, "Vip", ["vip"]);
        var one = await AddMemberAsync(gymId, "Uno");
        var two = await AddMemberAsync(gymId, "Dos");

        var userOf = new Dictionary<Guid, Guid>();
        using (var scope = SystemScope(T0))
        {
            var members = await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Member>()
                .IgnoreQueryFilters().AsNoTracking().Where(m => m.GymId == gymId).ToListAsync();
            foreach (var m in members)
            {
                userOf[m.Id] = m.UserId!.Value;
            }
        }

        return new World(gymId, vip, [vip, one, two], userOf);
    }

    private sealed record World(Guid GymId, Guid Vip, IReadOnlyList<Guid> Active, IReadOnlyDictionary<Guid, Guid> UserOf);

    private IServiceScope SystemScope(DateTimeOffset at)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        Pin(scope, at);
        return scope;
    }

    private IServiceScope Staff(Guid gymId, DateTimeOffset at)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Staff);
        Pin(scope, at);
        return scope;
    }

    private IServiceScope MemberScope(Guid gymId, Guid userId, DateTimeOffset at)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId, UserRole.Member);
        Pin(scope, at);
        return scope;
    }

    private static void Pin(IServiceScope scope, DateTimeOffset at)
        => scope.ServiceProvider.GetRequiredService<MutableClock>()
            .PinTo(at, DateOnly.FromDateTime(BogotaClock.ToBusinessLocal(at)));

    private sealed class FakeStorage : IFileStorage
    {
        public List<string> Saved { get; } = [];

        public List<string> Deleted { get; } = [];

        public Task<string> SaveAsync(FileToStore file, string folder, CancellationToken cancellationToken = default)
        {
            var url = $"https://files.test/{folder}/{Guid.NewGuid():N}.png";
            Saved.Add(url);
            return Task.FromResult(url);
        }

        public Task DeleteAsync(string publicUrl, CancellationToken cancellationToken = default)
        {
            Deleted.Add(publicUrl);
            return Task.CompletedTask;
        }
    }
}
