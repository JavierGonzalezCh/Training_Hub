namespace GSM.Application.Memberships.Abstractions;

/// <summary>
/// Puerto que declara Memberships para saber cuántos miembros puede tener a la vez un gym según su plan
/// del SaaS (SuperAdmin §V1). Lo implementa SuperAdmin en Infrastructure, como <c>IGymLoginGate</c> de
/// Identity: Memberships no conoce ese módulo y ningún módulo escribe en el otro.
/// </summary>
public interface IMemberLimitProvider
{
    /// <summary>
    /// Límite de miembros que cuentan del gym, o <c>null</c> si no tiene (plan ilimitado). Ante un dato
    /// faltante (gym sin suscripción) devuelve <c>null</c>: se deja pasar, como en el bloqueo por impago.
    /// </summary>
    Task<int?> GetLimitAsync(Guid gymId, CancellationToken cancellationToken = default);
}
