using GSM.Client;
using GSM.Client.Http;
using GSM.Member.UI;
using GSM.Mobile.Platform;
using GSM.Panel.UI.Navigation;
using System.Reflection;
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
        builder.Services.AddGsmPanel(); // AP3: el panel del gym (staff y Owner) también vive en la app
        builder.Services.AddSingleton<GSM.UI.Shared.Push.PushLinkNavigator>();
#if ANDROID
        builder.Services.AddScoped<GSM.UI.Shared.Push.IPushNotifications, AndroidPushNotifications>();
        builder.Services.AddSingleton<GSM.UI.Shared.IPrintService, AndroidPrintService>();
#endif
        builder.Services.AddSingleton<IQrScanner, ZxingQrScanner>();
        builder.Services.AddSingleton<GSM.UI.Shared.IClipboard, MauiClipboard>();
        builder.Services.AddSingleton<IDeviceLocation, MauiDeviceLocation>();
        builder.Services.AddSingleton<GSM.UI.Shared.Diagnostics.IClientEnvironment, MauiClientEnvironment>();
        builder.Services.AddGsmDeviceLock(); // AP5: bloqueo del modo staff
        MauiCrashReporter.Install(ApiBaseUrl);

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    // La URL compilada (-p:GsmApiBaseUrl=...) manda; sin ella, el backend local de desarrollo.
    private static string ApiBaseUrl => CompiledApiBaseUrl ?? DevelopmentApiBaseUrl;

    private static readonly string? CompiledApiBaseUrl = typeof(MauiProgram).Assembly
        .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "ApiBaseUrl")?.Value?.TrimEnd('/');

    private static string DevelopmentApiBaseUrl =>
#if ANDROID
        "http://10.0.2.2:5175";
#else
        "https://localhost:7219";
#endif
}
