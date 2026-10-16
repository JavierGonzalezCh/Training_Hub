using GSM.Client;
using GSM.Client.Generated;
using GSM.UI.Shared.Auth;

namespace GSM.Panel.UI.Onboarding;

/// <summary>
/// Dice si el panel está bloqueado porque el gym no terminó su onboarding (Gyms §5). Lo consulta el layout del
/// panel; el asistente lo refresca al avanzar. Solo bloquea a Owner y Staff (un SuperAdmin impersonando no se
/// atora en el asistente: lo omite desde su ficha). <b>Falla abierto</b>: si la consulta falla, no se bloquea nada,
/// porque dejar a un gym sin panel por un error de red es peor que mostrárselo.
/// </summary>
public sealed class OnboardingGate(IGymClient gym, GymContext context)
{
    private MeResponse? _forMe;
    private OnboardingResponse? _last;

    /// <summary>El último estado leído para la sesión actual; <c>null</c> si no se pudo leer.</summary>
    public OnboardingResponse? Last => ReferenceEquals(_forMe, context.Me) ? _last : null;

    /// <summary>¿Esta sesión puede quedar bloqueada por el onboarding? Solo Owner y Staff.</summary>
    public bool Applies => context.Role is GsmRoles.Owner or GsmRoles.Staff;

    /// <summary>
    /// ¿Hay que llevar a esta sesión al asistente? Una vez que el gym está completo, no se vuelve a preguntar
    /// durante la sesión (el onboarding completo no se revierte).
    /// </summary>
    public async Task<bool> IsBlockedAsync(bool refresh = false)
    {
        if (!Applies)
        {
            return false;
        }

        if (!refresh && Last is { IsComplete: true })
        {
            return false;
        }

        var state = await RefreshAsync();
        return state is { IsComplete: false };
    }

    /// <summary>Vuelve a leer el estado. <c>null</c> si falla (el panel no se bloquea).</summary>
    public async Task<OnboardingResponse?> RefreshAsync()
    {
        try
        {
            _last = await gym.OnboardingAsync();
            _forMe = context.Me;
            return _last;
        }
        catch (Exception)
        {
            _last = null;
            return null;
        }
    }
}
