using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace GSM.Mobile;

// SingleTop: tocar un aviso con la app abierta reutiliza esta actividad (OnNewIntent) en vez de crear otra.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        GSM.Mobile.Platform.GsmFirebaseMessagingService.EnsureChannel(this);
        PublishPushLink(Intent);
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
