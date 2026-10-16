using Android.Content;
using Firebase;
using Microsoft.Maui.Storage;

namespace GSM.Mobile.Platform;

/// <summary>
/// Inicialización de Firebase en Android SIN <c>google-services.json</c> (N3). La configuración del proyecto de
/// Firebase de cada ambiente no se compila en la app: llega de la API (<c>GET /api/push/config</c>, la misma que
/// usa la web) y se guarda en <see cref="Preferences"/> para poder inicializar Firebase al arrancar el proceso,
/// incluso cuando lo despierta un push con la app cerrada (<see cref="MainApplication"/> llama a
/// <see cref="TryInitializeFromStored"/>).
/// <para>
/// <b>Sin verificar en dispositivo.</b> Si el integrador prefiere el camino estándar, basta añadir
/// <c>google-services.json</c> como <c>GoogleServicesJson</c>: con una app por defecto ya inicializada, esta clase
/// no hace nada.
/// </para>
/// </summary>
internal static class AndroidFirebase
{
    private const string KeyProject = "gsm.fcm.project_id";
    private const string KeyApiKey = "gsm.fcm.api_key";
    private const string KeyAppId = "gsm.fcm.app_id";
    private const string KeySender = "gsm.fcm.sender_id";

    private static readonly object Gate = new();

    public static bool IsInitialized(Context context) => FirebaseApp.GetApps(context).Count > 0;

    /// <summary>Inicializa Firebase con lo guardado de una sesión anterior; no hace nada si no hay nada guardado.</summary>
    public static void TryInitializeFromStored(Context context)
    {
        try
        {
            var projectId = Preferences.Default.Get(KeyProject, string.Empty);
            var apiKey = Preferences.Default.Get(KeyApiKey, string.Empty);
            var appId = Preferences.Default.Get(KeyAppId, string.Empty);
            var sender = Preferences.Default.Get(KeySender, string.Empty);
            if (HasAll(projectId, apiKey, appId, sender))
            {
                Initialize(context, projectId, apiKey, appId, sender);
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("GSM.Push", $"No se pudo inicializar Firebase desde lo guardado: {ex.Message}");
        }
    }

    /// <summary>
    /// Inicializa Firebase con la configuración que entrega la API y la guarda para los próximos arranques.
    /// Lanza <see cref="InvalidOperationException"/> con el detalle de lo que falta si el servidor no trae la
    /// configuración completa (<c>Notifications:Fcm:*</c> sin llenar).
    /// </summary>
    public static void EnsureInitialized(Context context, string? projectId, string? apiKey, string? appId, string? senderId)
    {
        if (!HasAll(projectId, apiKey, appId, senderId))
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(projectId)) missing.Add("projectId");
            if (string.IsNullOrWhiteSpace(apiKey)) missing.Add("apiKey");
            if (string.IsNullOrWhiteSpace(appId)) missing.Add("appId");
            if (string.IsNullOrWhiteSpace(senderId)) missing.Add("messagingSenderId");
            throw new InvalidOperationException(
                "Firebase no está configurado para la app Android: GET /api/push/config no trae " +
                $"{string.Join(", ", missing)}. Configura un proyecto de Firebase con una app Android " +
                "(com.gsm.mobile) y llena Notifications:Fcm:* en la API.");
        }

        Initialize(context, projectId!, apiKey!, appId!, senderId!);
        Preferences.Default.Set(KeyProject, projectId!);
        Preferences.Default.Set(KeyApiKey, apiKey!);
        Preferences.Default.Set(KeyAppId, appId!);
        Preferences.Default.Set(KeySender, senderId!);
    }

    private static void Initialize(Context context, string projectId, string apiKey, string appId, string senderId)
    {
        lock (Gate)
        {
            if (IsInitialized(context))
            {
                return;
            }

            var options = new FirebaseOptions.Builder()
                .SetProjectId(projectId)
                .SetApiKey(apiKey)
                .SetApplicationId(appId)
                .SetGcmSenderId(senderId)
                .Build();
            FirebaseApp.InitializeApp(context, options);
        }
    }

    private static bool HasAll(string? projectId, string? apiKey, string? appId, string? senderId)
        => !string.IsNullOrWhiteSpace(projectId) && !string.IsNullOrWhiteSpace(apiKey)
           && !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(senderId);
}
