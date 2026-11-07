namespace GSM.Application.Routines.Abstractions;

/// <summary>
/// Puerto invertido (lo declara Routines, lo implementa Attendance, dueño de <c>RemoteTraining</c>): ¿los miembros del
/// gym entrenan fuera de su sede? Con entrenamiento a distancia, el inventario de equipos no dice qué hay donde entrena
/// el miembro, así que no decide la disponibilidad de los ejercicios. Routines no referencia Attendance (que ya escucha
/// eventos de Routines): este puerto evita el ciclo.
/// </summary>
public interface IRemoteTrainingPolicy
{
    Task<bool> IsRemoteTrainingAsync(Guid gymId, CancellationToken cancellationToken = default);
}
