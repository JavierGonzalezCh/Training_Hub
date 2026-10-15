using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Payments;

/// <summary>
/// Turno de caja con arqueo (Payments v2 §4). Hay a lo sumo uno abierto por gym. Al cerrar se guarda el
/// efectivo esperado, el contado y la diferencia, y el turno ya no se reabre ni se edita.
/// </summary>
public sealed class CashShift : BaseEntity, ITenantEntity
{
    public const int NotesMaxLength = 500;

    private CashShift()
    {
    }

    public Guid GymId { get; private set; }

    public Guid OpenedBy { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>Efectivo con el que arranca el turno (la base).</summary>
    public decimal OpeningCash { get; private set; }

    public Guid? ClosedBy { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Efectivo contado al cerrar.</summary>
    public decimal? CountedCash { get; private set; }

    /// <summary>Efectivo esperado al cerrar. Se guarda para que el cierre no cambie después.</summary>
    public decimal? ExpectedCash { get; private set; }

    /// <summary>Contado menos esperado (negativo = faltante).</summary>
    public decimal? Difference { get; private set; }

    /// <summary>Obligatoria si la diferencia no es 0.</summary>
    public string? CloseNotes { get; private set; }

    public bool IsOpen => ClosedAt is null;

    public static CashShift Open(Guid gymId, Guid openedBy, DateTimeOffset openedAt, decimal openingCash)
    {
        if (gymId == Guid.Empty || openedBy == Guid.Empty)
        {
            throw new ValidationDomainException("El turno necesita gym y quien lo abre.");
        }

        if (openingCash < 0)
        {
            throw new ValidationDomainException("La base de la caja no puede ser negativa.");
        }

        return new CashShift
        {
            GymId = gymId,
            OpenedBy = openedBy,
            OpenedAt = openedAt,
            OpeningCash = decimal.Round(openingCash, 2),
        };
    }

    public void Close(Guid closedBy, DateTimeOffset closedAt, decimal countedCash, decimal expectedCash, string? notes)
    {
        if (!IsOpen)
        {
            throw new ConflictException("Este turno ya está cerrado.");
        }

        if (closedBy == Guid.Empty)
        {
            throw new ValidationDomainException("El cierre necesita quién lo hace.");
        }

        if (countedCash < 0)
        {
            throw new ValidationDomainException("El efectivo contado no puede ser negativo.");
        }

        var counted = decimal.Round(countedCash, 2);
        var expected = decimal.Round(expectedCash, 2);
        var difference = counted - expected;

        var trimmed = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (difference != 0 && (trimmed is null || trimmed.Length < 3))
        {
            throw new ValidationDomainException("La caja no cuadra: escribe una nota que explique la diferencia.");
        }

        if (trimmed is { Length: > NotesMaxLength })
        {
            throw new ValidationDomainException($"La nota no puede superar {NotesMaxLength} caracteres.");
        }

        ClosedBy = closedBy;
        ClosedAt = closedAt;
        CountedCash = counted;
        ExpectedCash = expected;
        Difference = difference;
        CloseNotes = trimmed;
    }
}
