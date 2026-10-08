using MudBlazor;
using MudBlazor.Services;

namespace GSM.UI.Shared.Components;

/// <summary>
/// Dice si la pantalla es de celular (por debajo de 960 px, el <c>md</c> de MudBlazor). Es el único
/// punto donde el panel decide entre la versión de celular y la de escritorio; los componentes
/// adaptables lo consultan en vez de medir cada uno por su cuenta. Un componente lo arranca con
/// <see cref="EnsureStartedAsync"/> y se redibuja cuando <see cref="Changed"/> se dispara.
/// </summary>
public sealed class ViewportState : IBrowserViewportObserver, IAsyncDisposable
{
    private readonly IBrowserViewportService _viewport;
    private bool _started;

    public ViewportState(IBrowserViewportService viewport) => _viewport = viewport;

    public Guid Id { get; } = Guid.NewGuid();

    public ResizeOptions? ResizeOptions => null;

    /// <summary>Verdadero en celular: menor que <see cref="Breakpoint.Md"/> (960 px).</summary>
    public bool IsNarrow { get; private set; }

    public event Action? Changed;

    /// <summary>Fija el estado a mano (pruebas y hosts que ya conocen el ancho).</summary>
    public void Set(bool narrow)
    {
        if (IsNarrow == narrow)
        {
            return;
        }

        IsNarrow = narrow;
        Changed?.Invoke();
    }

    /// <summary>Fija el ancho y no escucha el navegador (pruebas, o un host que ya lo conoce).</summary>
    public void Pin(bool narrow)
    {
        _started = true;
        Set(narrow);
    }

    public async Task EnsureStartedAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        await _viewport.SubscribeAsync(this, fireImmediately: true);
    }

    public Task NotifyBrowserViewportChangeAsync(BrowserViewportEventArgs e)
    {
        Set(e.Breakpoint is Breakpoint.Xs or Breakpoint.Sm);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            _started = false;
            await _viewport.UnsubscribeAsync(this);
        }
    }
}
