namespace GSM.UI.Shared.Push;

/// <summary>
/// Puente entre el aviso push que el usuario toca (llega por el <c>Intent</c> de Android) y el router de Blazor.
/// La plataforma llama a <see cref="Publish"/>; <c>PushLinkListener</c> lo consume y navega. Si la app aún no
/// montó Blazor, el enlace queda pendiente hasta que alguien lo tome. Es multiplataforma y no depende de
/// Android: el aviso solo trae una ruta interna (<c>PushMessage.Link</c>) y el id de su gym.
/// </summary>
public sealed class PushLinkNavigator
{
    private readonly object _gate = new();
    private PushLink? _pending;

    /// <summary>Se dispara cuando llega un enlace con la app ya abierta.</summary>
    public event Action? Arrived;

    public void Publish(string? link, string? gymId)
    {
        var safe = Sanitize(link);
        if (safe is null)
        {
            return;
        }

        lock (_gate)
        {
            _pending = new PushLink(safe, Guid.TryParse(gymId, out var id) ? id : null);
        }

        Arrived?.Invoke();
    }

    /// <summary>Toma (y borra) el enlace pendiente, si hay.</summary>
    public PushLink? TryTake()
    {
        lock (_gate)
        {
            var link = _pending;
            _pending = null;
            return link;
        }
    }

    /// <summary>Solo rutas internas de la app: nunca una URL absoluta ni <c>//host</c> (un aviso no manda al usuario fuera).</summary>
    public static string? Sanitize(string? link)
        => string.IsNullOrWhiteSpace(link) || link[0] != '/' || link.StartsWith("//", StringComparison.Ordinal) || link.Contains('\\')
            ? null
            : link;
}

/// <param name="Path">Ruta interna a abrir.</param>
/// <param name="GymId">Gym al que pertenece el aviso; hoy solo se informa (cambiar de gym queda como pregunta abierta de N3).</param>
public sealed record PushLink(string Path, Guid? GymId);
