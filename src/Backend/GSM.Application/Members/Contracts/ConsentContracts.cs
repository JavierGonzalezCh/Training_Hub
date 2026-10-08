namespace GSM.Application.Members.Contracts;

/// <summary>
/// El texto vigente de una autorización. <c>Version</c> 0 y <c>IsDefault</c> = es el texto base del
/// sistema (el gym no ha publicado el suyo). Los tipos viajan en snake_case (<c>data_processing</c>,
/// <c>liability_waiver</c>) para que el cliente generado los reciba como <c>string</c>.
/// </summary>
public sealed record ConsentDocumentItem(
    string Type,
    int Version,
    string Text,
    DateTimeOffset? EffectiveFrom,
    bool IsDefault);

/// <summary>Publica una versión nueva del texto de un tipo de autorización.</summary>
public sealed record PublishConsentDocumentRequest(string Type, string Text);

/// <summary>
/// Acepta una autorización. <c>DocumentVersion</c> es opcional: si viene, debe ser la vigente (si
/// el texto cambió mientras el miembro leía, 409 y vuelve a leer); si no, se acepta la vigente.
/// </summary>
public sealed record AcceptConsentRequest(string Type, int? DocumentVersion);

/// <summary>
/// Estado de una autorización para un miembro: <c>not_accepted</c> (nunca aceptó),
/// <c>accepted</c> (aceptó la versión vigente), <c>outdated</c> (aceptó una anterior: pendiente de
/// aceptar la vigente) o <c>revoked</c> (la revocó).
/// </summary>
public sealed record MemberConsentStatus(
    string Type,
    string Status,
    int CurrentVersion,
    string CurrentText,
    int? AcceptedVersion,
    DateTimeOffset? AcceptedAt,
    string? Channel,
    bool AcceptedByGuardian,
    Guid? AcceptedByUserId,
    string? AcceptedByName,
    DateTimeOffset? RevokedAt);
