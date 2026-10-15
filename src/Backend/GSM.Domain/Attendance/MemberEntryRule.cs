using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Attendance;

/// <summary>
/// Excepción a "una entrada por día" (Attendance v2, §3.2): el staff habilita a un miembro para entrar
/// hasta <see cref="MaxDailyEntries"/> veces al día (deportistas que entrenan mañana y tarde). Como
/// mucho una regla por miembro; quitarla borra la fila (la traza queda en el log).
/// </summary>
public sealed class MemberEntryRule : BaseEntity, ITenantEntity
{
    public const int MinAllowed = 2;
    public const int MaxAllowed = 5;

    /// <summary>Tiempo mínimo entre dos entradas del mismo día (igual que la permanencia asumida, §8).</summary>
    public static readonly TimeSpan MinGapBetweenEntries = TimeSpan.FromHours(2);

    private MemberEntryRule()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    /// <summary>Entradas por día permitidas, de 2 a 5.</summary>
    public int MaxDailyEntries { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    /// <summary>Último día (inclusive, día de negocio) en que rige. Nulo = sin vencimiento.</summary>
    public DateOnly? ValidUntil { get; private set; }

    public Guid GrantedBy { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public static MemberEntryRule Create(
        Guid gymId, Guid memberId, int maxDailyEntries, string reason, DateOnly? validUntil,
        Guid grantedBy, DateTimeOffset grantedAt, DateOnly today)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de la regla incompletos.");
        }

        var rule = new MemberEntryRule { GymId = gymId, MemberId = memberId };
        rule.Apply(maxDailyEntries, reason, validUntil, grantedBy, grantedAt, today);
        return rule;
    }

    /// <summary>Cambia el máximo, el motivo o el vencimiento; queda registrado quién y cuándo.</summary>
    public void Apply(
        int maxDailyEntries, string reason, DateOnly? validUntil, Guid grantedBy, DateTimeOffset grantedAt, DateOnly today)
    {
        if (maxDailyEntries is < MinAllowed or > MaxAllowed)
        {
            throw new ValidationDomainException($"El máximo de entradas por día debe estar entre {MinAllowed} y {MaxAllowed}.");
        }

        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is < 3 or > 500)
        {
            throw new ValidationDomainException("Indica el motivo (3 a 500 caracteres).");
        }

        if (validUntil is { } until && until < today)
        {
            throw new ValidationDomainException("La fecha de vencimiento no puede estar en el pasado.");
        }

        MaxDailyEntries = maxDailyEntries;
        Reason = trimmed;
        ValidUntil = validUntil;
        GrantedBy = grantedBy;
        GrantedAt = grantedAt;
    }

    /// <summary>¿Rige en ese día de negocio?</summary>
    public bool IsActiveOn(DateOnly day) => ValidUntil is null || ValidUntil >= day;

    /// <summary>Entradas permitidas ese día: el máximo si rige, 1 si no.</summary>
    public int AllowedEntriesOn(DateOnly day) => IsActiveOn(day) ? MaxDailyEntries : 1;
}
