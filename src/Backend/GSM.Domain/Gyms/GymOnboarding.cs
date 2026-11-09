using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Gyms;

/// <summary>
/// Estado del onboarding de un gym (Gyms §5). Los pasos <b>no se guardan</b>: se calculan al leer contra el
/// dato real (dirección, horario, máquinas, planes). Aquí queda solo lo que no se puede derivar: cuándo se
/// completó (y de ahí en adelante se da por hecho, aunque el Owner borre una máquina) y si el SuperAdmin lo omitió.
/// </summary>
/// <remarks>
/// Sin fila el onboarding está pendiente: así nace todo gym nuevo. La migración G5 inserta la fila de los gyms
/// existentes ya completada para no bloquearlos.
/// </remarks>
public sealed class GymOnboarding : BaseEntity, ITenantEntity
{
    public const int MaxSkipReasonLength = 500;

    private GymOnboarding()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Cuándo se cumplieron los pasos obligatorios (o se omitió). <c>null</c> = pendiente.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>El SuperAdmin que lo omitió; <c>null</c> si el Owner lo completó.</summary>
    public Guid? SkippedBy { get; private set; }

    public DateTimeOffset? SkippedAt { get; private set; }

    public string? SkipReason { get; private set; }

    /// <summary>El Owner dijo «Trabajo solo»: el paso de equipo queda resuelto y el pendiente de invitar staff desaparece.</summary>
    public bool TeamSkipped { get; private set; }

    public bool IsDone => CompletedAt is not null;

    public bool WasSkipped => SkippedBy is not null;

    public static GymOnboarding Start(Guid gymId)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El onboarding necesita un gym.");
        }

        return new GymOnboarding { GymId = gymId };
    }

    public void MarkTeamSkipped() => TeamSkipped = true;

    /// <summary>Marca los pasos obligatorios como cumplidos. Idempotente: no cambia la fecha si ya estaba.</summary>
    public void Complete(DateTimeOffset now) => CompletedAt ??= now;

    /// <summary>El SuperAdmin omite el onboarding (soporte). Exige motivo; si ya estaba completo es un conflicto.</summary>
    public void Skip(Guid skippedBy, string? reason, DateTimeOffset now)
    {
        if (skippedBy == Guid.Empty)
        {
            throw new ValidationDomainException("Falta quién omite el onboarding.");
        }

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ValidationDomainException("Indica el motivo para omitir el onboarding.");
        }

        if (trimmed.Length > MaxSkipReasonLength)
        {
            throw new ValidationDomainException($"El motivo no puede superar {MaxSkipReasonLength} caracteres.");
        }

        if (IsDone)
        {
            throw new ConflictException("El onboarding de este gym ya está completo.");
        }

        CompletedAt = now;
        SkippedBy = skippedBy;
        SkippedAt = now;
        SkipReason = trimmed;
    }
}
