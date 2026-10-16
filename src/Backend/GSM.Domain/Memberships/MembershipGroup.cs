using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Memberships;

/// <summary>
/// Una venta de un plan de grupo (MS7, Memberships v2 §9): el titular que paga, el plan y los cupos que tenía el plan
/// al venderse. Cada persona del grupo tiene su propia <see cref="Membership"/> con este <c>GroupId</c>. Una renovación
/// es otro grupo (con otras fechas) que reutiliza al titular y a los beneficiarios, igual que una renovación individual
/// es otra membresía.
/// </summary>
public sealed class MembershipGroup : BaseEntity, ITenantEntity
{
    private MembershipGroup()
    {
    }

    public Guid GymId { get; private set; }

    public Guid PlanId { get; private set; }

    /// <summary>El que paga. No cambia durante la vida del grupo.</summary>
    public Guid HolderMemberId { get; private set; }

    /// <summary>Cupos con que se vendió (titular incluido). Copiados del plan: editar el plan no cambia el grupo.</summary>
    public int Seats { get; private set; }

    public static MembershipGroup Create(Guid gymId, Guid planId, Guid holderMemberId, int seats)
    {
        if (gymId == Guid.Empty || planId == Guid.Empty || holderMemberId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos del grupo incompletos.");
        }

        if (seats < 2)
        {
            throw new ValidationDomainException("Un grupo necesita al menos 2 cupos.");
        }

        return new MembershipGroup { GymId = gymId, PlanId = planId, HolderMemberId = holderMemberId, Seats = seats };
    }

    /// <summary>
    /// Valida la cantidad de beneficiarios de una asignación: el titular ocupa un cupo, así que caben como máximo
    /// <c>Seats - 1</c> beneficiarios. Puede haber menos (se llenan después mientras haya cupo).
    /// </summary>
    public static void RequireBeneficiaryCount(int seats, int beneficiaries)
    {
        if (beneficiaries < 0 || beneficiaries > seats - 1)
        {
            throw new ValidationDomainException(
                $"El plan tiene {seats} cupos (el titular ocupa uno): caben hasta {seats - 1} beneficiarios.");
        }
    }
}
