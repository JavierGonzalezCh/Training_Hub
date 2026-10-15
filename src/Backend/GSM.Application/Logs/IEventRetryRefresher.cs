using GSM.Application.Common.Events;

namespace GSM.Application.Logs;

/// <summary>
/// Cómo reintentar un evento que lleva datos que el log no guarda (códigos) o que pueden haber cambiado
/// (docs/GSM-logs.md §4.3). El reintento <b>no usa el payload</b> para esos datos: cada módulo dueño registra un
/// refrescador que vuelve a leer lo vigente y devuelve el evento listo para ejecutar el manejador, o
/// <c>null</c> si el dato ya no existe o ya no aplica. Un evento con campos limpiados y sin refrescador no se reintenta.
/// </summary>
public interface IEventRetryRefresher<TEvent>
    where TEvent : IDomainEvent
{
    Task<TEvent?> RefreshAsync(TEvent stored, CancellationToken cancellationToken);
}
