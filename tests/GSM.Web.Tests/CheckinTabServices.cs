using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.UI.Shared.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using FakeClient = GSM.Web.Tests.PaymentPermissionsUiTests.FakeClient;

namespace GSM.Web.Tests;

/// <summary>Lo que pide la pestaña de Entrada para guardar el QR del gym: el cliente del gym y la sesión.</summary>
internal static class CheckinTabServices
{
    /// <summary>Registra el cliente del gym (con sus flags) y <see cref="SessionService"/>; devuelve el doble del gym.</summary>
    public static FakeClient AddFor(TestContext context, bool checkinQr)
    {
        var gym = DispatchProxy.Create<IGymClient, FakeClient>();
        var fake = (FakeClient)(object)gym;
        fake.Handlers["FeatureFlagsGETAsync"] = _ => new GymFeatureFlagsResponse
        {
            ShowRoutines = true, ShowClasses = true, ShowProgress = true, ShowGamification = true, ShowCommunity = true,
            CheckinQr = checkinQr, ShowRanking = true, ShowFrontDesk = true,
        };
        fake.Handlers["FeatureFlagsPUTAsync"] = _ => new GymFeatureFlagsResponse();
        context.Services.AddSingleton(gym);

        context.Services.AddSingleton(DispatchProxy.Create<IAuthClient, FakeClient>());
        context.Services.AddSingleton<ITokenStore>(new InMemoryTokenStore());
        context.Services.AddSingleton<GsmAuthenticationStateProvider>();
        context.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<GsmAuthenticationStateProvider>());
        context.Services.AddSingleton<SessionService>();
        return fake;
    }
}
