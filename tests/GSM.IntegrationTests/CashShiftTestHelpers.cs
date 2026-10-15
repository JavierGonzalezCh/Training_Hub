using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.IntegrationTests;

/// <summary>P3: cobrar en efectivo exige una caja abierta. Ayudas para los tests que registran efectivo.</summary>
internal static class CashShiftTestHelpers
{
    /// <summary>Abre la caja a nombre del usuario actual del <paramref name="scope"/> (con el reloj del scope).</summary>
    public static Task<CashShiftResponse> OpenCashAsync(this IServiceScope scope, decimal openingCash = 0m)
        => scope.ServiceProvider.GetRequiredService<OpenCashShiftHandler>()
            .HandleAsync(new OpenCashShiftRequest(openingCash), default);

    public static Task<CashShiftReportResponse> CloseCashAsync(
        this IServiceScope scope, decimal countedCash, string? notes = null)
        => scope.ServiceProvider.GetRequiredService<CloseCashShiftHandler>()
            .HandleAsync(new CloseCashShiftRequest(countedCash, notes), default);
}
