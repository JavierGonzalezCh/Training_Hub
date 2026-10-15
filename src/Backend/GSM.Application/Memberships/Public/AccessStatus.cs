namespace GSM.Application.Memberships.Public;

/// <summary>
/// Situación comercial de un miembro a una fecha (Memberships v2, §10). Se calcula, no se guarda:
/// depende del día y no hay tarea programada.
/// </summary>
public enum CommercialStatus
{
    /// <summary>Nunca tuvo membresía, o ninguna vigente (canceladas aparte).</summary>
    NoPlan,

    /// <summary>Cubre hoy y le quedan más días que la ventana de aviso.</summary>
    Current,

    /// <summary>Cubre hoy y le quedan <c>expiring_days</c> o menos.</summary>
    Expiring,

    /// <summary>Vencida, dentro de la gracia.</summary>
    Grace,

    /// <summary>Vencida, pasada la gracia.</summary>
    Expired,

    /// <summary>Su membresía está congelada.</summary>
    Frozen,

    /// <summary>Hoy no lo cubre ninguna membresía, pero hay una que empieza más adelante (y no está en gracia).</summary>
    Scheduled,
}

/// <summary>Lo que Attendance necesita saber para dejar entrar (o no) a un miembro.</summary>
public enum AccessKind
{
    /// <summary>Cubierto: entra sin aviso.</summary>
    Covered,

    /// <summary>Por vencer: entra con letrero.</summary>
    Expiring,

    /// <summary>En gracia: entra con letrero.</summary>
    Grace,

    /// <summary>Bloqueado: el miembro no entra; recepción puede dejarlo pasar con motivo.</summary>
    Blocked,
}

public enum AccessBlockReason
{
    NoMembership,
    Expired,
    Frozen,
    Scheduled,
}

/// <summary>
/// Veredicto de acceso de un miembro. <see cref="Notice"/> es el texto del letrero (§5.3) cuando el
/// miembro está por vencer o en gracia; <see cref="BlockMessage"/> explica el bloqueo al staff.
/// </summary>
/// <param name="EndsOn">Último día cubierto (o el día que venció). Nulo sin membresía.</param>
/// <param name="DaysLeft">Días que le quedan cubiertos (0 = vence hoy). Solo si cubre hoy.</param>
/// <param name="GraceEndsOn">Último día de la gracia. Solo en gracia.</param>
/// <param name="GraceDaysLeft">Días que le quedan de gracia (0 = hoy es el último). Solo en gracia.</param>
/// <param name="StartsOn">Día en que empieza su próxima membresía. Solo si está programada (<see cref="CommercialStatus.Scheduled"/>).</param>
public sealed record AccessStatus(
    CommercialStatus Commercial,
    AccessKind Kind,
    AccessBlockReason? BlockReason,
    DateOnly? EndsOn,
    int? DaysLeft,
    DateOnly? GraceEndsOn,
    int? GraceDaysLeft,
    string? Notice,
    string? BlockMessage,
    DateOnly? StartsOn = null)
{
    public bool CanEnter => Kind != AccessKind.Blocked;
}

public static class CommercialStatusExtensions
{
    /// <summary>
    /// ¿Este estado cuenta para el límite de miembros del plan del SaaS? Cuentan quienes tienen una
    /// membresía que los cubre: al día, por vencer, en gracia o congelados. Sin plan o vencido, no
    /// (SuperAdmin §V1).
    /// </summary>
    public static bool CountsTowardMemberLimit(this CommercialStatus status)
        => status is CommercialStatus.Current or CommercialStatus.Expiring
            or CommercialStatus.Grace or CommercialStatus.Frozen;
}

/// <summary>Ajustes de membresías vigentes de un gym (con los valores por defecto si no hay fila).</summary>
public sealed record MembershipSettingsView(int GraceDays, int ExpiringDays);
