namespace GSM.Client.Http;

/// <summary>
/// Avisos de la sesión hacia la interfaz. <see cref="Ended"/> se dispara cuando no se pudo renovar la sesión y los
/// tokens se borraron: la interfaz debe llevar al login con un mensaje, en vez de quedarse abierta sin sesión.
/// </summary>
public sealed class SessionEvents
{
    public event Action? Ended;

    internal void RaiseEnded() => Ended?.Invoke();
}
