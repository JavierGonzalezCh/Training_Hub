using GSM.Domain.Members;

namespace GSM.Application.Members.Abstractions;

/// <summary>
/// <c>Ids</c> acota la lista a esos miembros (o a todos menos esos si <c>ExcludeIds</c>): es el filtro
/// por estado comercial, cuyos ids entrega Memberships.
/// </summary>
public sealed record MemberPageQuery(
    string? Search, MemberStatus? Status, string? Tag, int Page, int PageSize,
    IReadOnlyCollection<Guid>? Ids = null, bool ExcludeIds = false,
    Guid? TrainerUserId = null, bool WithoutTrainer = false, bool WithHealthAlerts = false);

/// <summary>Acceso de escritura/consulta a las entidades del módulo Members. Sin <c>SaveChanges</c>.</summary>
public interface IMemberRepository
{
    Task<Member?> GetByIdAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Member> Items, int Total)> GetPageAsync(
        MemberPageQuery query, CancellationToken cancellationToken = default);

    /// <summary>¿Hay otro miembro (del gym en contexto) con ese tipo y número de documento?</summary>
    Task<bool> DocumentExistsAsync(
        DocumentType documentType, string documentNumber, Guid? excludeMemberId,
        CancellationToken cancellationToken = default);

    /// <summary>¿Existe el miembro en el gym en contexto? (valida el referente).</summary>
    Task<bool> ExistsAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Miembros (seguidos) que tienen a esta cuenta como entrenador asignado.</summary>
    Task<IReadOnlyList<Member>> GetByTrainerAsync(Guid trainerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Las fichas (seguidas) vinculadas a esta cuenta en TODOS los gyms: ignora el filtro de multi-tenancy. Solo
    /// para reacciones a eventos de la cuenta (cambio de email), nunca para peticiones de un gym.
    /// </summary>
    Task<IReadOnlyList<Member>> GetByUserAcrossGymsAsync(Guid userId, CancellationToken cancellationToken = default);

    void Add(Member member);

    // --- Historial de cambios ---

    void Add(MemberChange change);

    /// <summary><paramref name="includeSensitive"/> falso deja fuera las filas de salud (quien no tiene el permiso).</summary>
    Task<(IReadOnlyList<MemberChange> Items, int Total)> GetChangesPageAsync(
        Guid memberId, int page, int pageSize, bool includeSensitive, CancellationToken cancellationToken = default);

    // --- Consultas de salud (§10) ---

    /// <summary>Registra la consulta; si ya hay una del mismo usuario, miembro y día, no hace nada. Escribe al instante, sin <c>SaveChanges</c>.</summary>
    Task LogHealthAccessAsync(MemberHealthAccess access, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<MemberHealthAccess> Items, int Total)> GetHealthAccessPageAsync(
        Guid memberId, int page, int pageSize, CancellationToken cancellationToken = default);

    // --- Notas internas ---

    void Add(MemberNote note);

    /// <summary>Las notas del miembro, de la más reciente a la más antigua.</summary>
    Task<IReadOnlyList<MemberNote>> GetNotesAsync(Guid memberId, CancellationToken cancellationToken = default);

    // --- Mediciones de peso y altura ---

    void Add(MemberMeasurement measurement);

    void Remove(MemberMeasurement measurement);

    /// <summary>Todas las mediciones del miembro, de la fecha más reciente a la más antigua.</summary>
    Task<IReadOnlyList<MemberMeasurement>> GetMeasurementsAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<MemberMeasurement?> GetMeasurementAsync(
        Guid memberId, Guid measurementId, CancellationToken cancellationToken = default);

    // --- Invitaciones ---

    void Add(MemberInvitation invitation);

    Task<MemberInvitation?> GetInvitationByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> InvitationCodeExistsAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Borra las invitaciones sin canjear de un miembro (al reinvitar).</summary>
    Task RemovePendingInvitationsAsync(Guid memberId, CancellationToken cancellationToken = default);
}
