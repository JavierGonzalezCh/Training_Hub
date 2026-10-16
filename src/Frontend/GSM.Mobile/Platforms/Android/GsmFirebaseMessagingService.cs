using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Firebase.Messaging;

namespace GSM.Mobile.Platform;

/// <summary>
/// Recibe los push de FCM (N3). Con la app cerrada o en segundo plano el sistema muestra solo el aviso (la API
/// manda <c>notification</c> para Android) y, al tocarlo, abre <see cref="MainActivity"/> con los datos del
/// mensaje como extras. Con la app en primer plano FCM no muestra nada: este servicio dibuja el aviso él mismo.
/// <para><b>Sin verificar en dispositivo.</b></para>
/// </summary>
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public sealed class GsmFirebaseMessagingService : FirebaseMessagingService
{
    public const string ChannelId = "gsm_avisos";

    public const string ExtraLink = "link";

    public const string ExtraGymId = "gymId";

    public override void OnMessageReceived(RemoteMessage message)
    {
        var data = message.Data;
        var title = Read(data, "title") ?? message.GetNotification()?.Title;
        var body = Read(data, "body") ?? message.GetNotification()?.Body;
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        EnsureChannel(this);

        var open = new Intent(this, typeof(MainActivity))
            .SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        if (Read(data, "link") is { Length: > 0 } link)
        {
            open.PutExtra(ExtraLink, link);
        }

        if (Read(data, "gymId") is { Length: > 0 } gymId)
        {
            open.PutExtra(ExtraGymId, gymId);
        }

        var pending = PendingIntent.GetActivity(
            this, Guid.NewGuid().GetHashCode(), open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(title ?? string.Empty)!
            .SetContentText(body ?? string.Empty)!
            .SetSmallIcon(ApplicationInfo?.Icon ?? Android.Resource.Drawable.IcDialogInfo)!
            .SetAutoCancel(true)!
            .SetPriority(NotificationCompat.PriorityHigh)!
            .SetContentIntent(pending)!
            .Build();

        // Android 13+ sin el permiso: NotificationManager descarta el aviso sin lanzar nada.
        NotificationManagerCompat.From(this)?.Notify(Guid.NewGuid().GetHashCode(), notification);
    }

    // El token se vuelve a registrar al abrir la app (PushRegistrationService.SyncAsync en el layout del panel):
    // aquí no hay sesión ni cliente HTTP, así que no se hace nada.
    public override void OnNewToken(string token) => base.OnNewToken(token);

    /// <summary>Crea el canal de avisos (Android 8+). Idempotente.</summary>
    internal static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        if (context.GetSystemService(NotificationService) is NotificationManager manager
            && manager.GetNotificationChannel(ChannelId) is null)
        {
            manager.CreateNotificationChannel(
                new NotificationChannel(ChannelId, "Avisos del gimnasio", NotificationImportance.High));
        }
    }

    private static string? Read(IDictionary<string, string>? data, string key)
        => data is not null && data.TryGetValue(key, out var value) ? value : null;
}
