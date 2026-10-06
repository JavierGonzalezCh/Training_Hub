using GSM.Client;
using GSM.Client.Http;
using GSM.Member.UI;
using GSM.Mobile.Platform;
using GSM.UI.Shared;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using ZXing.Net.Maui.Controls;

namespace GSM.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

        // El emulador de Android no ve "localhost" como el equipo anfitrión: 10.0.2.2 es el alias
        // fijo que Android reserva para eso. Windows sí puede usar localhost directamente.
        builder.Services.AddScoped<ITokenStore, SecureStorageTokenStore>();
        builder.Services.AddGsmClient(ApiBaseUrl);
        builder.Services.AddGsmUiShared();
        builder.Services.AddGsmMemberUi();
        builder.Services.AddSingleton<IQrScanner, ZxingQrScanner>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static string ApiBaseUrl =>
#if ANDROID
        "http://10.0.2.2:5175";
#else
        "https://localhost:7219";
#endif
}
