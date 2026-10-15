using System.Collections.Concurrent;

namespace GSM.Application.Attendance;

/// <summary>
/// Candado del teclado del kiosco (Attendance §7): 5 intentos fallidos seguidos de un mismo equipo lo
/// bloquean 1 minuto, para que nadie pruebe números al azar. Un acierto reinicia la cuenta. El estado vive
/// en memoria del proceso (singleton): un reinicio de la API lo limpia, que para un bloqueo de un minuto es
/// aceptable; si la API pasa a varias instancias habrá que llevarlo a Postgres.
/// </summary>
public sealed class DocumentEntryGuard
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, State> _states = new();

    /// <summary>Hasta cuándo está bloqueado el equipo, o <c>null</c> si puede intentar.</summary>
    public DateTimeOffset? LockedUntil(Guid deviceId, DateTimeOffset now)
    {
        if (_states.TryGetValue(deviceId, out var state))
        {
            lock (state)
            {
                if (state.LockedUntil is { } until && until > now)
                {
                    return until;
                }
            }
        }

        return null;
    }

    /// <summary>Anota un intento fallido. Devuelve <c>true</c> si este intento acaba de bloquear el teclado.</summary>
    public bool RegisterFailure(Guid deviceId, DateTimeOffset now)
    {
        var state = _states.GetOrAdd(deviceId, _ => new State());
        lock (state)
        {
            // Un bloqueo ya vencido arranca la cuenta de cero.
            if (state.LockedUntil is { } until && until <= now)
            {
                state.Failures = 0;
                state.LockedUntil = null;
            }

            state.Failures++;
            if (state.Failures >= MaxFailures)
            {
                state.Failures = 0;
                state.LockedUntil = now + LockDuration;
                return true;
            }

            return false;
        }
    }

    public void RegisterSuccess(Guid deviceId)
    {
        if (_states.TryGetValue(deviceId, out var state))
        {
            lock (state)
            {
                state.Failures = 0;
                state.LockedUntil = null;
            }
        }
    }

    private sealed class State
    {
        public int Failures { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }
    }
}
