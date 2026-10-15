namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Código de 6 dígitos por correo para que el miembro entre sin contraseña (Identity §6). Implementado en
/// Infrastructure. Solo guarda el hash; el código en claro sale una vez, en <see cref="LoginCodeIssue"/>.
/// </summary>
public interface ILoginCodeService
{
    /// <summary>
    /// Emite un código nuevo (anula el anterior) para la cuenta con ese email, si es una persona activa con
    /// un acceso de miembro. Límites: uno por minuto y cinco por hora por persona.
    /// </summary>
    Task<LoginCodeIssue> IssueAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Comprueba el código. Correcto: lo consume (un solo uso) y devuelve la cuenta. Incorrecto: suma un
    /// intento al código vigente (a los 5 se agota). <c>null</c> si el email no tiene cuenta elegible, no hay
    /// código vigente, venció, se agotó o no coincide: el llamador no distingue los casos hacia afuera.
    /// No mira el bloqueo por contraseñas: quien tiene el correo no queda afuera.
    /// </summary>
    Task<AccountIdentity?> VerifyAsync(string email, string code, CancellationToken cancellationToken = default);
}

public enum LoginCodeIssueStatus
{
    Issued,

    /// <summary>El email no tiene cuenta, está inactiva o no tiene acceso de miembro.</summary>
    NotEligible,

    /// <summary>Se pidió uno hace menos de un minuto, o ya van cinco en la última hora.</summary>
    Throttled,
}

/// <param name="Code">El código en claro; solo con <see cref="LoginCodeIssueStatus.Issued"/>. Nunca se loguea.</param>
public sealed record LoginCodeIssue(
    LoginCodeIssueStatus Status,
    string? Email = null,
    string? FullName = null,
    string? Code = null);
