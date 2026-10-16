using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.UI.Shared.Push;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Web.Tests;

/// <summary>
/// AP3 + N3: lo compartido de la app nativa. El push de Android y el panel dentro de la app solo se verifican en
/// un dispositivo; aquí se prueba lo que no depende de él (plataforma con la que se registra el dispositivo y
/// apertura del enlace de un aviso tocado).
/// </summary>
public class NativePushTests : TestContext
{
    private sealed class NativeDevice : IPushNotifications
    {
        public string? PlatformOverride => PushPlatforms.Android;

        public Task<PushPermissionState> GetStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(PushPermissionState.Granted);

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<string?> RequestTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default) => Task.FromResult<string?>("fcm-native");

        public Task<string?> GetTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default) => Task.FromResult<string?>("fcm-native");

        public Task ForgetTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> IsPromptDismissedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task DismissPromptAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class WebDevice : IPushNotifications
    {
        public Task<PushPermissionState> GetStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(PushPermissionState.Granted);

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<string?> RequestTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default) => Task.FromResult<string?>("web-token");

        public Task<string?> GetTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default) => Task.FromResult<string?>("web-token");

        public Task ForgetTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> IsPromptDismissedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task DismissPromptAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingPushClient : IPushClient
    {
        public List<RegisterPushDeviceRequest> Registered { get; } = [];

        public Task<PushConfigResponse> GetPushConfigAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PushConfigResponse
            {
                Enabled = true, ProjectId = "gsm", ApiKey = "k", AppId = "a", MessagingSenderId = "1", VapidKey = "v",
            });

        public Task<PushDeviceResponse> RegisterPushDeviceAsync(RegisterPushDeviceRequest body, CancellationToken cancellationToken = default)
        {
            Registered.Add(body);
            return Task.FromResult(new PushDeviceResponse { Id = Guid.NewGuid(), Platform = body.Platform });
        }

        public Task UnregisterPushDeviceAsync(UnregisterPushDeviceRequest body, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static (PushRegistrationService Service, RecordingPushClient Client) Build(IPushNotifications device)
    {
        var client = new RecordingPushClient();
        return (new PushRegistrationService(device, client), client);
    }

    [Fact]
    public async Task The_native_app_registers_as_android_whatever_screen_asks()
    {
        var (service, client) = Build(new NativeDevice());

        Assert.Equal(PushEnableResult.Enabled, await service.EnableAsync(PushPlatforms.WebPanel));
        await service.SyncAsync(PushPlatforms.WebMember);

        Assert.All(client.Registered, r => Assert.Equal("android", r.Platform));
        Assert.Equal(2, client.Registered.Count);
    }

    [Fact]
    public async Task The_web_keeps_registering_with_the_platform_each_screen_passes()
    {
        var (service, client) = Build(new WebDevice());

        await service.EnableAsync(PushPlatforms.WebPanel);
        await service.SyncAsync(PushPlatforms.WebMember);

        Assert.Equal(["web_panel", "web_member"], client.Registered.Select(r => r.Platform));
    }

    [Theory]
    [InlineData("/attendance", "/attendance")]
    [InlineData("/app/novedades?x=1", "/app/novedades?x=1")]
    [InlineData("https://evil.example/phish", null)]
    [InlineData("//evil.example", null)]
    [InlineData("/\\evil.example", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_internal_routes_are_accepted_from_a_notification(string? link, string? expected)
        => Assert.Equal(expected, PushLinkNavigator.Sanitize(link));

    [Fact]
    public void A_tapped_notification_opens_its_screen_even_if_it_arrived_before_the_router()
    {
        var links = new PushLinkNavigator();
        links.Publish("/equipment", Guid.NewGuid().ToString()); // el aviso llegó con la app arrancando
        Services.AddSingleton(links);
        var nav = Services.GetRequiredService<FakeNavigationManager>();

        RenderComponent<PushLinkListener>();

        Assert.EndsWith("/equipment", nav.Uri);
        Assert.Null(links.TryTake());
    }

    [Fact]
    public void A_notification_tapped_with_the_app_open_navigates_and_a_hostile_link_does_not()
    {
        var links = new PushLinkNavigator();
        Services.AddSingleton(links);
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        RenderComponent<PushLinkListener>();
        var start = nav.Uri;

        links.Publish("https://evil.example/", null);
        Assert.Equal(start, nav.Uri);

        links.Publish("/classes", null);
        Assert.EndsWith("/classes", nav.Uri);
    }
}
