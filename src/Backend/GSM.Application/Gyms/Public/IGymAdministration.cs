using GSM.Domain.Gyms;

namespace GSM.Application.Gyms.Public;

/// <summary>
/// Escritura acotada que el módulo Gyms expone a SuperAdmin: fijar el estado comercial manual
/// del gym (suspender / reactivar).
/// </summary>
/// <remarks>
/// Tercera excepción a "ningún módulo escribe en otro" (ver <c>CLAUDE.md</c>). Se justifica porque
/// el estado resultante <em>es</em> la respuesta de la operación de SuperAdmin, no una reacción:
/// un evento dejaría al superadmin sin saber si el gym quedó realmente suspendido. Se ejecuta
/// dentro de la misma transacción del caso de uso que la invoca.
/// </remarks>
public interface IGymAdministration
{
    /// <summary>Marca el estado comercial del gym. Falla con 404 si el gym no existe.</summary>
    Task SetCommercialStatusAsync(Guid gymId, GymStatus status, CancellationToken cancellationToken = default);
}
