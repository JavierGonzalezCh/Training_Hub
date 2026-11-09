using GSM.Client.Generated;

namespace GSM.Panel.UI.Pages.Payments;

/// <summary>
/// Estado compartido de la caja (P2): lo usan la barra de la caja, los diálogos que cobran en efectivo y el chip de la barra
/// superior, y se actualizan juntos con <see cref="Changed"/>. <see cref="UseCashDrawer"/> es el ajuste del negocio; apagado,
/// no hay turnos ni barra.
/// </summary>
public sealed class CashDrawerState(ICashShiftsClient cashShifts, IPaymentSettingsClient settings)
{
    private bool _loaded;

    public bool UseCashDrawer { get; private set; } = true;

    public CashShiftResponse? Shift { get; private set; }

    public decimal SuggestedBase { get; private set; }

    public bool IsOpen => Shift is not null;

    public event Action? Changed;

    /// <summary>Carga el ajuste y el turno una sola vez por sesión; las pantallas llaman a <see cref="RefreshAsync"/> tras un cambio.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (!_loaded)
        {
            await RefreshAsync();
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            UseCashDrawer = (await settings.PaymentSettingsGETAsync()).UseCashDrawer;
            if (UseCashDrawer)
            {
                var status = await cashShifts.CurrentAsync();
                Shift = status?.Shift;
                SuggestedBase = (decimal)(status?.SuggestedOpeningCash ?? 0);
            }
            else
            {
                Shift = null;
            }

            _loaded = true;
        }
        catch (GsmApiException)
        {
            // Sin conexión o sin permiso: se conserva lo último conocido y el servidor decide al cobrar.
        }

        Changed?.Invoke();
    }

    /// <summary>Abre la caja con la base dada (el mismo endpoint que el diálogo de la barra) y avisa a todos.</summary>
    public async Task OpenAsync(decimal openingCash)
    {
        await cashShifts.OpenAsync(new OpenCashShiftRequest { OpeningCash = (double)openingCash });
        await RefreshAsync();
    }

    public void Reset()
    {
        _loaded = false;
        Shift = null;
        UseCashDrawer = true;
    }
}
