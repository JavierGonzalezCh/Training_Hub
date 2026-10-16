using Android.App;
using Android.Runtime;

namespace GSM.Mobile;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	public override void OnCreate()
	{
		base.OnCreate();

		// Firebase se inicializa por código con la configuración guardada (sin google-services.json): hace falta
		// al arrancar el proceso aunque sea un push el que lo despierte (N3). Sin lo guardado, no hace nada.
		GSM.Mobile.Platform.AndroidFirebase.TryInitializeFromStored(this);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
