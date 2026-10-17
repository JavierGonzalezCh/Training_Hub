using GSM.Application.Common.Events;
using GSM.Application.Identity.Public;
using GSM.Application.Notifications;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Application.SuperAdmin.Events;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Notifications;
using GSM.Domain.Payments;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// S3: avisos de la suscripción a los Owners (SuperAdmin §V3). La tarea periódica publica un evento por hito de
/// calendario y Notifications crea un aviso por Owner, idempotente. Suspender, reactivar y registrar un pago publican
/// el suyo. El tiempo va por <c>MutableClock</c>; cada prueba usa su propio gym (la base de pruebas se comparte).
/// </summary>
public sealed class SubscriptionNoticeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private static readonly DateOnly Today = new(2026, 11, 10);

    // ---------- prueba ----------

    [Fact]
    public async Task Trial_ending_soon_notifies_every_owner_once_with_email_and_not_before_the_window()
    {
        var w = await SeedAsync(
            GymStatus.Trial, startedOn: Today.AddDays(-12), trialEndsOn: Today.AddDays(4), paidThrough: Today.AddDays(4));

        // A 4 días del fin todavía no toca.
        await RunNoticesAsync(Today);
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.SubscriptionTrialEnding));

        // A 3 días, sí: un aviso por Owner, con su correo en cola. Repetir la tarea no duplica.
        await RunNoticesAsync(Today.AddDays(1));
        await RunNoticesAsync(Today.AddDays(1));
        await RunNoticesAsync(Today.AddDays(2));

        var notices = await NoticesAsync(w.GymId, NotificationType.SubscriptionTrialEnding);
        Assert.Equal(2, notices.Count);
        Assert.Equal(w.OwnerIds.Order(), notices.Select(n => n.RecipientUserId!.Value).Order());
        Assert.All(notices, n =>
        {
            Assert.False(n.RequiresAction);
            Assert.Equal(NotificationPriority.Normal, n.Priority);
            Assert.Equal("Tu prueba termina pronto", n.Title);
        });

        var emails = await EmailsAsync(notices.Select(n => n.Id));
        Assert.Equal(2, emails.Count);
        Assert.Equal(w.OwnerEmails.Order(), emails.Select(e => e.ToAddress.ToLowerInvariant()).Order());
    }

    [Fact]
    public async Task Calendar_notices_are_scheduled_for_8am_when_the_job_runs_earlier()
    {
        var w = await SeedAsync(
            GymStatus.Active, startedOn: Today.AddDays(-30), trialEndsOn: null, paidThrough: Today.AddDays(2));

        // 6:00 hora del gym: el aviso espera a las 8:00.
        await RunNoticesAsync(Today, hour: 6);
        var notice = (await NoticesAsync(w.GymId, NotificationType.SubscriptionExpiring)).First();
        Assert.Equal(AtLocal(Today, 8), notice.ScheduledFor);
        Assert.Equal(NotificationStatus.Pending, notice.Status);

        // Después de las 8:00 de otro gym-día, un aviso nuevo sale ya.
        var later = await SeedAsync(
            GymStatus.Active, startedOn: Today.AddDays(-30), trialEndsOn: null, paidThrough: Today.AddDays(2));
        await RunNoticesAsync(Today, hour: 12);
        var now = (await NoticesAsync(later.GymId, NotificationType.SubscriptionExpiring)).First();
        Assert.Equal(AtLocal(Today, 12), now.ScheduledFor);
    }

    [Fact]
    public async Task Trial_ended_gives_the_grace_deadline_and_no_disable_soon_with_the_default_grace()
    {
        var trialEnd = Today.AddDays(-1);
        var w = await SeedAsync(GymStatus.Trial, startedOn: Today.AddDays(-31), trialEndsOn: trialEnd, paidThrough: trialEnd);

        await RunNoticesAsync(Today); // primer día de gracia

        var ended = await NoticesAsync(w.GymId, NotificationType.SubscriptionTrialEnded);
        Assert.Equal(2, ended.Count);
        Assert.Contains("12 de noviembre", ended[0].Body); // fin de la prueba (9-nov) + 3 días de gracia
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.SubscriptionDisableSoon));

        // Con la gracia por defecto, el último día sigue siendo el mismo aviso y no se acumula uno nuevo.
        await RunNoticesAsync(Today.AddDays(2)); // último día de gracia (12-nov)
        Assert.Equal(2, (await NoticesAsync(w.GymId, NotificationType.SubscriptionTrialEnded)).Count);
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.SubscriptionDisableSoon));
    }

    [Fact]
    public async Task A_longer_trial_grace_adds_the_disable_soon_notice_three_days_before_the_end()
    {
        var trialEnd = Today.AddDays(-1);
        var w = await SeedAsync(
            GymStatus.Trial, startedOn: Today.AddDays(-31), trialEndsOn: trialEnd, paidThrough: trialEnd, trialGraceDays: 10);

        await RunNoticesAsync(Today.AddDays(4));  // 14-nov: quedan 5 días hasta el 19-nov
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.SubscriptionDisableSoon));

        await RunNoticesAsync(Today.AddDays(6));  // 16-nov: quedan 3
        Assert.Equal(2, (await NoticesAsync(w.GymId, NotificationType.SubscriptionDisableSoon)).Count);
    }

    // ---------- vencimiento, mora e inhabilitación ----------

    [Fact]
    public async Task A_paying_gym_walks_expiring_expires_today_overdue_disable_soon_and_disabled()
    {
        var paidThrough = Today;
        var w = await SeedAsync(GymStatus.Active, startedOn: Today.AddDays(-90), trialEndsOn: null, paidThrough: paidThrough);

        async Task<int> CountAsync(NotificationType type) => (await NoticesAsync(w.GymId, type)).Count;

        await RunNoticesAsync(paidThrough.AddDays(-6));
        Assert.Equal(0, await CountAsync(NotificationType.SubscriptionExpiring));

        await RunNoticesAsync(paidThrough.AddDays(-5));
        Assert.Equal(2, await CountAsync(NotificationType.SubscriptionExpiring));

        await RunNoticesAsync(paidThrough);
        Assert.Equal(2, await CountAsync(NotificationType.SubscriptionExpiresToday));

        await RunNoticesAsync(paidThrough.AddDays(1));
        Assert.Equal(2, await CountAsync(NotificationType.SubscriptionOverdue));

        // S3-3: los avisos llevan a la pantalla de suscripción del Owner, no a la raíz.
        Assert.All(await NoticesAsync(w.GymId, NotificationType.SubscriptionOverdue), n => Assert.Equal("/subscription", n.Link));
        Assert.Equal(0, await CountAsync(NotificationType.SubscriptionDisableSoon));

        await RunNoticesAsync(paidThrough.AddDays(12)); // 15 de gracia: quedan 3
        Assert.Equal(2, await CountAsync(NotificationType.SubscriptionDisableSoon));
        Assert.Equal(0, await CountAsync(NotificationType.SubscriptionDisabled));

        await RunNoticesAsync(paidThrough.AddDays(16));
        var disabled = await NoticesAsync(w.GymId, NotificationType.SubscriptionDisabled);
        Assert.Equal(2, disabled.Count);
        Assert.All(disabled, n => Assert.Equal(NotificationPriority.Urgent, n.Priority));

        // Seguir inhabilitado no vuelve a avisar.
        await RunNoticesAsync(paidThrough.AddDays(40));
        Assert.Equal(2, await CountAsync(NotificationType.SubscriptionDisabled));
    }

    [Fact]
    public async Task A_missed_run_is_recovered_by_the_next_one()
    {
        var w = await SeedAsync(GymStatus.Active, startedOn: Today.AddDays(-90), trialEndsOn: null, paidThrough: Today);

        // La tarea no corrió el día del vencimiento: al día siguiente el gym ya está en mora y se avisa de la mora.
        await RunNoticesAsync(Today.AddDays(2));

        Assert.Equal(2, (await NoticesAsync(w.GymId, NotificationType.SubscriptionOverdue)).Count);
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.SubscriptionExpiresToday));
    }

    [Fact]
    public async Task A_new_period_after_a_payment_warns_again()
    {
        var w = await SeedAsync(GymStatus.Active, startedOn: Today.AddDays(-90), trialEndsOn: null, paidThrough: Today);
        await RunNoticesAsync(Today.AddDays(-3));
        Assert.Equal(2, (await NoticesAsync(w.GymId, NotificationType.SubscriptionExpiring)).Count);

        // El pago empuja la cobertura 30 días; en el siguiente vencimiento hay aviso nuevo (otra clave).
        using (var scope = AsSuperAdmin(Today.AddDays(-2)))
        {
            await scope.ServiceProvider.GetRequiredService<RegisterSubscriptionPaymentHandler>().HandleAsync(
                w.GymId, new RegisterSubscriptionPaymentRequest(100_000m, PaymentMethod.Cash, null, null, null, null), default);
        }

        await RunNoticesAsync(Today.AddDays(27));
        Assert.Equal(4, (await NoticesAsync(w.GymId, NotificationType.SubscriptionExpiring)).Count);
    }

    // ---------- hechos: pago, suspensión, reactivación ----------

    [Fact]
    public async Task Registering_a_payment_tells_the_owners_how_far_the_subscription_goes()
    {
        var w = await SeedAsync(GymStatus.Active, startedOn: Today.AddDays(-90), trialEndsOn: null, paidThrough: Today);

        using (var scope = AsSuperAdmin(Today))
        {
            await scope.ServiceProvider.GetRequiredService<RegisterSubscriptionPaymentHandler>().HandleAsync(
                w.GymId, new RegisterSubscriptionPaymentRequest(150_000m, PaymentMethod.BankTransfer, null, null, null, null), default);
        }

        var notices = await NoticesAsync(w.GymId, NotificationType.SubscriptionPaymentRegistered);
        Assert.Equal(2, notices.Count);
        Assert.All(notices, n =>
        {
            Assert.Equal("Pago registrado", n.Title);
            Assert.Contains("150", n.Body);
            Assert.Contains("10 de diciembre", n.Body); // día siguiente a paid_through + 1 mes - 1
            Assert.Equal(AtLocal(Today, 12), n.ScheduledFor); // un hecho sale ya
        });
        Assert.Equal(2, (await EmailsAsync(notices.Select(n => n.Id))).Count);
    }

    [Fact]
    public async Task Suspending_and_reactivating_notify_the_owners_each_time_and_the_suspension_is_urgent()
    {
        var w = await SeedAsync(GymStatus.Active, startedOn: Today.AddDays(-90), trialEndsOn: null, paidThrough: Today.AddDays(20));

        async Task SuspendAsync()
        {
            using var scope = AsSuperAdmin(Today);
            await scope.ServiceProvider.GetRequiredService<SuspendGymHandler>()
                .HandleAsync(w.GymId, new SuspendGymRequest("Falta de pago acordada"), default);
        }

        async Task ReactivateAsync()
        {
            using var scope = AsSuperAdmin(Today);
            await scope.ServiceProvider.GetRequiredService<ReactivateGymHandler>().HandleAsync(w.GymId, default);
        }

        await SuspendAsync();
        var suspended = await NoticesAsync(w.GymId, NotificationType.SubscriptionSuspended);
        Assert.Equal(2, suspended.Count);
        Assert.All(suspended, n => Assert.Equal(NotificationPriority.Urgent, n.Priority));
        Assert.DoesNotContain("Falta de pago acordada", suspended[0].Body); // el motivo es interno

        await ReactivateAsync();
        Assert.Equal(2, (await NoticesAsync(w.GymId, NotificationType.SubscriptionReactivated)).Count);

        // Suspender otra vez es otro hecho, otro aviso.
        await SuspendAsync();
        Assert.Equal(4, (await NoticesAsync(w.GymId, NotificationType.SubscriptionSuspended)).Count);

        // La tarea no avisa de calendario a un gym suspendido.
        await RunNoticesAsync(Today.AddDays(18));
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.SubscriptionExpiring));
    }

    [Fact]
    public async Task Publishing_the_same_event_twice_does_not_duplicate_and_a_gym_without_owners_is_fine()
    {
        var w = await SeedAsync(GymStatus.Active, startedOn: Today.AddDays(-90), trialEndsOn: null, paidThrough: Today.AddDays(20));
        var reference = Guid.CreateVersion7();
        var due = new SubscriptionNoticeDue(w.GymId, SubscriptionNoticeKind.Reactivated, reference, null, null, null, null, null);

        using (var scope = System(Today))
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
            await publisher.PublishAsync(due);
            await publisher.PublishAsync(due);
            await publisher.PublishAsync(due with { GymId = Guid.CreateVersion7(), ReferenceId = Guid.CreateVersion7() });
        }

        Assert.Equal(2, (await NoticesAsync(w.GymId, NotificationType.SubscriptionReactivated)).Count);
    }

    // ---------- límite de miembros ----------

    [Fact]
    public async Task The_member_limit_warns_at_90_percent_and_again_on_reaching_it_once_each()
    {
        var w = await SeedAsync(GymStatus.Active, startedOn: Today.AddDays(-90), trialEndsOn: null, paidThrough: Today.AddDays(60), memberLimit: 10);

        await AddCoveredMembersAsync(w.GymId, 8);
        await RunNoticesAsync(Today);
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.MemberLimitNear));

        await AddCoveredMembersAsync(w.GymId, 1); // 9 de 10
        await RunNoticesAsync(Today);
        await RunNoticesAsync(Today);
        var near = await NoticesAsync(w.GymId, NotificationType.MemberLimitNear);
        Assert.Equal(2, near.Count);
        Assert.Equal("Te queda 1 cupo", near[0].Title);
        Assert.Contains("9 de 10", near[0].Body);
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.MemberLimitReached));

        await AddCoveredMembersAsync(w.GymId, 1); // 10 de 10
        await RunNoticesAsync(Today);
        var reached = await NoticesAsync(w.GymId, NotificationType.MemberLimitReached);
        Assert.Equal(2, reached.Count);
        Assert.Equal("Llegaste al límite de miembros", reached[0].Title);
    }

    [Fact]
    public async Task A_gym_without_a_member_limit_gets_no_limit_notice()
    {
        // Sin plan ni override no hay límite.
        var w = await SeedAsync(
            GymStatus.Active, startedOn: Today.AddDays(-90), trialEndsOn: null, paidThrough: Today.AddDays(60));
        await AddCoveredMembersAsync(w.GymId, 12);

        await RunNoticesAsync(Today);

        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.MemberLimitNear));
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.MemberLimitReached));
    }

    // ---------- bandeja y entrega ----------

    [Fact]
    public async Task The_dispatcher_sends_the_scheduled_notice_and_its_email_at_8am()
    {
        var w = await SeedAsync(GymStatus.Active, startedOn: Today.AddDays(-90), trialEndsOn: null, paidThrough: Today);
        await RunNoticesAsync(Today, hour: 6); // vence hoy; sale a las 8:00

        await RunDispatcherAsync(Today, hour: 7);
        Assert.All(
            await NoticesAsync(w.GymId, NotificationType.SubscriptionExpiresToday),
            n => Assert.Equal(NotificationStatus.Pending, n.Status));

        await RunDispatcherAsync(Today, hour: 8);
        var sent = await NoticesAsync(w.GymId, NotificationType.SubscriptionExpiresToday);
        Assert.All(sent, n => Assert.Equal(NotificationStatus.Sent, n.Status));
        Assert.All(await EmailsAsync(sent.Select(n => n.Id)), e => Assert.Equal(DeliveryStatus.Sent, e.Status));
    }

    // ---------- helpers ----------

    private static DateTimeOffset AtLocal(DateOnly day, int hour)
        => new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(-5)).ToUniversalTime(); // Colombia: UTC-5; Npgsql solo escribe UTC

    private IServiceScope System(DateOnly day, int hour = 12)
    {
        var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(AtLocal(day, hour), day);
        return scope;
    }

    private IServiceScope AsSuperAdmin(DateOnly day)
    {
        var scope = System(day);
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), null, UserRole.SuperAdmin);
        return scope;
    }

    private async Task RunNoticesAsync(DateOnly day, int hour = 12)
    {
        using var scope = System(day, hour);
        await scope.ServiceProvider.GetRequiredService<RunSubscriptionNoticesHandler>().HandleAsync(default);
    }

    private async Task RunDispatcherAsync(DateOnly day, int hour)
    {
        using var scope = System(day, hour);
        await scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>().RunOnceAsync();
    }

    private async Task<List<Notification>> NoticesAsync(Guid gymId, NotificationType type)
    {
        using var scope = System(Today);
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>().AsNoTracking()
            .Where(n => n.GymId == gymId && n.Type == type)
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id)
            .ToListAsync();
    }

    private async Task<List<NotificationDelivery>> EmailsAsync(IEnumerable<Guid> notificationIds)
    {
        var ids = notificationIds.ToList();
        using var scope = System(Today);
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<NotificationDelivery>().AsNoTracking()
            .Where(d => ids.Contains(d.NotificationId) && d.Channel == DeliveryChannel.Email)
            .ToListAsync();
    }

    private async Task AddCoveredMembersAsync(Guid gymId, int count)
    {
        using var scope = System(Today);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var plan = await db.Set<MembershipPlan>().IgnoreQueryFilters().FirstOrDefaultAsync(p => p.GymId == gymId);
        if (plan is null)
        {
            plan = MembershipPlan.Create(gymId, "Mensual", null, 50_000m, 30);
            db.Add(plan);
        }

        for (var i = 0; i < count; i++)
        {
            var member = Member.Create(gymId, "Miembro", $"{i}", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-60));
            db.Add(member);
            db.Add(Membership.Create(gymId, member.Id, plan.Id, Today.AddDays(-5), 60));
        }

        await db.SaveChangesAsync();
    }

    private async Task<World> SeedAsync(
        GymStatus status, DateOnly startedOn, DateOnly? trialEndsOn, DateOnly paidThrough,
        int trialGraceDays = GymSubscription.DefaultTrialGraceDays, int? memberLimit = null)
    {
        using var scope = System(Today);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("S3 Gym", $"s3-{suffix}");
        if (status != GymStatus.Trial)
        {
            gym.SetCommercialStatus(status);
        }

        var subscription = GymSubscription.Create(gym.Id, 100_000m, startedOn, paidThrough, trialEndsOn);
        subscription.SetTrialGraceDays(trialGraceDays);
        if (memberLimit is { } limit)
        {
            subscription.SetMemberLimitOverride(limit);
        }

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), subscription);
        await db.SaveChangesAsync();

        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var emails = new List<string>();
        var ids = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var email = $"s3-owner{i}-{suffix}@test.local";
            ids.Add(await provisioning.CreateGymUserAsync(email, $"Owner {i}", Password, UserRole.Owner, gym.Id));
            emails.Add(email);
        }

        gym.AssignOwner(ids[0]);
        await db.SaveChangesAsync();
        return new World(gym.Id, ids, emails);
    }

    private sealed record World(Guid GymId, IReadOnlyList<Guid> OwnerIds, IReadOnlyList<string> OwnerEmails);
}
