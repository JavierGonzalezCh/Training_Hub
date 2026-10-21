using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace GSM.Mobile;

// SingleTop: tocar un aviso con la app abierta reutiliza esta actividad (OnNewIntent) en vez de crear otra.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        FitInsideSystemBars();
        GSM.Mobile.Platform.GsmFirebaseMessagingService.EnsureChannel(this);
        PublishPushLink(Intent);
    }

    // Desde Android 15 la app se dibuja bajo la barra de estado y la de gestos, y el WebView no
    // informa de esos márgenes a la página. Se aplican como padding a la raíz para que el panel
    // quede entre ambas, y la barra de estado usa iconos claros sobre el fondo oscuro.
    private void FitInsideSystemBars()
    {
        var background = Android.Graphics.Color.ParseColor("#0F0E0D");
        Window?.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(background));

        var root = FindViewById(Android.Resource.Id.Content);
        if (root is not null)
        {
            ViewCompat.SetOnApplyWindowInsetsListener(root, new SystemBarsInsetsListener());
        }

        if (Window?.DecorView is { } decor)
        {
            new WindowInsetsControllerCompat(Window, decor).AppearanceLightStatusBars = false;
            new WindowInsetsControllerCompat(Window, decor).AppearanceLightNavigationBars = false;
        }
    }

    private sealed class SystemBarsInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(Android.Views.View? v, WindowInsetsCompat? insets)
        {
            var bars = insets!.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
            v!.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            return WindowInsetsCompat.Consumed;
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        PublishPushLink(intent);
    }

    // Un aviso tocado trae «link» y «gymId» como extras (los pone FCM o GsmFirebaseMessagingService).
    private static void PublishPushLink(Intent? intent)
    {
        var link = intent?.GetStringExtra(GSM.Mobile.Platform.GsmFirebaseMessagingService.ExtraLink);
        if (string.IsNullOrEmpty(link))
        {
            return;
        }

        var gymId = intent?.GetStringExtra(GSM.Mobile.Platform.GsmFirebaseMessagingService.ExtraGymId);
        IPlatformApplication.Current?.Services.GetService<GSM.UI.Shared.Push.PushLinkNavigator>()?.Publish(link, gymId);

        // Evita reabrir el mismo aviso si la actividad se recrea (rotación, proceso reiniciado).
        intent?.RemoveExtra(GSM.Mobile.Platform.GsmFirebaseMessagingService.ExtraLink);
    }
}
