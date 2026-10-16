using GSM.Domain.Members;

namespace GSM.Application.Members.Public;

/// <summary>Lectura que el módulo Members expone a los demás módulos.</summary>
public interface IMemberReader
{
    Task<MemberInfo?> GetAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Miembros del gym en contexto cuyo número de documento coincide (sin puntos ni espacios; el tipo no
    /// importa: el teclado del kiosco solo digita el número). Devuelve a lo sumo 2: si vienen 2 el número es
    /// ambiguo (mismo número con otro tipo de documento) y quien llama no debe elegir uno. Para la entrada con
    /// documento del kiosco (A4); no es una búsqueda para mostrar datos.
    /// </summary>
    Task<IReadOnlyList<MemberInfo>> FindByDocumentNumberAsync(
        string documentNumber, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Miembro vinculado a esa cuenta de Identity. <c>null</c> si el usuario no es un miembro.</summary>
    Task<MemberInfo?> ResolveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> GetActiveCountAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Datos de contacto de un miembro (Comunicaciones). Nulo si no existe.</summary>
    Task<MemberContactView?> GetContactAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Miembros activos del gym con email (destinatarios de un anuncio por correo).</summary>
    Task<IReadOnlyList<MemberContactView>> GetEmailContactsAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Quién refirió a este miembro (<c>ReferredByMemberId</c>). <c>null</c> si nadie o el miembro no existe. Para los logros de referidos (L2).</summary>
    Task<Guid?> GetReferrerIdAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Los miembros referidos por cada uno de <paramref name="referrerIds"/> (ficha "referido por"). Quien no
    /// refirió a nadie no aparece. En bloque, para evaluar referidos sin ir miembro por miembro (L2).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetReferredMemberIdsAsync(
        IReadOnlyCollection<Guid> referrerIds, CancellationToken cancellationToken = default);

    /// <summary>Ids de los miembros activos del gym (audiencia de un anuncio).</summary>
    Task<IReadOnlyList<Guid>> GetActiveMemberIdsAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Celular de cada miembro en E.164 (<c>+573001234567</c>), listo para <c>wa.me</c>. Quien no tiene
    /// celular o no es interpretable no aparece. Para el botón de WhatsApp (Comunicaciones).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetWhatsAppPhonesAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids de los miembros activos del gym en contexto que tienen alguna de las etiquetas (sin distinguir
    /// mayúsculas). Filtro "etiqueta" de la audiencia de un anuncio (CO2).
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetActiveMemberIdsByTagsAsync(
        IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default);

    /// <summary>Etiquetas distintas en uso entre los miembros activos del gym en contexto, ordenadas (CO2).</summary>
    Task<IReadOnlyList<string>> GetActiveTagsAsync(CancellationToken cancellationToken = default);

    /// <summary>Nombre completo de cada miembro pedido (revisar una audiencia, CO2). Los que no existen no aparecen.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Miembros activos del gym que cumplen años el día de negocio <paramref name="day"/> (con la regla del 29 de
    /// febrero, <see cref="BirthdayRule"/>). Lleva <paramref name="gymId"/> explícito porque la tarea de
    /// felicitaciones corre sin usuario (CO5).
    /// </summary>
    Task<IReadOnlyList<MemberBirthdayView>> GetBirthdaysOnAsync(
        Guid gymId, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>¿Cumple años ese día el miembro (activo o no)? Para la tarjeta de recepción (CO5).</summary>
    Task<bool> IsBirthdayAsync(Guid memberId, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>
    /// Datos para mostrar a un miembro en el ranking (L6): nombre, foto y si es menor de edad a <paramref name="today"/>
    /// (sin fecha de nacimiento cuenta como menor: ante la duda, no participa). Los que no existen no aparecen.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, MemberRankingView>> GetRankingViewsAsync(
        IReadOnlyCollection<Guid> memberIds, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>La invitación más reciente sin canjear del miembro (puede estar vencida: lo decide quien llama). <c>null</c> si no hay.</summary>
    Task<MemberInvitationView?> GetPendingInvitationAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿Tiene el miembro una autorización de tratamiento de datos aceptada y no revocada? Es el
    /// candado de las secciones de salud (M3) y medidas (M4): sin ella no se guardan ni se leen.
    /// Basta con haber aceptado cualquier versión; publicar un texto nuevo no la anula (la ficha
    /// solo avisa que falta aceptar la vigente).
    /// </summary>
    Task<bool> HasDataProcessingConsentAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿Tiene la exoneración de responsabilidad aceptada (cualquier versión)? Recepción avisa
    /// "falta la exoneración" cuando es falso. Sin más detalle.
    /// </summary>
    Task<bool> HasLiabilityWaiverAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Miembros activos por gym, en una sola consulta (panel de SuperAdmin). Un gym sin miembros
    /// activos no aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetActiveCountsByGymAsync(CancellationToken cancellationToken = default);
}

public sealed record MemberRankingView(
    Guid Id, string FirstName, string LastName, string? PhotoUrl, MemberStatus Status, bool IsMinorOrUnknownAge);

public sealed record MemberBirthdayView(Guid Id, string FirstName, string FullName);

public sealed record MemberInvitationView(string Code, DateTimeOffset ExpiresAt);

public sealed record MemberContactView(
    Guid Id,
    Guid GymId,
    string FirstName,
    string? Email,
    MemberStatus Status);

public sealed record MemberInfo(
    Guid Id,
    Guid GymId,
    Guid? UserId,
    string FirstName,
    string LastName,
    MemberStatus Status,
    string? PhotoUrl = null,
    bool HasHealthAlerts = false);
