using GSM.UI.Shared.Diagnostics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace GSM.Mobile.Platform;

/// <summary>
/// La app nativa como origen de los reportes de error. Solo Android e iOS son plataformas de la spec
/// (<c>docs/GSM-logs.md</c> §4.6): en Windows no se reporta.
/// </summary>
public sealed class MauiClientEnvironment : IClientEnvironment
{
    public string? AppVersion => AppInfo.Current.VersionString;

    public string? ResolvePlatform(string route) => PlatformName();

    public Task<string?> GetDeviceInfoAsync()
        => Task.FromResult<string?>(DescribeDevice());

    internal static string? PlatformName()
        => DeviceInfo.Current.Platform == DevicePlatform.Android ? ClientPlatformNames.Android
            : DeviceInfo.Current.Platform == DevicePlatform.iOS ? ClientPlatformNames.Ios
            : null;

    internal static string DescribeDevice()
        => $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model} · {DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}";
}
