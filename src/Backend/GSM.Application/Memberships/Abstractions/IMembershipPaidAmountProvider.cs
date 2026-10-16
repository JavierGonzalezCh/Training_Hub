namespace GSM.Application.Memberships.Abstractions;

/// <summary>
/// Puerto que declara Memberships para saber cuánto se pagó de verdad por una membresía (cobros menos anulaciones y
/// devoluciones), que es la base del crédito de un cambio de plan (§8). Lo implementa Payments en Infrastructure, como
/// <see cref="IMemberLimitProvider"/>: así Memberships no conoce a Payments y la dependencia va en un solo sentido
/// (Payments → Memberships), sin un segundo ciclo entre módulos.
/// </summary>
public interface IMembershipPaidAmountProvider
{
    Task<decimal> GetNetPaidAsync(Guid membershipId, CancellationToken cancellationToken = default);
}
