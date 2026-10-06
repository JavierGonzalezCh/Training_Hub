using GSM.Member.UI;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace GSM.Mobile.Platform;

/// <summary>
/// Escáner de QR de la app nativa: abre una página modal con la cámara (ZXing.Net.Maui) y devuelve el
/// primer código leído. Hace falta porque, sin App Links verificados, el enlace que lee la cámara del
/// sistema abriría el navegador y no esta app.
/// </summary>
public sealed class ZxingQrScanner : IQrScanner
{
    public bool IsSupported => true;

    public async Task<string?> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureCameraPermissionAsync())
        {
            return null;
        }

        var navigation = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (navigation is null)
        {
            return null;
        }

        var page = new QrScanPage();
        await MainThread.InvokeOnMainThreadAsync(() => navigation.PushModalAsync(page));
        using var registration = cancellationToken.Register(page.Cancel);

        var text = await page.Result;

        if (navigation.ModalStack.Contains(page))
        {
            await MainThread.InvokeOnMainThreadAsync(() => navigation.PopModalAsync());
        }

        return text;
    }

    private static async Task<bool> EnsureCameraPermissionAsync()
    {
        while (true)
        {
            var status = await MainThread.InvokeOnMainThreadAsync(Permissions.CheckStatusAsync<Permissions.Camera>);
            if (status != PermissionStatus.Granted)
            {
                status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.Camera>);
            }

            if (status == PermissionStatus.Granted)
            {
                return true;
            }

            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null)
            {
                return false;
            }

            // Permiso negado: se explica y se deja reintentar (desde los ajustes del sistema si el SO ya no vuelve a preguntar).
            var retry = await MainThread.InvokeOnMainThreadAsync(() => page.DisplayAlert(
                "Cámara necesaria",
                "Para escanear el QR del gimnasio la app necesita usar la cámara. Concede el permiso y vuelve a intentarlo.",
                "Reintentar",
                "Cancelar"));
            if (!retry)
            {
                return false;
            }
        }
    }

    private sealed class QrScanPage : ContentPage
    {
        private readonly TaskCompletionSource<string?> _result = new();

        public QrScanPage()
        {
            Title = "Escanear QR";

            var reader = new CameraBarcodeReaderView
            {
                Options = new BarcodeReaderOptions
                {
                    Formats = BarcodeFormats.TwoDimensional,
                    AutoRotate = true,
                    Multiple = false,
                },
                IsDetecting = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
            };

            // La detección llega en un hilo de fondo: solo se completa la tarea; el cierre lo hace ScanAsync.
            reader.BarcodesDetected += (_, e) =>
            {
                var value = e.Results?.FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    reader.IsDetecting = false;
                    _result.TrySetResult(value);
                }
            };

            var cancel = new Button { Text = "Cancelar", Margin = new Thickness(16) };
            cancel.Clicked += (_, _) => Cancel();

            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                },
                Children =
                {
                    reader,
                    cancel,
                },
            };
            Grid.SetRow(reader, 0);
            Grid.SetRow(cancel, 1);

            // Si el sistema cierra la página (botón atrás), se cuenta como cancelación.
            Disappearing += (_, _) => Cancel();
        }

        public Task<string?> Result => _result.Task;

        public void Cancel() => _result.TrySetResult(null);
    }
}
