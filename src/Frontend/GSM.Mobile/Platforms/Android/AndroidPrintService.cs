using Android.Content;
using Android.Print;
using Android.Views;
using Android.Webkit;
using GSM.UI.Shared;
using Microsoft.Maui.ApplicationModel;

namespace GSM.Mobile.Platform;

/// <summary>
/// «Imprimir» en la app Android (AP3): abre el visor de impresión del sistema con el contenido del
/// <c>BlazorWebView</c>, desde donde la persona elige «Guardar como PDF» y lo comparte. Hoy lo usa el reporte de
/// la caja (<c>ShiftReport</c>). <c>window.print()</c> no hace nada dentro de un WebView, de ahí esta clase.
/// <para><b>Sin verificar en dispositivo.</b></para>
/// </summary>
public sealed class AndroidPrintService : IPrintService
{
    public async Task<bool> PrintAsync(string title, CancellationToken cancellationToken = default)
    {
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                var webView = activity?.Window?.DecorView is { } root ? FindWebView(root) : null;
                if (activity is null || webView is null
                    || activity.GetSystemService(Context.PrintService) is not PrintManager manager)
                {
                    return false;
                }

                var job = string.IsNullOrWhiteSpace(title) ? "TrainingHub" : title;
                manager.Print(job, webView.CreatePrintDocumentAdapter(job), null);
                return true;
            });
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("GSM.Print", $"No se pudo abrir la impresión: {ex.Message}");
            return false;
        }
    }

    private static Android.Webkit.WebView? FindWebView(Android.Views.View view)
    {
        if (view is Android.Webkit.WebView webView)
        {
            return webView;
        }

        if (view is ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                if (group.GetChildAt(i) is { } child && FindWebView(child) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }
}
