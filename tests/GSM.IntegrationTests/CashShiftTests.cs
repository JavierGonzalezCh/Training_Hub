using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>P3: caja por turno con arqueo (Payments v2 §4): abrir, cobrar, movimientos, cerrar, anulaciones en efectivo.</summary>
public sealed class CashShiftTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day = new(2026, 4, 14);

    // ---------------------------------------------------------------- abrir

    [Fact]
    public async Task Only_one_shift_can_be_open_per_gym_and_the_second_open_is_a_conflict()
    {
        var w = await SeedAsync();

        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            var shift = await ana.OpenCashAsync(50_000m);
            Assert.True(shift.IsOpen);
            Assert.Equal(50_000m, shift.OpeningCash);
            Assert.Equal(w.AnaId, shift.OpenedBy);
            Assert.Equal("Ana", shift.OpenedByName);
        }

        using var beto = At(w, w.BetoId, UserRole.Staff, Hours(9));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => beto.OpenCashAsync(10_000m));
        Assert.Contains("Ana", ex.Message);
    }

    [Fact]
    public async Task Opening_with_a_negative_base_is_rejected()
    {
        var w = await SeedAsync();
        using var ana = At(w, w.AnaId, UserRole.Staff, Hours(8));

        await Assert.ThrowsAsync<ValidationDomainException>(() => ana.OpenCashAsync(-1m));
    }

    [Fact]
    public async Task The_status_suggests_the_last_counted_cash_as_the_next_base()
    {
        var w = await SeedAsync();

        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync(20_000m);
            await ana.CloseCashAsync(20_000m);
        }

        using var beto = At(w, w.BetoId, UserRole.Staff, Hours(14));
        var status = await beto.ServiceProvider.GetRequiredService<GetCashDrawerStatusHandler>().HandleAsync(default);

        Assert.Null(status.Shift);
        Assert.Equal(20_000m, status.SuggestedOpeningCash);
    }

    // ---------------------------------------------------------------- cobrar

    [Fact]
    public async Task Cash_needs_an_open_shift_but_other_methods_do_not_and_every_payment_is_linked_to_the_open_shift()
    {
        var w = await SeedAsync();

        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => Record(ana, 10_000m, PaymentMethod.Cash));
            Assert.Contains("Abre la caja", ex.Message);

            var withoutShift = await Record(ana, 20_000m, PaymentMethod.Nequi);
            Assert.Null(withoutShift.ShiftId);
        }

        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(9)))
        {
            var shift = await ana.OpenCashAsync();
            var cash = await Record(ana, 10_000m, PaymentMethod.Cash);
            var card = await Record(ana, 30_000m, PaymentMethod.Card);

            Assert.Equal(shift.Id, cash.ShiftId);
            Assert.Equal(shift.Id, card.ShiftId);
            Assert.True(cash.AffectsCashDrawer);
            Assert.False(card.AffectsCashDrawer);
        }
    }

    [Fact]
    public async Task A_second_staff_charges_into_the_open_shift_when_it_is_not_forgotten()
    {
        var w = await SeedAsync();
        CashShiftResponse shift;
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            shift = await ana.OpenCashAsync();
        }

        using var beto = At(w, w.BetoId, UserRole.Staff, Hours(10));
        var payment = await Record(beto, 5_000m, PaymentMethod.Cash);

        Assert.Equal(shift.Id, payment.ShiftId);
    }

    [Fact]
    public async Task A_forgotten_shift_blocks_cash_from_others_who_must_close_it_but_not_the_owner()
    {
        var w = await SeedAsync();
        CashShiftResponse shift;
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            shift = await ana.OpenCashAsync(10_000m);
        }

        // Pasaron 22 horas (> 16): quedó olvidada.
        using (var beto = At(w, w.BetoId, UserRole.Staff, Hours(30)))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => Record(beto, 5_000m, PaymentMethod.Cash));
            Assert.Contains("La caja de Ana sigue abierta", ex.Message);

            // No bloquea los cobros que no son en efectivo.
            await Record(beto, 5_000m, PaymentMethod.Card);

            // Nadie abre otra encima.
            await Assert.ThrowsAsync<ConflictException>(() => beto.OpenCashAsync());

            // Beto la cierra con su conteo y recién entonces abre la suya.
            var report = await beto.CloseCashAsync(10_000m);
            Assert.Equal(w.BetoId, report.Shift.ClosedBy);
            Assert.True(report.Shift.IsOpen == false);
            await beto.OpenCashAsync(10_000m);
        }

        // El Owner puede cobrar en efectivo aunque la de otro siga abierta.
        var w2 = await SeedAsync();
        using (var ana = At(w2, w2.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync();
        }

        using var owner = At(w2, w2.OwnerId, UserRole.Owner, Hours(30));
        var stale = (await owner.ServiceProvider.GetRequiredService<GetCashDrawerStatusHandler>().HandleAsync(default)).Shift!;
        Assert.True(stale.IsStale);
        var paid = await Record(owner, 7_000m, PaymentMethod.Cash);
        Assert.NotNull(paid.ShiftId);
        Assert.NotEqual(shift.Id, paid.ShiftId);
    }

    [Fact]
    public async Task A_shift_is_not_stale_before_sixteen_hours()
    {
        var w = await SeedAsync();
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync();
        }

        using var beto = At(w, w.BetoId, UserRole.Staff, Hours(8 + 15));
        var status = await beto.ServiceProvider.GetRequiredService<GetCashDrawerStatusHandler>().HandleAsync(default);

        Assert.False(status.Shift!.IsStale);
    }

    // ---------------------------------------------------------------- movimientos

    [Fact]
    public async Task Movements_need_an_open_shift_a_reason_and_belong_to_who_opened_it_or_the_owner()
    {
        var w = await SeedAsync();

        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await Assert.ThrowsAsync<ConflictException>(() => Move(ana, "withdrawal", 1_000m, "Sin caja"));
            await ana.OpenCashAsync(100_000m);

            await Assert.ThrowsAsync<ValidationDomainException>(() => Move(ana, "withdrawal", 1_000m, ""));
            await Assert.ThrowsAsync<ValidationDomainException>(() => Move(ana, "withdrawal", 0m, "Cero"));
            await Assert.ThrowsAsync<ValidationDomainException>(() => Move(ana, "otro", 1_000m, "Tipo malo"));

            var withdrawal = await Move(ana, "withdrawal", 40_000m, "Llevar al banco");
            Assert.Equal("withdrawal", withdrawal.Type);
            Assert.Equal("Ana", withdrawal.CreatedByName);
        }

        using (var beto = At(w, w.BetoId, UserRole.Staff, Hours(9)))
        {
            await Assert.ThrowsAsync<ForbiddenDomainException>(() => Move(beto, "deposit", 5_000m, "Cambio"));
        }

        using var owner = At(w, w.OwnerId, UserRole.Owner, Hours(10));
        var deposit = await Move(owner, "deposit", 5_000m, "Cambio");
        Assert.Equal("deposit", deposit.Type);

        var status = await owner.ServiceProvider.GetRequiredService<GetCashDrawerStatusHandler>().HandleAsync(default);
        Assert.Equal(65_000m, status.Shift!.ExpectedCash); // 100k - 40k + 5k
    }

    [Fact]
    public async Task A_withdrawal_larger_than_the_expected_cash_is_rejected_and_the_exact_amount_is_allowed()
    {
        var w = await SeedAsync();

        using var ana = At(w, w.AnaId, UserRole.Staff, Hours(8));
        await ana.OpenCashAsync(50_000m);
        await Record(ana, 20_000m, PaymentMethod.Cash);
        await Record(ana, 90_000m, PaymentMethod.Nequi); // no es efectivo: no cuenta

        var ex = await Assert.ThrowsAsync<ValidationDomainException>(() => Move(ana, "withdrawal", 70_001m, "Banco"));
        Assert.Equal("No puedes retirar más efectivo del que hay en la caja ($70.000).", ex.Message);

        // Rechazado no deja rastro: el esperado sigue intacto y se puede retirar justo lo que hay.
        var status = await ana.ServiceProvider.GetRequiredService<GetCashDrawerStatusHandler>().HandleAsync(default);
        Assert.Equal(70_000m, status.Shift!.ExpectedCash);
        await Move(ana, "withdrawal", 70_000m, "Banco");

        // Un ingreso nunca se limita por el esperado y sube el tope de los retiros siguientes.
        await Move(ana, "deposit", 500_000m, "Cambio");
        await Assert.ThrowsAsync<ValidationDomainException>(() => Move(ana, "withdrawal", 500_001m, "Banco"));
    }

    [Fact]
    public async Task Two_simultaneous_openings_leave_the_second_as_a_conflict_not_a_server_error()
    {
        var w = await SeedAsync();

        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync(10_000m);
        }

        // La segunda apertura salta la comprobación del caso de uso (la carrera) y choca con el índice único.
        using var beto = At(w, w.BetoId, UserRole.Staff, Hours(8));
        beto.ServiceProvider.GetRequiredService<GSM.Application.Payments.Abstractions.ICashShiftRepository>()
            .Add(CashShift.Open(w.GymId, w.BetoId, Hours(8), 5_000m));

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => beto.ServiceProvider.GetRequiredService<GSM.Application.Common.Abstractions.IUnitOfWork>().SaveChangesAsync());
        Assert.Equal("Ya hay una caja abierta.", ex.Message);
    }

    // ---------------------------------------------------------------- cerrar

    [Fact]
    public async Task Closing_computes_and_stores_the_expected_cash_and_the_difference()
    {
        var w = await SeedAsync();
        CashShiftReportResponse report;
        Guid shiftId;

        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            shiftId = (await ana.OpenCashAsync(50_000m)).Id;
            await Record(ana, 100_000m, PaymentMethod.Cash);
            await Record(ana, 30_000m, PaymentMethod.Card);
            await Record(ana, 20_000m, PaymentMethod.Nequi);
            await Record(ana, -15_000m, PaymentMethod.Cash, "Devolución parcial");
            await Move(ana, "deposit", 10_000m, "Cambio");
            await Move(ana, "withdrawal", 20_000m, "Domicilio");

            // 50 + 100 - 15 + 10 - 20 = 125k; cuenta 120k: faltan 5k.
            var noNote = await Assert.ThrowsAsync<ValidationDomainException>(() => ana.CloseCashAsync(120_000m));
            Assert.Contains("nota", noNote.Message);

            report = await ana.CloseCashAsync(120_000m, "Faltan 5 mil, se pagó un domicilio sin anotar");
        }

        Assert.Equal(125_000m, report.Shift.ExpectedCash);
        Assert.Equal(120_000m, report.Shift.CountedCash);
        Assert.Equal(-5_000m, report.Shift.Difference);
        Assert.False(report.Shift.IsOpen);
        Assert.Equal(100_000m, report.CashIn);
        Assert.Equal(15_000m, report.CashOut);
        Assert.Equal(10_000m, report.Deposits);
        Assert.Equal(20_000m, report.Withdrawals);
        Assert.Equal(2, report.Movements.Count);
        Assert.Equal(4, report.Payments.Count);
        Assert.Equal(3, report.Methods.Count);
        Assert.Equal(85_000m, report.Methods.Single(m => m.Method == "Cash").Total);
        Assert.Equal(2, report.Methods.Single(m => m.Method == "Cash").Count);

        // Lo guardado no cambia aunque después entren más cobros al gym.
        using (var beto = At(w, w.BetoId, UserRole.Staff, Hours(12)))
        {
            await beto.OpenCashAsync();
            await Record(beto, 999_000m, PaymentMethod.Cash);
        }

        using var owner = At(w, w.OwnerId, UserRole.Owner, Hours(13));
        var again = await owner.ServiceProvider.GetRequiredService<GetCashShiftReportHandler>().HandleAsync(shiftId, default);
        Assert.Equal(125_000m, again.Shift.ExpectedCash);
        Assert.Equal(-5_000m, again.Shift.Difference);
    }

    [Fact]
    public async Task A_balanced_close_needs_no_note_and_a_closed_shift_cannot_be_closed_again()
    {
        var w = await SeedAsync();
        using var ana = At(w, w.AnaId, UserRole.Staff, Hours(8));
        await ana.OpenCashAsync(10_000m);
        await Record(ana, 40_000m, PaymentMethod.Cash);

        var report = await ana.CloseCashAsync(50_000m);

        Assert.Equal(0m, report.Shift.Difference);
        Assert.Null(report.Shift.CloseNotes);
        await Assert.ThrowsAsync<ConflictException>(() => ana.CloseCashAsync(50_000m));
    }

    [Fact]
    public async Task Only_who_opened_or_the_owner_closes_a_shift_that_is_not_forgotten()
    {
        var w = await SeedAsync();
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync();
        }

        using (var beto = At(w, w.BetoId, UserRole.Staff, Hours(9)))
        {
            await Assert.ThrowsAsync<ForbiddenDomainException>(() => beto.CloseCashAsync(0m));
        }

        using var owner = At(w, w.OwnerId, UserRole.Owner, Hours(10));
        var report = await owner.CloseCashAsync(0m);
        Assert.Equal(w.OwnerId, report.Shift.ClosedBy);
    }

    [Fact]
    public async Task Closing_without_an_open_shift_is_a_conflict()
    {
        var w = await SeedAsync();
        using var ana = At(w, w.AnaId, UserRole.Staff, Hours(8));

        await Assert.ThrowsAsync<ConflictException>(() => ana.CloseCashAsync(0m));
    }

    // ---------------------------------------------------------------- anulaciones en efectivo

    [Fact]
    public async Task Voiding_a_cash_payment_of_the_current_shift_enters_that_same_drawer_and_nets_to_zero()
    {
        var w = await SeedAsync();
        using var ana = At(w, w.AnaId, UserRole.Owner, Hours(8));
        var shift = await ana.OpenCashAsync(10_000m);
        var payment = await Record(ana, 30_000m, PaymentMethod.Cash);

        var reversal = await Void(ana, payment.Id, "Error de cobro", null);

        Assert.Equal(shift.Id, reversal.ShiftId);
        Assert.True(reversal.AffectsCashDrawer);
        var status = await ana.ServiceProvider.GetRequiredService<GetCashDrawerStatusHandler>().HandleAsync(default);
        Assert.Equal(10_000m, status.Shift!.ExpectedCash);
    }

    [Fact]
    public async Task Voiding_cash_of_a_closed_shift_asks_whether_the_money_is_returned_now()
    {
        var w = await SeedAsync();
        Guid paymentId;
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync();
            paymentId = (await Record(ana, 30_000m, PaymentMethod.Cash)).Id;
            await ana.CloseCashAsync(30_000m);
        }

        using var owner = At(w, w.OwnerId, UserRole.Owner, Hours(20));
        await owner.OpenCashAsync(100_000m);

        // Sin respuesta: 422.
        await Assert.ThrowsAsync<ValidationDomainException>(() => Void(owner, paymentId, "Cobro duplicado", null));

        var reversal = await Void(owner, paymentId, "Cobro duplicado", true);
        Assert.True(reversal.AffectsCashDrawer);
        Assert.NotNull(reversal.ShiftId);

        var status = await owner.ServiceProvider.GetRequiredService<GetCashDrawerStatusHandler>().HandleAsync(default);
        Assert.Equal(70_000m, status.Shift!.ExpectedCash); // la devolución salió de la caja abierta
    }

    [Fact]
    public async Task Voiding_cash_of_a_closed_shift_whose_money_never_existed_does_not_touch_the_open_drawer()
    {
        var w = await SeedAsync();
        Guid paymentId;
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync();
            paymentId = (await Record(ana, 30_000m, PaymentMethod.Cash)).Id;
            await ana.CloseCashAsync(30_000m);
        }

        using var owner = At(w, w.OwnerId, UserRole.Owner, Hours(20));
        await owner.OpenCashAsync(100_000m);

        var reversal = await Void(owner, paymentId, "Fue un error de registro", false);

        Assert.False(reversal.AffectsCashDrawer);
        var status = await owner.ServiceProvider.GetRequiredService<GetCashDrawerStatusHandler>().HandleAsync(default);
        Assert.Equal(100_000m, status.Shift!.ExpectedCash);

        // Se ve en el reporte del turno abierto, marcada como anulación que no toca la caja.
        var report = await owner.ServiceProvider.GetRequiredService<GetCashShiftReportHandler>().HandleAsync(status.Shift.Id, default);
        var shown = Assert.Single(report.Reversals);
        Assert.False(shown.AffectsCashDrawer);
    }

    [Fact]
    public async Task Refunding_now_needs_an_open_shift()
    {
        var w = await SeedAsync();
        Guid paymentId;
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync();
            paymentId = (await Record(ana, 30_000m, PaymentMethod.Cash)).Id;
            await ana.CloseCashAsync(30_000m);
        }

        using var owner = At(w, w.OwnerId, UserRole.Owner, Hours(20));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => Void(owner, paymentId, "Devolver", true));
        Assert.Contains("Abre la caja", ex.Message);
    }

    [Fact]
    public async Task Voiding_a_non_cash_payment_never_moves_the_drawer()
    {
        var w = await SeedAsync();
        using var owner = At(w, w.OwnerId, UserRole.Owner, Hours(8));
        await owner.OpenCashAsync(10_000m);
        var payment = await Record(owner, 80_000m, PaymentMethod.Nequi);

        var reversal = await Void(owner, payment.Id, "Monto mal digitado", null);

        Assert.False(reversal.AffectsCashDrawer);
        var status = await owner.ServiceProvider.GetRequiredService<GetCashDrawerStatusHandler>().HandleAsync(default);
        Assert.Equal(10_000m, status.Shift!.ExpectedCash);
    }

    [Fact]
    public async Task Approving_a_void_request_takes_the_refund_decision_too()
    {
        var w = await SeedAsync();
        Guid paymentId;
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync();
            paymentId = (await Record(ana, 30_000m, PaymentMethod.Cash)).Id;
            await ana.CloseCashAsync(30_000m);
        }

        Guid requestId;
        using (var beto = At(w, w.BetoId, UserRole.Staff, Hours(19)))
        {
            requestId = (await beto.ServiceProvider.GetRequiredService<RequestVoidHandler>()
                .HandleAsync(paymentId, new RequestVoidRequest("Cobro duplicado"), default)).Id;
        }

        using var owner = At(w, w.OwnerId, UserRole.Owner, Hours(20));
        await owner.OpenCashAsync(50_000m);

        var pending = await owner.ServiceProvider.GetRequiredService<ListVoidRequestsHandler>()
            .HandleAsync(new ListVoidRequestsQuery { Status = "pending" }, default);
        Assert.True(Assert.Single(pending.Items).RequiresRefundDecision);

        var approve = owner.ServiceProvider.GetRequiredService<ApproveVoidRequestHandler>();
        await Assert.ThrowsAsync<ValidationDomainException>(() => approve.HandleAsync(requestId, null, default));

        var reversal = await approve.HandleAsync(requestId, new ApproveVoidRequestRequest(false), default);
        Assert.False(reversal.AffectsCashDrawer);
    }

    // ---------------------------------------------------------------- quién ve qué

    [Fact]
    public async Task Staff_sees_the_report_of_their_own_shift_but_not_of_others_without_ViewFinances()
    {
        var w = await SeedAsync();
        Guid shiftId;
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            shiftId = (await ana.OpenCashAsync()).Id;
            await Record(ana, 10_000m, PaymentMethod.Cash);

            var own = await ana.ServiceProvider.GetRequiredService<GetCashShiftReportHandler>().HandleAsync(shiftId, default);
            Assert.Single(own.Payments);
        }

        using (var beto = At(w, w.BetoId, UserRole.Staff, Hours(9)))
        {
            await Assert.ThrowsAsync<ForbiddenDomainException>(
                () => beto.ServiceProvider.GetRequiredService<GetCashShiftReportHandler>().HandleAsync(shiftId, default));
            await Assert.ThrowsAsync<ForbiddenDomainException>(
                () => beto.ServiceProvider.GetRequiredService<ListCashShiftsHandler>()
                    .HandleAsync(new ListCashShiftsQuery(), default));

            beto.ServiceProvider.GetRequiredService<MutableCurrentUser>().Permissions.Add(StaffPermission.ViewFinances);
            var other = await beto.ServiceProvider.GetRequiredService<GetCashShiftReportHandler>().HandleAsync(shiftId, default);
            Assert.Single(other.Payments);
        }

        using var missing = At(w, w.OwnerId, UserRole.Owner, Hours(10));
        await Assert.ThrowsAsync<NotFoundException>(
            () => missing.ServiceProvider.GetRequiredService<GetCashShiftReportHandler>().HandleAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task The_owner_lists_shifts_filtered_by_person_and_date_with_their_differences()
    {
        var w = await SeedAsync();
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync(10_000m);
            await ana.CloseCashAsync(8_000m, "Faltan 2 mil");
        }

        using (var beto = At(w, w.BetoId, UserRole.Staff, Hours(14)))
        {
            await beto.OpenCashAsync(8_000m);
            await beto.CloseCashAsync(8_000m);
        }

        using var owner = At(w, w.OwnerId, UserRole.Owner, Hours(20));
        var list = owner.ServiceProvider.GetRequiredService<ListCashShiftsHandler>();

        var all = await list.HandleAsync(new ListCashShiftsQuery(), default);
        Assert.Equal(2, all.Total);

        var anas = await list.HandleAsync(new ListCashShiftsQuery { OpenedBy = w.AnaId }, default);
        var shift = Assert.Single(anas.Items);
        Assert.Equal(-2_000m, shift.Difference);

        var otherDay = await list.HandleAsync(new ListCashShiftsQuery { From = Day.AddDays(1) }, default);
        Assert.Equal(0, otherDay.Total);
    }

    // ---------------------------------------------------------------- cierre diario del Owner

    [Fact]
    public async Task The_daily_close_lists_shifts_movements_method_counts_and_reversals_of_the_business_day()
    {
        var w = await SeedAsync();
        using (var ana = At(w, w.AnaId, UserRole.Staff, Hours(8)))
        {
            await ana.OpenCashAsync(10_000m);
            await Record(ana, 100_000m, PaymentMethod.Cash);
            var nequi = await Record(ana, 120_000m, PaymentMethod.Nequi);
            await Record(ana, 60_000m, PaymentMethod.Nequi);
            await Move(ana, "withdrawal", 50_000m, "Banco");
            ana.ServiceProvider.GetRequiredService<MutableCurrentUser>().Permissions.Add(StaffPermission.VoidPayments);
            await Void(ana, nequi.Id, "Monto mal digitado", null);
            await ana.CloseCashAsync(60_000m);
        }

        // Un turno de la noche (20:00 hora local) sigue siendo del mismo día de negocio aunque en UTC ya sea el siguiente.
        using (var beto = At(w, w.BetoId, UserRole.Staff, Hours(20)))
        {
            await beto.OpenCashAsync(60_000m);
        }

        using var owner = At(w, w.OwnerId, UserRole.Owner, Hours(21));
        var close = await owner.ServiceProvider.GetRequiredService<GetDailyCashRegisterHandler>().HandleAsync(Day, default);

        Assert.Equal(2, close.Shifts.Count);
        Assert.Equal(60_000m, close.Shifts[0].CountedCash);
        Assert.Equal(0m, close.Shifts[0].Difference);
        Assert.True(close.Shifts[1].IsOpen);
        var movement = Assert.Single(close.Movements);
        Assert.Equal(50_000m, movement.Amount);

        var nequiSummary = close.Methods.Single(m => m.Method == "Nequi");
        Assert.Equal(3, nequiSummary.Count);               // 120k, 60k y el asiento inverso
        Assert.Equal(60_000m, nequiSummary.Total);
        var reversal = Assert.Single(close.Reversals);
        Assert.Equal("Monto mal digitado", reversal.Notes);
        Assert.All(close.Payments, p => Assert.Equal("Ana", p.RegisteredByName));

        // El día siguiente (por rango de negocio) no incluye nada de esto.
        var next = await owner.ServiceProvider.GetRequiredService<GetDailyCashRegisterHandler>().HandleAsync(Day.AddDays(1), default);
        Assert.Empty(next.Shifts);
        Assert.Empty(next.Payments);
    }

    // ---------------------------------------------------------------- ayudas

    /// <summary>Instante de negocio: horas desde la medianoche local de <see cref="Day"/>.</summary>
    private static DateTimeOffset Hours(int hours)
        => new SystemClockProbe().At(Day, hours);

    private IServiceScope At(World w, Guid userId, UserRole role, DateTimeOffset utc)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, w.GymId, role);
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        clock.PinTo(utc, DateOnly.FromDateTime(clock.ToBusinessLocal(utc)));
        return scope;
    }

    // ---------------------------------------------------------------- caja opcional (P2)

    [Fact]
    public async Task With_the_drawer_off_cash_is_recorded_without_a_shift_and_moves_no_drawer()
    {
        var w = await SeedAsync();
        using (var owner = At(w, w.OwnerId, UserRole.Owner, Hours(8)))
        {
            var saved = await owner.ServiceProvider.GetRequiredService<UpdatePaymentSettingsHandler>()
                .HandleAsync(new UpdatePaymentSettingsRequest(false), default);
            Assert.False(saved.UseCashDrawer);
        }

        using var ana = At(w, w.AnaId, UserRole.Staff, Hours(9));
        var payment = await Record(ana, 40_000m, PaymentMethod.Cash);

        Assert.Null(payment.ShiftId);
        Assert.False(payment.AffectsCashDrawer);

        // Anular un efectivo sin caja tampoco pide decidir la devolución ni mueve el cajón.
        var reversal = await Void(ana, payment.Id, "Error de cobro", null);
        Assert.False(reversal.AffectsCashDrawer);
    }

    [Fact]
    public async Task With_the_drawer_on_by_default_cash_still_needs_an_open_shift()
    {
        var w = await SeedAsync();
        using var ana = At(w, w.AnaId, UserRole.Staff, Hours(8));

        var settings = await ana.ServiceProvider.GetRequiredService<GetPaymentSettingsHandler>().HandleAsync(default);
        Assert.True(settings.UseCashDrawer);
        await Assert.ThrowsAsync<ConflictException>(() => Record(ana, 10_000m, PaymentMethod.Cash));
    }

    [Fact]
    public void The_personal_trainer_template_turns_the_drawer_off_and_the_rest_keep_it()
    {
        Assert.False(PaymentTemplates.For(GymType.PersonalTrainer).UseCashDrawer);
        foreach (var type in new[] { GymType.Gym, GymType.Box, GymType.Studio, GymType.Academy, GymType.Other })
        {
            Assert.True(PaymentTemplates.For(type).UseCashDrawer);
        }
    }

    private static Task<PaymentResponse> Record(
        IServiceScope scope, decimal amount, PaymentMethod method, string concept = "Cobro")
        => scope.ServiceProvider.GetRequiredService<RecordPaymentHandler>()
            .HandleAsync(new RecordPaymentRequest(amount, method, concept, null, null, null), default);

    private static Task<CashMovementResponse> Move(IServiceScope scope, string type, decimal amount, string reason)
        => scope.ServiceProvider.GetRequiredService<RecordCashMovementHandler>()
            .HandleAsync(new CashMovementRequest(type, amount, reason), default);

    private static Task<PaymentResponse> Void(IServiceScope scope, Guid paymentId, string reason, bool? refundNow)
        => scope.ServiceProvider.GetRequiredService<VoidPaymentHandler>()
            .HandleAsync(paymentId, new VoidPaymentRequest(reason, refundNow), default);

    private sealed record World(Guid GymId, Guid OwnerId, Guid AnaId, Guid BetoId);

    /// <summary>Convierte "hora local del día" a instante UTC con la misma regla del reloj de los tests.</summary>
    private sealed class SystemClockProbe
    {
        public DateTimeOffset At(DateOnly day, int hours)
            => new GSM.Infrastructure.Common.SystemClock(
                    Microsoft.Extensions.Options.Options.Create(new GSM.Infrastructure.Common.ClockOptions()))
                .BusinessDayRange(day).Start.AddHours(hours);
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships, payments.payments, payments.cash_movements, payments.cash_shifts, payments.void_requests restart identity cascade;");

        var gym = Gym.Create("Shift Gym", $"shift-{Guid.NewGuid():N}"[..20]);
        var owner = User("owner", "Owner Shift");
        var ana = User("ana", "Ana");
        var beto = User("beto", "Beto");
        gym.AssignOwner(owner.Id);

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.AddRange(owner, ana, beto);
        await db.SaveChangesAsync();

        return new World(gym.Id, owner.Id, ana.Id, beto.Id);
    }

    private static GSM.Infrastructure.Identity.AppUser User(string key, string fullName) => new()
    {
        UserName = $"{key}@shift.local",
        Email = $"{key}@shift.local",
        NormalizedUserName = $"{key}@shift.local".ToUpperInvariant(),
        NormalizedEmail = $"{key}@shift.local".ToUpperInvariant(),
        EmailConfirmed = true,
        FullName = fullName,
        IsActive = true,
        SecurityStamp = Guid.NewGuid().ToString(),
    };
}
