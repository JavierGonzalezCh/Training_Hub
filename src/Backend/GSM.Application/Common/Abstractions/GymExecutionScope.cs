namespace GSM.Application.Common.Abstractions;

/// <summary>
/// Ámbito ambiental que hace que el código corra "como el sistema dentro de un gym": mientras está abierto,
/// <see cref="ICurrentUserService"/> informa ese <c>GymId</c>, sin usuario, sin rol y sin superadmin, así que el
/// Global Query Filter vuelve a aplicar. Lo usa el reintento de eventos fallidos (GSM-logs.md §4.3, LG4-4), que
/// lo pide un SuperAdmin pero debe ejecutar el manejador en el gym del evento, igual que cuando se publicó.
/// Las implementaciones de <see cref="ICurrentUserService"/> deben consultarlo antes de leer su propia identidad.
/// </summary>
public static class GymExecutionScope
{
    private static readonly AsyncLocal<Guid?> CurrentGym = new();

    public static Guid? Current => CurrentGym.Value;

    public static IDisposable Enter(Guid gymId)
    {
        var previous = CurrentGym.Value;
        CurrentGym.Value = gymId;
        return new Exit(previous);
    }

    private sealed class Exit(Guid? previous) : IDisposable
    {
        public void Dispose() => CurrentGym.Value = previous;
    }
}
