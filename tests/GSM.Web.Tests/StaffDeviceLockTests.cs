using Bunit;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using GSM.UI.Shared.Push;
using GSM.UI.Shared.Security;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>AP5: bloqueo con huella o PIN en modo staff. El código nativo (Android) no se prueba aquí.</summary>
public class StaffLockStateTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Staff_returning_after_15_minutes_is_locked()
    {
        var time = new ManualTime();
        var state = new StaffLockState(time);
        state.Backgrounded();
        time.Now += TimeSpan.FromMinutes(15);
        state.Foregrounded(isStaffMode: true);
        Assert.True(state.IsLocked);
    }

    [Fact]
    public void Staff_returning_before_15_minutes_is_not_locked()
    {
        var time = new ManualTime();
        var state = new StaffLockState(time);
        state.Backgrounded();
        time.Now += TimeSpan.FromMinutes(14);
        state.Foregrounded(isStaffMode: true);
        Assert.False(state.IsLocked);
    }

    [Fact]
    public void Member_mode_never_locks()
    {
        var time = new ManualTime();
        var state = new StaffLockState(time);
        state.Backgrounded();
        time.Now += TimeSpan.FromHours(5);
        state.Foregrounded(isStaffMode: false);
        Assert.False(state.IsLocked);
    }

    [Fact]
    public void Cold_start_in_staff_mode_locks_once()
    {
        var state = new StaffLockState(new ManualTime());
        state.CheckInitial(true);
        Assert.True(state.IsLocked);
        state.Unlock();
        state.CheckInitial(true);
        Assert.False(state.IsLocked);
    }

    [Fact]
    public void Foreground_without_prior_background_does_not_lock()
    {
        var state = new StaffLockState(new ManualTime());
        state.Foregrounded(true);
        Assert.False(state.IsLocked);
    }
}

public class DevicePinServiceTests
{
    [Theory]
    [InlineData("1234", true)]
    [InlineData("123456", true)]
    [InlineData("123", false)]
    [InlineData("1234567", false)]
    [InlineData("12a4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Format_is_4_to_6_digits(string? pin, bool valid) => Assert.Equal(valid, DevicePinService.IsValidFormat(pin));

    [Fact]
    public async Task Pin_is_never_stored_in_clear_and_verifies()
    {
        var store = new InMemorySecureValueStore();
        var service = new DevicePinService(store);
        await service.SetPinAsync("4821");

        var stored = await store.GetAsync("gsm.lock.pin");
        Assert.NotNull(stored);
        Assert.DoesNotContain("4821", stored);
        Assert.Equal(PinCheckResult.Ok, await service.VerifyAsync("4821"));
        Assert.Equal(PinCheckResult.Wrong, await service.VerifyAsync("0000"));
    }

    [Fact]
    public async Task Same_pin_gets_a_different_salt_each_time()
    {
        var store = new InMemorySecureValueStore();
        var service = new DevicePinService(store);
        await service.SetPinAsync("4821");
        var first = await store.GetAsync("gsm.lock.pin");
        await service.SetPinAsync("4821");
        Assert.NotEqual(first, await store.GetAsync("gsm.lock.pin"));
    }

    [Fact]
    public async Task Five_wrong_attempts_ask_for_a_new_login()
    {
        var service = new DevicePinService(new InMemorySecureValueStore());
        await service.SetPinAsync("4821");
        for (var i = 0; i < DevicePinService.MaxAttempts - 1; i++)
        {
            Assert.Equal(PinCheckResult.Wrong, await service.VerifyAsync("0000"));
        }

        Assert.Equal(PinCheckResult.TooManyAttempts, await service.VerifyAsync("0000"));
    }

    [Fact]
    public async Task Without_a_stored_pin_nothing_verifies()
    {
        var service = new DevicePinService(new InMemorySecureValueStore());
        Assert.False(await service.HasPinAsync());
        Assert.NotEqual(PinCheckResult.Ok, await service.VerifyAsync("1234"));
    }

    [Fact]
    public async Task Invalid_pin_is_rejected_on_set()
    {
        var service = new DevicePinService(new InMemorySecureValueStore());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetPinAsync("12"));
    }
}

public class DeviceLockGateTests : TestContext
{
    private sealed class FakeDeviceLock(bool supported, bool deviceAuth, bool authResult) : IDeviceLock
    {
        public bool IsSupported => supported;

        public Task<bool> IsDeviceAuthAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(deviceAuth);

        public Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken = default) => Task.FromResult(authResult);
    }

    private sealed class FakeLifecycle : IAppLifecycle
    {
        public event Action? Backgrounded;

        public event Action? Foregrounded;

        public void Attach()
        {
        }

        public void Raise(bool background)
        {
            if (background)
            {
                Backgrounded?.Invoke();
            }
            else
            {
                Foregrounded?.Invoke();
            }
        }
    }

    private (IRenderedComponent<DeviceLockGate> Cut, StaffLockState State, FakeLifecycle Lifecycle) Render(
        string role, FakeDeviceLock device)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var gym = new GymContext();
        gym.Set(new MeResponse { Role = role }, impersonating: false);
        Services.AddSingleton(gym);
        var state = new StaffLockState(TimeProvider.System);
        var lifecycle = new FakeLifecycle();
        Services.AddSingleton(state);
        Services.AddSingleton<IDeviceLock>(device);
        Services.AddSingleton<IAppLifecycle>(lifecycle);
        Services.AddSingleton(new DevicePinService(new InMemorySecureValueStore()));

        var tokens = Fake<ITokenStore>().Client;
        Services.AddSingleton(tokens);
        var authState = new GsmAuthenticationStateProvider(tokens);
        Services.AddSingleton(authState);
        Services.AddSingleton(new SessionService(Fake<IAuthClient>().Client, tokens, authState, gym));
        Services.AddSingleton(new PushRegistrationService(new UnsupportedPushNotifications(), Fake<IPushClient>().Client));

        return (RenderComponent<DeviceLockGate>(), state, lifecycle);
    }

    [Fact]
    public void Web_host_never_shows_the_lock()
    {
        var (cut, state, _) = Render("staff", new FakeDeviceLock(supported: false, deviceAuth: false, authResult: false));
        Assert.False(state.IsLocked);
        Assert.Empty(cut.FindAll("[data-testid=device-lock]"));
    }

    [Fact]
    public void Member_mode_is_not_locked()
    {
        var (cut, state, _) = Render("member", new FakeDeviceLock(true, false, false));
        Assert.False(state.IsLocked);
        Assert.Empty(cut.FindAll("[data-testid=device-lock]"));
    }

    [Fact]
    public void Staff_on_a_phone_without_lock_is_asked_to_create_a_pin()
    {
        var (cut, state, _) = Render("staff", new FakeDeviceLock(true, deviceAuth: false, authResult: false));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=lock-new-pin]")));
        Assert.True(state.IsLocked);
    }

    [Fact]
    public void Successful_device_auth_unlocks()
    {
        var (cut, state, _) = Render("owner", new FakeDeviceLock(true, deviceAuth: true, authResult: true));
        cut.WaitForAssertion(() => Assert.False(state.IsLocked));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=device-lock]")));
    }

    [Fact]
    public void Failed_device_auth_keeps_the_lock_and_offers_retry()
    {
        var (cut, state, _) = Render("staff", new FakeDeviceLock(true, deviceAuth: true, authResult: false));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=lock-biometric]")));
        Assert.True(state.IsLocked);
    }
}
