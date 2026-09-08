namespace GSM.Application.Memberships.Public;

/// <summary>Lectura que el módulo Memberships expone a los demás módulos.</summary>
public interface IMembershipReader
{
    /// <summary>Membresía que cubre hoy (Active y con el rango de fechas conteniendo la fecha actual). <c>null</c> si no hay.</summary>
    Task<ActiveMembershipView?> GetActiveAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpiringMembershipView>> GetExpiringAsync(
        Guid gymId, int withinDays, CancellationToken cancellationToken = default);

    /// <summary>¿La membresía existe y pertenece a ese miembro? (validación al registrar un pago).</summary>
    Task<bool> BelongsToMemberAsync(
        Guid membershipId, Guid memberId, CancellationToken cancellationToken = default);
}

public sealed record ActiveMembershipView(
    Guid MembershipId,
    Guid PlanId,
    string PlanName,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record ExpiringMembershipView(
    Guid MembershipId,
    Guid MemberId,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft);
