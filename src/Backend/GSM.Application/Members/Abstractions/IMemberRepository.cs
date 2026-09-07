using GSM.Domain.Members;

namespace GSM.Application.Members.Abstractions;

public sealed record MemberPageQuery(string? Search, MemberStatus? Status, int Page, int PageSize);

/// <summary>Acceso de escritura/consulta a las entidades del módulo Members. Sin <c>SaveChanges</c>.</summary>
public interface IMemberRepository
{
    Task<Member?> GetByIdAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Member> Items, int Total)> GetPageAsync(
        MemberPageQuery query, CancellationToken cancellationToken = default);

    /// <summary>¿Hay otro miembro (del gym en contexto) con ese documento?</summary>
    Task<bool> DocumentExistsAsync(
        string documentNumber, Guid? excludeMemberId, CancellationToken cancellationToken = default);

    Task<bool> QrExistsAsync(string qrCode, CancellationToken cancellationToken = default);

    void Add(Member member);
}
