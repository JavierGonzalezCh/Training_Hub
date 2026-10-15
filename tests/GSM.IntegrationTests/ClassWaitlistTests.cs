using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Events;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
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

/// <summary>
/// Classes v2, C4: lista de espera con confirmación. Sesión llena → lista; al liberarse un cupo se ofrece al primero
/// con plazo; vence de forma perezosa (consulta o tarea periódica) y pasa al siguiente; cuenta para el máximo de
/// reservas; los avisos salen por evento y son idempotentes. Cada prueba siembra su propio gym.
/// </summary>
public sealed class ClassWaitlistTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly NextMonday = Monday.AddDays(7);
    private static readonly DateOnly NextTuesday = NextMonday.AddDays(1);

    // ---------- Unirse ----------

    [Fact]
    public async Task Joining_a_full_session_puts_the_member_last_in_line()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);

        var first = await JoinAsync(w, 1, classId);
        var second = await JoinAsync(w, 2, classId);

        Assert.Equal("Waiting", first.Status);
        Assert.Equal(1, first.Position);
        Assert.Equal(2, second.Position);
        Assert.Null(first.OfferExpiresAt);

        // Anotarse otra vez devuelve la misma entrada (idempotente), no una segunda.
        var again = await JoinAsync(w, 1, classId);
        Assert.Equal(first.EntryId, again.EntryId);
        Assert.Equal(2, (await EntriesAsync(w.GymId)).Count);
    }

    [Fact]
    public async Task Joining_is_refused_while_the_session_still_has_room_or_the_member_already_booked()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(w, capacity: 2);

        // Sin materializar: nadie ha reservado, no puede estar llena.
        await Assert.ThrowsAsync<ConflictException>(() => JoinAsync(w, 1, classId));

        await BookAsync(w, 0, classId);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => JoinAsync(w, 1, classId));
        Assert.Contains("tiene cupo", ex.Message);

        // Quien ya tiene reserva tampoco se anota.
        await BookAsync(w, 1, classId);
        var booked = await Assert.ThrowsAsync<ConflictException>(() => JoinAsync(w, 1, classId));
        Assert.Contains("Ya tienes una reserva", booked.Message);

        Assert.Empty(await EntriesAsync(w.GymId));
    }

    [Fact]
    public async Task A_member_on_the_list_cannot_book_the_session_directly()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => BookAsync(w, 1, classId));
        Assert.Contains("lista de espera", ex.Message);
    }

    // ---------- Oferta al liberarse un cupo ----------

    [Fact]
    public async Task Cancelling_a_booking_offers_the_spot_to_the_first_in_line_with_a_deadline_and_a_notice()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);

        var cancelAt = AtLocal(Monday, 12);
        using (var scope = AsMember(w, 0, cancelAt))
        {
            await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>().HandleAsync(classId, NextMonday, default);
        }

        var entries = await EntriesAsync(w.GymId);
        var first = entries.Single(e => e.MemberId == w.MemberIds[1]);
        var second = entries.Single(e => e.MemberId == w.MemberIds[2]);
        Assert.Equal(WaitlistEntryStatus.Offered, first.Status);
        Assert.Equal(cancelAt.AddMinutes(60), first.OfferExpiresAt);
        Assert.Equal(WaitlistEntryStatus.Waiting, second.Status);

        // Aviso urgente solo al primero, con la entrada como referencia.
        var notice = Assert.Single(await NoticesAsync(w.GymId, NotificationType.WaitlistOffer));
        Assert.Equal(w.MemberIds[1], notice.RecipientMemberId);
        Assert.Equal(first.Id, notice.ReferenceId);
        Assert.Equal(NotificationPriority.Urgent, notice.Priority);
    }

    [Fact]
    public async Task The_offer_notice_is_idempotent_when_the_event_repeats()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await CancelAsync(w, 0, classId, AtLocal(Monday, 12));

        var entry = (await EntriesAsync(w.GymId)).Single();
        using (var scope = System())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
            var repeated = new WaitlistOfferMade(
                w.GymId, w.MemberIds[1], classId, entry.Id, NextMonday, new TimeOnly(7, 0), entry.OfferExpiresAt!.Value);
            await publisher.PublishAsync(repeated, default);
            await publisher.PublishAsync(repeated, default);
        }

        Assert.Single(await NoticesAsync(w.GymId, NotificationType.WaitlistOffer));
    }

    [Fact]
    public async Task The_offer_never_goes_past_15_minutes_before_the_class_and_none_is_made_when_too_late()
    {
        var w = await SeedAsync(4);
        var classId = await CreateClassAsync(w, capacity: 2);
        await BookAsync(w, 0, classId);
        await BookAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);
        await JoinAsync(w, 3, classId);

        // 30 minutos antes de la clase (7:00): la oferta de 60 min se recorta a las 6:45.
        await CancelAsync(w, 0, classId, AtLocal(NextMonday, 6, 30));
        var offered = (await EntriesAsync(w.GymId)).Single(e => e.Status == WaitlistEntryStatus.Offered);
        Assert.Equal(w.MemberIds[2], offered.MemberId);
        Assert.Equal(AtLocal(NextMonday, 6, 45), offered.OfferExpiresAt);

        // A las 6:50 quedan menos de 15 minutos: se libera otro cupo pero ya no se ofrece.
        await CancelAsync(w, 1, classId, AtLocal(NextMonday, 6, 50));
        var third = (await EntriesAsync(w.GymId)).Single(e => e.MemberId == w.MemberIds[3]);
        Assert.Equal(WaitlistEntryStatus.Waiting, third.Status);
    }

    // ---------- Confirmar ----------

    [Fact]
    public async Task Confirming_within_the_deadline_books_the_spot_and_schedules_the_reminder()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await CancelAsync(w, 0, classId, AtLocal(Monday, 12));

        BookingResponse confirmed;
        using (var scope = AsMember(w, 1, AtLocal(Monday, 12, 30)))
        {
            confirmed = await scope.ServiceProvider.GetRequiredService<ConfirmWaitlistOfferHandler>()
                .HandleAsync(classId, NextMonday, default);
        }

        Assert.Equal(ClassBookingStatus.Booked.ToString(), confirmed.Status);
        Assert.Equal(WaitlistEntryStatus.Confirmed, (await EntriesAsync(w.GymId)).Single().Status);
        Assert.Equal(1, await BookedCountAsync(w, classId));

        // N5: la reserva confirmada programa su recordatorio de clase.
        var reminders = await NoticesAsync(w.GymId, NotificationType.ClassReminder);
        Assert.Single(reminders, n => n.RecipientMemberId == w.MemberIds[1]);
    }

    [Fact]
    public async Task Confirming_after_the_deadline_is_409_and_the_spot_goes_to_the_next_in_line()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);
        await CancelAsync(w, 0, classId, AtLocal(Monday, 12));

        var late = AtLocal(Monday, 13, 1); // 61 minutos después
        using (var scope = AsMember(w, 1, late))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<ConfirmWaitlistOfferHandler>().HandleAsync(classId, NextMonday, default));
            Assert.Contains("venció", ex.Message);
        }

        var entries = await EntriesAsync(w.GymId);
        Assert.Equal(WaitlistEntryStatus.Expired, entries.Single(e => e.MemberId == w.MemberIds[1]).Status);
        var next = entries.Single(e => e.MemberId == w.MemberIds[2]);
        Assert.Equal(WaitlistEntryStatus.Offered, next.Status);
        Assert.Equal(late.AddMinutes(60), next.OfferExpiresAt);
        Assert.Equal(0, await BookedCountAsync(w, classId));

        Assert.Single(await NoticesAsync(w.GymId, NotificationType.WaitlistOfferExpired));
        Assert.Equal(2, (await NoticesAsync(w.GymId, NotificationType.WaitlistOffer)).Count);
    }

    [Fact]
    public async Task Two_confirmations_for_the_same_spot_leave_exactly_one_booking()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);
        await CancelAsync(w, 0, classId, AtLocal(Monday, 12));

        // La oferta del primero venció: al mismo tiempo él (tarde) y el siguiente confirman.
        var late = AtLocal(Monday, 13, 5);
        var results = await Task.WhenAll(
            TryConfirmAsync(w, 1, classId, late),
            TryConfirmAsync(w, 2, classId, late));

        Assert.Equal(1, results.Count(ok => ok));
        Assert.Equal(1, await BookedCountAsync(w, classId));
        Assert.Equal(WaitlistEntryStatus.Confirmed, (await EntriesAsync(w.GymId)).Single(e => e.MemberId == w.MemberIds[2]).Status);
    }

    [Fact]
    public async Task A_reserved_spot_cannot_be_taken_by_a_direct_booking_while_the_offer_is_open()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await CancelAsync(w, 0, classId, AtLocal(Monday, 12));

        // El cupo está apartado para el miembro 1: el miembro 2 no puede reservarlo por la puerta de al lado.
        using (var scope = AsMember(w, 2, AtLocal(Monday, 12, 10)))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<BookClassHandler>().HandleAsync(classId, NextMonday, default));
            Assert.Contains("completa", ex.Message);
        }

        // Pasada la oferta sin respuesta y sin nadie más en la lista, el cupo vuelve a estar libre.
        using (var scope = AsMember(w, 2, AtLocal(Monday, 13, 5)))
        {
            var ok = await scope.ServiceProvider.GetRequiredService<BookClassHandler>()
                .HandleAsync(classId, NextMonday, default);
            Assert.Equal(ClassBookingStatus.Booked.ToString(), ok.Status);
        }
    }

    // ---------- Salir / rechazar ----------

    [Fact]
    public async Task Declining_the_offer_passes_the_spot_to_the_next_and_leaving_loses_the_place()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);
        await CancelAsync(w, 0, classId, AtLocal(Monday, 12));

        using (var scope = AsMember(w, 1, AtLocal(Monday, 12, 5)))
        {
            await scope.ServiceProvider.GetRequiredService<LeaveWaitlistHandler>().HandleAsync(classId, NextMonday, default);
        }

        var entries = await EntriesAsync(w.GymId);
        Assert.Equal(WaitlistEntryStatus.Left, entries.Single(e => e.MemberId == w.MemberIds[1]).Status);
        Assert.Equal(WaitlistEntryStatus.Offered, entries.Single(e => e.MemberId == w.MemberIds[2]).Status);

        // Quien salió y vuelve a anotarse queda al final (otra sesión llena: aquí, tras el miembro 2).
        var back = await JoinAsync(w, 1, classId, AtLocal(Monday, 12, 6));
        Assert.Equal(2, back.Position);
        Assert.Equal(3, (await EntriesAsync(w.GymId)).Count);
    }

    // ---------- Vencimiento perezoso: roster y tarea periódica ----------

    [Fact]
    public async Task The_roster_shows_the_list_in_order_and_expires_overdue_offers_when_viewed()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);
        await CancelAsync(w, 0, classId, AtLocal(Monday, 12));

        RosterResponse before;
        using (var scope = AsOwner(w, AtLocal(Monday, 12, 20)))
        {
            before = await scope.ServiceProvider.GetRequiredService<GetRosterHandler>().HandleAsync(classId, NextMonday, default);
        }

        Assert.Equal(new[] { "Offered", "Waiting" }, before.Waitlist!.Select(x => x.Status).ToArray());
        Assert.Equal(new[] { 1, 2 }, before.Waitlist!.Select(x => x.Position).ToArray());

        // Una hora después, solo mirar el roster vence la oferta y pasa el cupo.
        RosterResponse after;
        using (var scope = AsOwner(w, AtLocal(Monday, 13, 10)))
        {
            after = await scope.ServiceProvider.GetRequiredService<GetRosterHandler>().HandleAsync(classId, NextMonday, default);
        }

        var only = Assert.Single(after.Waitlist!);
        Assert.Equal("Offered", only.Status);
        Assert.Equal(1, only.Position);
    }

    [Fact]
    public async Task The_periodic_job_expires_overdue_offers_and_is_idempotent()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);
        await CancelAsync(w, 0, classId, AtLocal(Monday, 12));

        await RunJobAtAsync(AtLocal(Monday, 12, 30)); // dentro del plazo: no cambia nada
        Assert.Equal(WaitlistEntryStatus.Offered, (await EntriesAsync(w.GymId)).Single(e => e.MemberId == w.MemberIds[1]).Status);

        await RunJobAtAsync(AtLocal(Monday, 13, 5));
        await RunJobAtAsync(AtLocal(Monday, 13, 6)); // repetirla no vuelve a vencer ni a avisar
        var entries = await EntriesAsync(w.GymId);
        Assert.Equal(WaitlistEntryStatus.Expired, entries.Single(e => e.MemberId == w.MemberIds[1]).Status);
        Assert.Equal(WaitlistEntryStatus.Offered, entries.Single(e => e.MemberId == w.MemberIds[2]).Status);
        Assert.Single(await NoticesAsync(w.GymId, NotificationType.WaitlistOfferExpired));
        Assert.Equal(2, (await NoticesAsync(w.GymId, NotificationType.WaitlistOffer)).Count);
    }

    [Fact]
    public async Task The_list_closes_when_the_class_starts_without_notifying_those_who_never_got_an_offer()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);

        await RunJobAtAsync(AtLocal(NextMonday, 7, 5));

        Assert.Equal(WaitlistEntryStatus.Expired, (await EntriesAsync(w.GymId)).Single().Status);
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.WaitlistOfferExpired));
        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.WaitlistOffer));
    }

    // ---------- Máximo de reservas ----------

    [Fact]
    public async Task The_waitlist_counts_for_the_max_of_active_bookings()
    {
        var w = await SeedAsync(2);
        var full = await CreateClassAsync(w, capacity: 1, days: [ClassDayOfWeek.Monday]);
        var other = await CreateClassAsync(w, capacity: 5, days: [ClassDayOfWeek.Tuesday]);
        await UpdateSettingsAsync(w, new UpdateClassSettingsRequest(2, 3, 30, 7, 14, 1));
        await BookAsync(w, 0, full);

        // Con máximo 1, estar en la lista llena el cupo del miembro: ya no puede reservar otra clase...
        await JoinAsync(w, 1, full);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => BookAsync(w, 1, other, NextTuesday));
        Assert.Contains("máximo es 1", ex.Message);

        using (var scope = AsMember(w, 1, AtLocal(Monday, 12)))
        {
            var status = await scope.ServiceProvider.GetRequiredService<GetMyBookingStatusHandler>().HandleAsync(default);
            Assert.Equal(1, status.ActiveBookings);
        }

        // ...y al revés: con una reserva ya hecha, no puede anotarse en una lista.
        var third = await SeedExtraMemberAsync(w);
        await BookAsync(w, third, other, NextTuesday);
        await Assert.ThrowsAsync<ConflictException>(() => JoinAsync(w, third, full));

        // Salir de la lista libera el lugar del máximo.
        using (var scope = AsMember(w, 1, AtLocal(Monday, 12)))
        {
            await scope.ServiceProvider.GetRequiredService<LeaveWaitlistHandler>().HandleAsync(full, NextMonday, default);
        }

        await BookAsync(w, 1, other, NextTuesday);
    }

    [Fact]
    public async Task Confirming_an_offer_does_not_count_twice_against_the_max()
    {
        var w = await SeedAsync(2);
        var classId = await CreateClassAsync(w, capacity: 1);
        await UpdateSettingsAsync(w, new UpdateClassSettingsRequest(2, 3, 30, 7, 14, 1));
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId); // ocupa el único lugar del máximo
        await CancelAsync(w, 0, classId, AtLocal(Monday, 12));

        using var scope = AsMember(w, 1, AtLocal(Monday, 12, 5));
        var ok = await scope.ServiceProvider.GetRequiredService<ConfirmWaitlistOfferHandler>()
            .HandleAsync(classId, NextMonday, default);
        Assert.Equal(ClassBookingStatus.Booked.ToString(), ok.Status);
    }

    // ---------- Ajuste del tiempo para confirmar ----------

    [Fact]
    public async Task The_offer_minutes_are_a_per_gym_setting_with_a_default_of_60()
    {
        var w = await SeedAsync(2);
        using (var scope = Owner(w))
        {
            var s = await scope.ServiceProvider.GetRequiredService<GetClassSettingsHandler>().HandleAsync(default);
            Assert.Equal(60, s.WaitlistOfferMinutes);

            var handler = scope.ServiceProvider.GetRequiredService<UpdateClassSettingsHandler>();
            await Assert.ThrowsAsync<ValidationDomainException>(
                () => handler.HandleAsync(new UpdateClassSettingsRequest(2, 3, 30, 7, 7, 5, 2), default));

            var saved = await handler.HandleAsync(new UpdateClassSettingsRequest(2, 3, 30, 7, 7, 5, 30), default);
            Assert.Equal(30, saved.WaitlistOfferMinutes);

            // Sin mandarlo, conserva el valor actual.
            var kept = await handler.HandleAsync(new UpdateClassSettingsRequest(2, 3, 30, 7, 7, 5), default);
            Assert.Equal(30, kept.WaitlistOfferMinutes);
        }

        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        var cancelAt = AtLocal(Monday, 12);
        await CancelAsync(w, 0, classId, cancelAt);
        Assert.Equal(cancelAt.AddMinutes(30), (await EntriesAsync(w.GymId)).Single().OfferExpiresAt);
    }

    // ---------- Horario del miembro ----------

    [Fact]
    public async Task The_member_schedule_marks_full_sessions_and_shows_the_place_in_line()
    {
        var w = await SeedAsync(3);
        var classId = await CreateClassAsync(w, capacity: 1);
        await BookAsync(w, 0, classId);
        await JoinAsync(w, 1, classId);
        await JoinAsync(w, 2, classId);

        using var scope = AsMember(w, 2, AtLocal(Monday, 12));
        var schedule = await scope.ServiceProvider.GetRequiredService<GetMyClassesHandler>()
            .HandleAsync(NextMonday, NextMonday, default);

        var entry = Assert.Single(schedule.Entries);
        Assert.True(entry.IsFull);
        Assert.Equal("Waiting", entry.MyWaitlistStatus);
        Assert.Equal(2, entry.MyWaitlistPosition);
    }

    // ---------- Siembra y ayudas ----------

    private async Task<Guid> CreateClassAsync(World w, int? capacity = 1, ClassDayOfWeek[]? days = null)
    {
        using var scope = Owner(w);
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>().HandleAsync(
            new SaveClassRequest("Spinning", null, "Ana", capacity, new TimeOnly(7, 0), 45,
                IsRecurring: true, DaysOfWeek: days ?? [ClassDayOfWeek.Monday], ScheduledDate: null), default);
        return created.Id;
    }

    private async Task UpdateSettingsAsync(World w, UpdateClassSettingsRequest request)
    {
        using var scope = Owner(w);
        await scope.ServiceProvider.GetRequiredService<UpdateClassSettingsHandler>().HandleAsync(request, default);
    }

    private async Task BookAsync(World w, int memberIndex, Guid classId, DateOnly? date = null)
    {
        using var scope = AsMember(w, memberIndex, AtLocal(Monday, 12));
        await scope.ServiceProvider.GetRequiredService<BookClassHandler>().HandleAsync(classId, date ?? NextMonday, default);
    }

    private async Task<WaitlistResponse> JoinAsync(World w, int memberIndex, Guid classId, DateTimeOffset? at = null)
    {
        using var scope = AsMember(w, memberIndex, at ?? AtLocal(Monday, 12));
        return await scope.ServiceProvider.GetRequiredService<JoinWaitlistHandler>().HandleAsync(classId, NextMonday, default);
    }

    private async Task CancelAsync(World w, int memberIndex, Guid classId, DateTimeOffset at)
    {
        using var scope = AsMember(w, memberIndex, at);
        await scope.ServiceProvider.GetRequiredService<CancelBookingHandler>().HandleAsync(classId, NextMonday, default);
    }

    private async Task<bool> TryConfirmAsync(World w, int memberIndex, Guid classId, DateTimeOffset at)
    {
        using var scope = AsMember(w, memberIndex, at);
        try
        {
            await scope.ServiceProvider.GetRequiredService<ConfirmWaitlistOfferHandler>().HandleAsync(classId, NextMonday, default);
            return true;
        }
        catch (ConflictException)
        {
            return false;
        }
    }

    private async Task RunJobAtAsync(DateTimeOffset at)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(at, DayOf(at));
        await scope.ServiceProvider.GetRequiredService<RunWaitlistJobHandler>().HandleAsync(default);
    }

    private Task<List<WaitlistEntry>> EntriesAsync(Guid gymId)
        => DbAsync(db => db.Set<WaitlistEntry>().Where(e => e.GymId == gymId).OrderBy(e => e.Position).ToListAsync());

    private Task<List<Notification>> NoticesAsync(Guid gymId, NotificationType type)
        => DbAsync(db => db.Set<Notification>().Where(n => n.GymId == gymId && n.Type == type).ToListAsync());

    private Task<int> BookedCountAsync(World w, Guid classId)
        => DbAsync(db => (
            from b in db.Set<ClassBooking>()
            join s in db.Set<ClassSession>() on b.SessionId equals s.Id
            where b.GymId == w.GymId && s.ClassId == classId && b.Status == ClassBookingStatus.Booked
            select b.Id).CountAsync());

    private async Task<T> DbAsync<T>(Func<GsmDbContext, Task<T>> query)
    {
        using var scope = System();
        return await query(scope.ServiceProvider.GetRequiredService<GsmDbContext>());
    }

    private static DateTimeOffset AtLocal(DateOnly day, int hour, int minute = 0)
        => new GSM.Infrastructure.Common.SystemClock(
                Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions { TimeZone = "America/Bogota" }))
            .BusinessDayRange(day).Start.AddHours(hour).AddMinutes(minute);

    private static DateOnly DayOf(DateTimeOffset instant)
        => DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Owner(World w) => AsOwner(w, AtLocal(Monday, 9));

    private IServiceScope AsOwner(World w, DateTimeOffset at)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(at, DayOf(at));
        return scope;
    }

    private IServiceScope AsMember(World w, int index, DateTimeOffset at)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserIds[index], w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(at, DayOf(at));
        return scope;
    }

    private async Task<int> SeedExtraMemberAsync(World w)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var u = MakeUser($"wl-{suffix}-x");
        var m = Member.Create(w.GymId, "Miembro", "X", $"doc-{suffix}-x", new DateOnly(2026, 1, 1));
        m.LinkUser(u.Id);
        var plan = await db.Set<MembershipPlan>().FirstAsync(p => p.GymId == w.GymId);
        db.Add(u);
        db.Add(m);
        db.Add(Membership.Create(w.GymId, m.Id, plan.Id, Monday.AddDays(-5), 120));
        await db.SaveChangesAsync();
        w.MemberIds.Add(m.Id);
        w.MemberUserIds.Add(u.Id);
        return w.MemberIds.Count - 1;
    }

    private async Task<World> SeedAsync(int members)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("WL", $"wl-{suffix}");
        var staff = MakeUser($"wl-{suffix}-staff");
        gym.AssignOwner(staff.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);

        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.Add(plan);

        var memberIds = new List<Guid>();
        var memberUserIds = new List<Guid>();
        for (var i = 0; i < members; i++)
        {
            var u = MakeUser($"wl-{suffix}-m{i}");
            var m = Member.Create(gym.Id, "Miembro", $"{i}", $"doc-{suffix}-{i}", new DateOnly(2026, 1, 1));
            m.LinkUser(u.Id);
            db.Add(u);
            db.Add(m);
            db.Add(Membership.Create(gym.Id, m.Id, plan.Id, Monday.AddDays(-5), 120));
            memberIds.Add(m.Id);
            memberUserIds.Add(u.Id);
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, staff.Id, memberIds, memberUserIds);
    }

    private static GSM.Infrastructure.Identity.AppUser MakeUser(string local)
        => new()
        {
            UserName = $"{local}@test.local",
            Email = $"{local}@test.local",
            NormalizedUserName = $"{local}@TEST.LOCAL",
            NormalizedEmail = $"{local}@TEST.LOCAL",
            EmailConfirmed = true,
            FullName = local,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

    private sealed record World(Guid GymId, Guid StaffId, List<Guid> MemberIds, List<Guid> MemberUserIds);
}
