namespace GSM.Domain.SuperAdmin;

/// <summary>
/// Modo de una sesión de impersonación. <see cref="Read"/> solo admite <c>GET</c>; <see cref="Write"/>
/// admite todos los métodos y las escrituras quedan firmadas como "TrainingHub" en el historial del gym.
/// </summary>
public enum ImpersonationMode
{
    Read,
    Write,
}

public static class ImpersonationModeName
{
    public const string Read = "read";
    public const string Write = "write";

    public static string ToClaim(ImpersonationMode mode) => mode == ImpersonationMode.Write ? Write : Read;

    public static bool TryParse(string? value, out ImpersonationMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case Read:
                mode = ImpersonationMode.Read;
                return true;
            case Write:
                mode = ImpersonationMode.Write;
                return true;
            default:
                mode = ImpersonationMode.Read;
                return false;
        }
    }
}
