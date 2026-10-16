namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Cambio de email de la cuenta con verificación (Identity §3.4). Implementado en Infrastructure. Solo guarda
/// el hash del código; el código en claro sale una vez, en <see cref="EmailChangeIssue"/>.
/// </summary>
public interface IEmailChangeService
{
    /// <summary>
    /// Emite un código para <paramref name="newEmail"/> y otro para el correo actual de la cuenta (anula el pedido anterior de la persona). Límites: uno
    /// por minuto y cinco por hora por persona.
    /// </summary>
    Task<EmailChangeIssue> IssueAsync(Guid userId, string newEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Comprueba los dos códigos del pedido vigente (el del correo nuevo y el del actual). Correcto: lo consume, cambia el email de la cuenta (y su nombre de
    /// usuario) y devuelve el resultado. Incorrecto: suma un intento al pedido (a los 5 se agota).
    /// </summary>
    Task<EmailChangeConfirmation> ConfirmAsync(
        Guid userId, string code, string currentCode, CancellationToken cancellationToken = default);
}

public enum EmailChangeIssueStatus
{
    Issued,

    /// <summary>La cuenta no existe o está inactiva.</summary>
    AccountUnavailable,

    /// <summary>El email nuevo es el que la cuenta ya tiene.</summary>
    SameEmail,

    /// <summary>Otra cuenta ya usa ese email.</summary>
    EmailTaken,

    /// <summary>Se pidió uno hace menos de un minuto, o ya van cinco en la última hora.</summary>
    Throttled,
}

/// <param name="Code">El código para el correo nuevo, en claro; solo con <see cref="EmailChangeIssueStatus.Issued"/>. Nunca se loguea.</param>
/// <param name="CurrentEmail">El correo actual de la cuenta, al que va <paramref name="CurrentCode"/>.</param>
/// <param name="CurrentCode">El código para el correo actual, en claro. Nunca se loguea.</param>
public sealed record EmailChangeIssue(
    EmailChangeIssueStatus Status,
    string? NewEmail = null,
    string? FullName = null,
    string? Code = null,
    string? CurrentEmail = null,
    string? CurrentCode = null);

public enum EmailChangeConfirmationStatus
{
    Changed,

    /// <summary>No hay pedido vigente, venció, se agotó o el código no coincide.</summary>
    InvalidCode,

    /// <summary>Mientras tanto otra cuenta tomó ese email.</summary>
    EmailTaken,

    AccountUnavailable,
}

public sealed record EmailChangeConfirmation(
    EmailChangeConfirmationStatus Status, string? OldEmail = null, string? NewEmail = null, string? FullName = null);
