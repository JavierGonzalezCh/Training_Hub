using GSM.Application.Attendance.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Common.Logs;
using GSM.Application.Communications.Events;
using GSM.Application.Identity.Public;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Common;
using GSM.Infrastructure.Logs;
using GSM.Infrastructure.Notifications;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>N1: schema notifications, dispatcher con candado, bandeja general del staff.</summary>
public sealed class NotificationsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    // ---------- 1 · la migración conserva los datos ----------

    [Fact]
    public async Task The_migration_moves_the_inbox_the_email_queue_and_unseen_attempts_without_losing_data()
    {
        // Una base desechable: se migra hasta la migración anterior a N1, se insertan datos con el schema
        // viejo (communications.member_notifications / email_outbox, attendance.checkin_attempts), se aplica
        // N1 y se verifica lo copiado.
        var scratch = $"{ApiFactory.TestDatabaseName}_mig";
        var admin = "Host=localhost;Port=5432;Database=postgres;Username=gsm;Password=gsm";
        var target = $"Host=localhost;Port=5432;Database={scratch};Username=gsm;Password=gsm";
        await using (var conn = new NpgsqlConnection(admin))
        {
            await conn.OpenAsync();
            await ExecAsync(conn, $"DROP DATABASE IF EXISTS \"{scratch}\" WITH (FORCE)");
            await ExecAsync(conn, $"CREATE DATABASE \"{scratch}\"");
        }

        try
        {
            var options = new DbContextOptionsBuilder<GsmDbContext>()
                .UseNpgsql(target, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(GsmDbContext).Assembly.FullName);
                    PgEnumMappings.ConfigureProvider(npgsql);
                })
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new GsmDbContext(options, NullCurrentUser.Instance);
            var migrator = db.GetService<IMigrator>();
            var all = db.Database.GetMigrations().ToList();
            var n1 = all.FindIndex(m => m.EndsWith("V2_N1_Notifications", StringComparison.Ordinal));
            Assert.True(n1 > 0);
            await migrator.MigrateAsync(all[n1 - 1]);

            var gym = Guid.CreateVersion7();
            var m1 = Guid.CreateVersion7();
            var m2 = Guid.CreateVersion7();
            var classRef = Guid.CreateVersion7();
            var announcementRef = Guid.CreateVersion7();
            var pendingAttempt = Guid.CreateVersion7();
            await using (var conn = new NpgsqlConnection(target))
            {
                await conn.OpenAsync();
                await ExecAsync(conn, $"""
                    INSERT INTO communications.member_notifications (id, gym_id, member_id, type, reference_id, title, body, link, read_at, created_at, updated_at) VALUES
                     (gen_random_uuid(), '{gym}', '{m1}', 'class_cancelled', '{classRef}', 'Spinning se canceló', 'x', '/app/clases', NULL, now(), now()),
                     (gen_random_uuid(), '{gym}', '{m1}', 'achievement_unlocked', '{Guid.CreateVersion7()}', 'Logro', 'x', '/app/logros', now(), now(), now());
                    INSERT INTO communications.email_outbox (id, gym_id, member_id, to_address, subject, html_body, text_body, kind, reference_id, status, attempts, last_error, sent_at, created_at, updated_at) VALUES
                     (gen_random_uuid(), '{gym}', '{m1}', 'a@t.co', 'Gym: Spinning se canceló', '<p>h</p>', 'h', 'class_cancelled', '{classRef}', 'pending', 0, NULL, NULL, now(), now()),
                     (gen_random_uuid(), '{gym}', '{m1}', 'a@t.co', 'Gym: Anuncio', '<p>h</p>', 'h', 'announcement', '{announcementRef}', 'sent', 1, NULL, now(), now(), now()),
                     (gen_random_uuid(), '{gym}', '{m2}', 'b@t.co', 'Gym: Anuncio', '<p>h</p>', 'h', 'announcement', '{announcementRef}', 'failed', 3, 'boom', NULL, now(), now());
                    INSERT INTO attendance.checkin_attempts (id, gym_id, member_id, reason, attempted_at, attempt_date, attempt_count, membership_ended_on, acknowledged_at, acknowledged_by, created_at, updated_at) VALUES
                     ('{pendingAttempt}', '{gym}', '{m1}', 'membership_expired', now(), current_date, 2, current_date - 10, NULL, NULL, now(), now()),
                     (gen_random_uuid(), '{gym}', '{m1}', 'no_membership', now(), current_date - 1, 1, NULL, now(), NULL, now(), now()),
                     (gen_random_uuid(), '{gym}', '{m1}', 'no_membership', now() - interval '200 days', current_date - 200, 1, NULL, NULL, NULL, now(), now());
                    """);
            }

            await migrator.MigrateAsync();

            await using var check = new NpgsqlConnection(target);
            await check.OpenAsync();

            // La bandeja del miembro: 2 avisos propios + 2 de anuncio (los correos de anuncio ganan su aviso).
            Assert.Equal(2, await ScalarAsync(check,
                $"SELECT count(*) FROM notifications.notifications WHERE recipient_member_id = '{m1}' AND type <> 'announcement'"));
            Assert.Equal(1, await ScalarAsync(check,
                $"SELECT count(*) FROM notifications.notifications WHERE recipient_member_id = '{m1}' AND type = 'announcement' AND read_at IS NOT NULL"));

            // La cola de correos: las 3 filas con su estado; el correo pendiente deja su aviso pendiente para el dispatcher.
            Assert.Equal(3, await ScalarAsync(check, "SELECT count(*) FROM notifications.notification_deliveries"));
            Assert.Equal(1, await ScalarAsync(check, "SELECT count(*) FROM notifications.notification_deliveries WHERE status = 'pending'"));
            Assert.Equal(1, await ScalarAsync(check, "SELECT count(*) FROM notifications.notification_deliveries WHERE status = 'failed' AND attempts = 3"));
            Assert.Equal(1, await ScalarAsync(check,
                $"SELECT count(*) FROM notifications.notifications WHERE reference_id = '{classRef}' AND status = 'pending'"));

            // Los intentos sin ver (últimos 90 días) pasan a ser avisos de acción; los vistos y los viejos no.
            Assert.Equal(1, await ScalarAsync(check,
                "SELECT count(*) FROM notifications.notifications WHERE type = 'checkin_rejected' AND requires_action"));
            Assert.Equal(1, await ScalarAsync(check,
                $"SELECT count(*) FROM notifications.notifications WHERE type = 'checkin_rejected' AND reference_id = '{pendingAttempt}' AND resolved_at IS NULL"));

            // Las tablas y los tipos viejos desaparecen.
            Assert.Equal(0, await ScalarAsync(check,
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'communications' AND table_name IN ('member_notifications', 'email_outbox')"));
        }
        finally
        {
            await using var conn = new NpgsqlConnection(admin);
            await conn.OpenAsync();
            await ExecAsync(conn, $"DROP DATABASE IF EXISTS \"{scratch}\" WITH (FORCE)");
        }
    }

    // ---------- 2 · avisos por evento, idempotentes ----------

    [Fact]
    public async Task Publishing_an_announcement_event_creates_one_notice_per_member_and_never_duplicates()
    {
        var world = await SeedAsync(3);
        var announcementId = Guid.CreateVersion7();
        var evt = new AnnouncementPublished(
            announcementId, world.GymId, "Cerramos el lunes", "Festivo nacional.", true,
            world.Members.Select(m => m.Id).ToList());

        await PublishAsync(world.GymId, evt);
        await PublishAsync(world.GymId, evt);

        Assert.Equal(3, await CountAsync(world.GymId, NotificationType.Announcement));
        Assert.Equal(3, await CountDeliveriesAsync(world.GymId));

        // Un anuncio sin correo crea avisos pero ninguna entrega.
        await PublishAsync(world.GymId, evt with { AnnouncementId = Guid.CreateVersion7(), SendEmail = false });
        Assert.Equal(6, await CountAsync(world.GymId, NotificationType.Announcement));
        Assert.Equal(3, await CountDeliveriesAsync(world.GymId));

        // Los anuncios viven en el feed: no aparecen en la bandeja del miembro.
        using var scope = Scope(world.GymId, world.Members[0].UserId, UserRole.Member);
        var inbox = await scope.ServiceProvider.GetRequiredService<ListMyNotificationsHandler>().HandleAsync(1, 20, default);
        Assert.Empty(inbox.Items);
    }

    [Fact]
    public async Task A_membership_reminder_event_creates_the_notice_and_its_email_once()
    {
        var world = await SeedAsync(1);
        var evt = new MembershipReminderDue(
            world.GymId, world.Members[0].Id, Guid.CreateVersion7(), "Mensual", Today.AddDays(2), 2, MembershipReminderKind.DaysBefore);

        await PublishAsync(world.GymId, evt);
        await PublishAsync(world.GymId, evt);

        Assert.Equal(1, await CountAsync(world.GymId, NotificationType.MembershipExpiring));
        Assert.Equal(1, await CountDeliveriesAsync(world.GymId));
    }

    // ---------- 3 · el dispatcher: candado, envío único, latido ----------

    [Fact]
    public async Task Only_the_round_holding_the_lock_sends_and_a_held_lock_skips_the_round()
    {
        var world = await SeedAsync(2);
        var sender = factory.Services.GetRequiredService<CapturingEmailSender>();
        await DrainAsync();
        sender.Clear();

        await PublishAsync(world.GymId, new AnnouncementPublished(
            Guid.CreateVersion7(), world.GymId, "Otro aviso", "Texto.", true, world.Members.Select(m => m.Id).ToList()));

        // Otro proceso tiene el candado: esta vuelta no hace nada.
        var external = new PostgresNotificationDispatchLock(factory.Services.GetRequiredService<IConfiguration>());
        var held = await external.TryAcquireAsync();
        Assert.NotNull(held);
        DispatchRoundResult skipped;
        try
        {
            skipped = await RunRoundAsync();
        }
        finally
        {
            await held!.DisposeAsync();
        }

        Assert.False(skipped.LockAcquired);
        Assert.Empty(MyMails(world, sender));

        // Liberado, la vuelta siguiente envía.
        var done = await RunRoundAsync();
        Assert.True(done.LockAcquired);
        Assert.Equal(2, MyMails(world, sender).Count);
    }

    [Fact]
    public async Task Two_concurrent_rounds_never_send_the_same_email_twice()
    {
        var world = await SeedAsync(3);
        var sender = factory.Services.GetRequiredService<CapturingEmailSender>();
        await DrainAsync();
        sender.Clear();
        await PublishAsync(world.GymId, new AnnouncementPublished(
            Guid.CreateVersion7(), world.GymId, "Aviso", "Texto.", true, world.Members.Select(m => m.Id).ToList()));

        var slow = new SlowSender(sender, TimeSpan.FromMilliseconds(400));
        var results = await Task.WhenAll(
            RunRoundAsync(slow),
            RunRoundAsync(slow));

        var mails = MyMails(world, sender);
        Assert.Equal(3, mails.Count);
        Assert.Equal(3, mails.Select(m => m.To).Distinct().Count());
        Assert.Contains(results, r => !r.LockAcquired);
    }

    [Fact]
    public async Task The_dispatcher_beats_every_round_and_records_a_job_run_only_when_it_processed_something()
    {
        var world = await SeedAsync(1);
        await DrainAsync();
        var logs = factory.Services.GetRequiredService<IDbContextFactory<LogsDbContext>>();
        await factory.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using (var before = logs.CreateDbContext())
        {
            // Punto de partida limpio de esta prueba.
            await before.JobRuns.Where(r => r.Job == NotificationDispatchRunner.Name).ExecuteDeleteAsync();
        }

        // Vuelta sin nada que hacer: latido sí, fila de tarea no.
        var idleAt = NowOf(Today).AddMinutes(1);
        await RunRoundAsync(at: idleAt);
        await factory.Services.GetRequiredService<ILogStore>().FlushAsync();
        await using (var db = logs.CreateDbContext())
        {
            Assert.Equal(0, await db.JobRuns.CountAsync(r => r.Job == NotificationDispatchRunner.Name));
            var beat = await db.Heartbeats.SingleAsync(h => h.Process == NotificationDispatchRunner.Name);
            Assert.Equal(idleAt, beat.LastBeatAt);
        }

        // Vuelta con un correo por enviar: fila de tarea con 1 procesado y latido nuevo.
        await PublishAsync(world.GymId, new AnnouncementPublished(
            Guid.CreateVersion7(), world.GymId, "Aviso", "Texto.", true, [world.Members[0].Id]));
        var busyAt = NowOf(Today).AddMinutes(2);
        var result = await RunRoundAsync(at: busyAt);
        await factory.Services.GetRequiredService<ILogStore>().FlushAsync();
        Assert.True(result.Processed >= 1);
        await using (var db = logs.CreateDbContext())
        {
            var run = await db.JobRuns.SingleAsync(r => r.Job == NotificationDispatchRunner.Name);
            Assert.Equal(result.Processed, run.ItemsProcessed);
            Assert.Equal("ok", run.Status);
            var beat = await db.Heartbeats.SingleAsync(h => h.Process == NotificationDispatchRunner.Name);
            Assert.Equal(busyAt, beat.LastBeatAt);
        }
    }

    // ---------- 4 · bandeja general del staff ----------

    [Fact]
    public async Task A_rejected_check_in_creates_one_action_notice_for_the_gym_that_only_its_staff_sees()
    {
        var world = await SeedAsync(1);
        var other = await SeedAsync(1);
        var attemptId = Guid.CreateVersion7();
        var rejected = new CheckInRejected(
            attemptId, world.GymId, world.Members[0].Id, CheckinRejectionReason.MembershipExpired, 1, Today.AddDays(-10));

        await PublishAsync(world.GymId, rejected);
        // El miembro insiste mientras sigue sin verse: el mismo aviso actualiza su contador.
        await PublishAsync(world.GymId, rejected with { AttemptCount = 2 });

        Assert.Equal(1, await CountAsync(world.GymId, NotificationType.CheckinRejected));

        using (var staff = Scope(world.GymId, Guid.CreateVersion7(), UserRole.Staff))
        {
            Assert.Equal(1, (await staff.ServiceProvider.GetRequiredService<GetStaffNotificationCountHandler>().HandleAsync(default)).Count);
            var item = (await staff.ServiceProvider.GetRequiredService<ListStaffNotificationsHandler>()
                .HandleAsync(true, 1, 20, default)).Items.Single();
            Assert.True(item.RequiresAction);
            Assert.Contains("2 intentos", item.Body);
            Assert.Equal($"/members/{world.Members[0].Id}", item.Link);
        }

        // El staff de otro gym no ve nada.
        using (var foreign = Scope(other.GymId, Guid.CreateVersion7(), UserRole.Staff))
        {
            Assert.Equal(0, (await foreign.ServiceProvider.GetRequiredService<GetStaffNotificationCountHandler>().HandleAsync(default)).Count);
            Assert.Empty((await foreign.ServiceProvider.GetRequiredService<ListStaffNotificationsHandler>()
                .HandleAsync(false, 1, 20, default)).Items);
        }
    }

    [Fact]
    public async Task Resolving_an_action_notice_resolves_it_for_everyone_with_who_and_when()
    {
        var anaId = Guid.CreateVersion7();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IIdentityReader>(_ => new FixedIdentityReader(anaId, "Ana Recepción"));
        }));
        var world = await SeedAsync(1);
        var attemptId = Guid.CreateVersion7();
        await PublishAsync(world.GymId, new CheckInRejected(
            attemptId, world.GymId, world.Members[0].Id, CheckinRejectionReason.NoMembership, 1, null));

        var resolveAt = NowOf(Today).AddHours(1);
        Guid noticeId;
        using (var ana = ScopeOn(host.Services, world.GymId, anaId, UserRole.Staff, resolveAt))
        {
            noticeId = (await ana.ServiceProvider.GetRequiredService<ListStaffNotificationsHandler>()
                .HandleAsync(true, 1, 20, default)).Items.Single().Id;
            var resolved = await ana.ServiceProvider.GetRequiredService<ResolveStaffNotificationHandler>()
                .HandleAsync(noticeId, default);
            Assert.Equal("Ana Recepción", resolved.ResolvedByName);
            Assert.Equal(anaId, resolved.ResolvedBy);
            Assert.Equal(resolveAt, resolved.ResolvedAt);
        }

        // Otra persona del staff lo ve atendido (por Ana) y ya no cuenta como abierto; atenderlo otra vez no cambia nada.
        var bruno = Guid.CreateVersion7();
        using (var other = ScopeOn(host.Services, world.GymId, bruno, UserRole.Owner, resolveAt.AddHours(1)))
        {
            Assert.Equal(0, (await other.ServiceProvider.GetRequiredService<GetStaffNotificationCountHandler>().HandleAsync(default)).Count);
            Assert.Empty((await other.ServiceProvider.GetRequiredService<ListStaffNotificationsHandler>()
                .HandleAsync(true, 1, 20, default)).Items);

            var again = await other.ServiceProvider.GetRequiredService<ResolveStaffNotificationHandler>()
                .HandleAsync(noticeId, default);
            Assert.Equal(anaId, again.ResolvedBy);
            Assert.Equal(resolveAt, again.ResolvedAt);
        }
    }

    [Fact]
    public async Task Informational_notices_are_per_person_and_a_notice_of_someone_else_is_not_found()
    {
        var world = await SeedAsync(1);
        var mine = Guid.CreateVersion7();
        var theirs = Guid.CreateVersion7();
        Guid mineId, theirsId;
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var a = Notification.CreateForUser(world.GymId, mine, NotificationType.ClassCancelled, Guid.CreateVersion7(), "Para mí", "x", null, NowOf(Today));
            var b = Notification.CreateForUser(world.GymId, theirs, NotificationType.ClassCancelled, Guid.CreateVersion7(), "Para otro", "x", null, NowOf(Today));
            db.AddRange(a, b);
            await db.SaveChangesAsync();
            mineId = a.Id;
            theirsId = b.Id;
        }

        using var staff = Scope(world.GymId, mine, UserRole.Staff);
        Assert.Equal(1, (await staff.ServiceProvider.GetRequiredService<GetStaffNotificationCountHandler>().HandleAsync(default)).Count);

        await Assert.ThrowsAsync<NotFoundException>(() => staff.ServiceProvider
            .GetRequiredService<MarkStaffNotificationReadHandler>().HandleAsync(theirsId, default));
        await Assert.ThrowsAsync<ConflictException>(() => staff.ServiceProvider
            .GetRequiredService<ResolveStaffNotificationHandler>().HandleAsync(mineId, default));

        await staff.ServiceProvider.GetRequiredService<MarkStaffNotificationReadHandler>().HandleAsync(mineId, default);
        Assert.Equal(0, (await staff.ServiceProvider.GetRequiredService<GetStaffNotificationCountHandler>().HandleAsync(default)).Count);
    }

    // ---------- helpers ----------

    private static DateTimeOffset NowOf(DateOnly day)
        => new SystemClock(Microsoft.Extensions.Options.Options.Create(new ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(12);

    private static async Task ExecAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static IReadOnlyList<EmailMessage> MyMails(World world, CapturingEmailSender sender)
        => sender.Sent.Where(m => world.Members.Any(x => x.Email == m.To)).ToList();

    private async Task<DispatchRoundResult> RunRoundAsync(IEmailSender? emailSender = null, DateTimeOffset? at = null)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(at ?? NowOf(Today), Today);
        var runner = emailSender is null
            ? scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>()
            : ActivatorUtilities.CreateInstance<NotificationDispatchRunner>(scope.ServiceProvider, emailSender);
        return await runner.RunOnceAsync();
    }

    /// <summary>Vacía lo que otras pruebas dejaron pendiente, para medir solo lo de esta.</summary>
    private async Task DrainAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            if ((await RunRoundAsync()).Processed == 0)
            {
                break;
            }
        }
    }

    private sealed class SlowSender(CapturingEmailSender inner, TimeSpan delay) : IEmailSender
    {
        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            await inner.SendAsync(message, cancellationToken);
        }
    }

    private sealed class FixedIdentityReader(Guid id, string name) : IIdentityReader
    {
        public Task<IdentityUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IdentityUserDto?>(userId == id ? new IdentityUserDto(id, name, false) : null);

        public Task<IReadOnlyList<TrainerCandidateDto>> ListTrainersAsync(Guid gymId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TrainerCandidateDto>>([]);

        public Task<bool> CanBeTrainerAsync(Guid userId, Guid gymId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlySet<Guid>> ListUserIdsWithAnyPermissionAsync(
            Guid gymId, IReadOnlyCollection<GSM.Domain.Identity.StaffPermission> permissions,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task<IReadOnlyList<GymOwnerDto>> ListActiveOwnersAsync(Guid gymId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GymOwnerDto>>([]);

        public Task<GymTeamSetup> GetTeamSetupAsync(Guid gymId, CancellationToken cancellationToken = default)
            => Task.FromResult(new GymTeamSetup(0, 0));
    }

    private async Task PublishAsync<TEvent>(Guid gymId, TEvent evt) where TEvent : IDomainEvent
    {
        using var scope = Scope(gymId, Guid.CreateVersion7(), UserRole.Staff);
        await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().PublishAsync(evt, default);
    }

    private async Task<int> CountAsync(Guid gymId, NotificationType type)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .CountAsync(n => n.GymId == gymId && n.Type == type);
    }

    private async Task<int> CountDeliveriesAsync(Guid gymId)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<NotificationDelivery>()
            .CountAsync(d => d.GymId == gymId);
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Scope(Guid gymId, Guid userId, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(NowOf(Today), Today);
        return scope;
    }

    private static IServiceScope ScopeOn(IServiceProvider services, Guid gymId, Guid userId, UserRole role, DateTimeOffset at)
    {
        var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(at, Today);
        return scope;
    }

    private async Task<World> SeedAsync(int members)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("Avisos Gym", $"nt-{Guid.NewGuid():N}"[..16]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));

        var people = new List<Person>();
        for (var i = 0; i < members; i++)
        {
            var userId = Guid.CreateVersion7();
            var address = $"n-{Guid.NewGuid():N}@test.local";
            var member = GSM.Domain.Members.Member.Create(
                gym.Id, "Miembro", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-60), email: address);
            member.LinkUser(userId);
            db.Add(member);
            people.Add(new Person(member.Id, userId, address));
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, people);
    }

    private sealed record Person(Guid Id, Guid UserId, string? Email);

    private sealed record World(Guid GymId, IReadOnlyList<Person> Members);
}
