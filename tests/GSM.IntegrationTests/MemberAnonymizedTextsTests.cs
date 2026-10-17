using GSM.Application.Common.Abstractions;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.IntegrationTests;

/// <summary>
/// M6-2: al anonimizar una ficha, los textos libres que otros módulos guardan sobre el miembro (motivo de "Dejar pasar",
/// notas de pagos, motivos de cancelación y de congelamiento) pasan a "[anonimizado]" por evento; montos y fechas no
/// se tocan. M6-4: una ficha anonimizada no admite notas ni mediciones nuevas (422).
/// </summary>
public sealed class MemberAnonymizedTextsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Replacement = "[anonimizado]";

    [Fact]
    public async Task Anonymizing_replaces_free_texts_of_attendance_payments_and_memberships_and_is_idempotent()
    {
        var (gymId, memberId, otherMemberId) = await SeedAsync();
        Guid attendanceId, otherAttendanceId, paymentId, otherPaymentId, membershipId, otherMembershipId, freezeId;
        DateOnly cancelledEnd;

        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var today = scope.ServiceProvider.GetRequiredService<IClock>().Today;
            var staff = Guid.CreateVersion7();

            var plan = MembershipPlan.Create(gymId, "Mensual", null, 50_000m, 30, maxFreezeDays: 10);
            db.Add(plan);

            var attendance = AttendanceEntity.Create(
                gymId, memberId, AttendanceMethod.Manual, DateTimeOffset.UtcNow, today, staff, "Dice que le pagan el viernes");
            var otherAttendance = AttendanceEntity.Create(
                gymId, otherMemberId, AttendanceMethod.Manual, DateTimeOffset.UtcNow, today, staff, "Motivo de otro");
            db.AddRange(attendance, otherAttendance);
            attendanceId = attendance.Id;
            otherAttendanceId = otherAttendance.Id;

            var payment = Payment.Create(
                gymId, memberId, null, 50_000m, PaymentMethod.Cash, "Mensualidad", "Pagó su mamá", staff, DateTimeOffset.UtcNow);
            var otherPayment = Payment.Create(
                gymId, otherMemberId, null, 30_000m, PaymentMethod.Cash, "Mensualidad", "Nota de otro", staff, DateTimeOffset.UtcNow);
            db.AddRange(payment, otherPayment);
            paymentId = payment.Id;
            otherPaymentId = otherPayment.Id;

            var cancelled = Membership.Create(gymId, memberId, plan.Id, today.AddDays(-40), 30);
            cancelled.Cancel("Se operó de la rodilla", DateTimeOffset.UtcNow);
            cancelledEnd = cancelled.EndDate;
            var frozen = Membership.Create(gymId, memberId, plan.Id, today, 30);
            var (freeze, _) = frozen.Freeze(plan, [], today, today, today.AddDays(2), "Viaje por tratamiento médico", false, staff);
            var other = Membership.Create(gymId, otherMemberId, plan.Id, today, 30);
            other.Cancel("Motivo de otro", DateTimeOffset.UtcNow);
            db.AddRange(cancelled, frozen, other, freeze);
            membershipId = cancelled.Id;
            freezeId = freeze.Id;
            otherMembershipId = other.Id;

            await db.SaveChangesAsync();
        }

        Assert.True(await AnonymizeAsync(gymId, memberId));
        // El evento puede reentregarse: una segunda anonimización no hace nada y el estado no cambia.
        Assert.False(await AnonymizeAsync(gymId, memberId));

        using var verify = System();
        var read = verify.ServiceProvider.GetRequiredService<GsmDbContext>();

        var a = await read.Set<AttendanceEntity>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == attendanceId);
        Assert.Equal(Replacement, a.AuthorizationReason);
        Assert.NotNull(a.AuthorizedBy);

        var p = await read.Set<Payment>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == paymentId);
        Assert.Equal(Replacement, p.Notes);
        Assert.Equal(50_000m, p.Amount);

        var m = await read.Set<Membership>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == membershipId);
        Assert.Equal(Replacement, m.CancellationReason);
        Assert.Equal(cancelledEnd, m.EndDate);

        var f = await read.Set<MembershipFreeze>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == freezeId);
        Assert.Equal(Replacement, f.Reason);
        Assert.Equal(3, f.DaysCredited);

        // Los de otro miembro no se tocan.
        Assert.Equal("Motivo de otro", (await read.Set<AttendanceEntity>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == otherAttendanceId)).AuthorizationReason);
        Assert.Equal("Nota de otro", (await read.Set<Payment>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == otherPaymentId)).Notes);
        Assert.Equal("Motivo de otro", (await read.Set<Membership>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == otherMembershipId)).CancellationReason);
    }

    [Fact]
    public async Task An_anonymized_sheet_rejects_new_notes_and_measurements_with_422()
    {
        var (gymId, memberId, _) = await SeedAsync();
        await AnonymizeAsync(gymId, memberId);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Owner);

        var note = await Assert.ThrowsAsync<ValidationDomainException>(
            () => scope.ServiceProvider.GetRequiredService<AddMemberNoteHandler>()
                .HandleAsync(memberId, new AddMemberNoteRequest("Nota nueva"), default));
        Assert.Equal("La ficha está anonimizada.", note.Message);

        var measurement = await Assert.ThrowsAsync<ValidationDomainException>(
            () => scope.ServiceProvider.GetRequiredService<AddMemberMeasurementHandler>()
                .HandleAsync(memberId, new AddMemberMeasurementRequest(null, 70m, 170m), default));
        Assert.Equal("La ficha está anonimizada.", measurement.Message);
    }

    // ---------- helpers ----------

    private async Task<bool> AnonymizeAsync(Guid gymId, Guid memberId)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<MemberAnonymizer>()
            .AnonymizeAsync(gymId, memberId, allowActive: true, default);
    }

    private async Task<(Guid GymId, Guid MemberId, Guid OtherMemberId)> SeedAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("M6 Textos", $"m6t-{Guid.NewGuid():N}"[..14]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));

        var joined = new DateOnly(2026, 1, 5);
        var member = Member.Create(gym.Id, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], joined);
        var other = Member.Create(gym.Id, "Luis", "Otro", $"doc-{Guid.NewGuid():N}"[..12], joined);
        db.AddRange(member, other);
        await db.SaveChangesAsync();
        return (gym.Id, member.Id, other.Id);
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }
}
