namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Lo que Identity necesita saber para cerrar el login: si el gym del usuario está suspendido.
/// </summary>
/// <remarks>
/// Lo declara Identity y lo implementa SuperAdmin en Infrastructure (inversión de dependencia): así
/// Identity no depende de SuperAdmin y el login no cierra un ciclo entre módulos.
/// </remarks>
public interface IGymLoginGate
{
    /// <summary><c>true</c> si el gym está suspendido. Un gym sin suscripción no lo está: no se bloquea.</summary>
    Task<bool> IsSuspendedAsync(Guid gymId, CancellationToken cancellationToken = default);
}
