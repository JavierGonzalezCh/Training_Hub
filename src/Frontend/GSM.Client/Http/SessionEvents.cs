namespace GSM.Client.Http;

/// <summary>
/// Avisos de la sesión hacia la interfaz. <see cref="Ended"/> se dispara cuando el servidor rechazó la renovación y los
/// tokens se borraron: la interfaz lleva al login con un mensaje, en vez de quedarse abierta sin sesión.
/// <para>
/// Una pantalla que no debe interrumpirse (el modo sesión del entrenamiento) la retiene con <see cref="Hold"/>: si la
/// sesión termina mientras tanto se avisa por <see cref="EndedWhileHeld"/> y <see cref="Ended"/> espera a que se suelte.
/// </para>
/// </summary>
public sealed class SessionEvents
{
    private int _holds;

    public event Action? Ended;

    /// <summary>La sesión terminó mientras una pantalla la retenía: se avisa sin sacar a la persona de ahí.</summary>
    public event Action? EndedWhileHeld;

    /// <summary>La sesión terminó y el aviso a <see cref="Ended"/> espera a que se suelte la retención.</summary>
    public bool EndPending { get; private set; }

    /// <summary>Retiene el cierre hasta que se libere el objeto devuelto (al salir del modo sesión o de la pantalla).</summary>
    public IDisposable Hold()
    {
        _holds++;
        return new Release(this);
    }

    internal void RaiseEnded()
    {
        if (_holds > 0)
        {
            EndPending = true;
            EndedWhileHeld?.Invoke();
            return;
        }

        Ended?.Invoke();
    }

    private void Unhold()
    {
        _holds = Math.Max(0, _holds - 1);
        if (_holds == 0 && EndPending)
        {
            EndPending = false;
            Ended?.Invoke();
        }
    }

    private sealed class Release(SessionEvents owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
            {
                return;
            }

            _done = true;
            owner.Unhold();
        }
    }
}
