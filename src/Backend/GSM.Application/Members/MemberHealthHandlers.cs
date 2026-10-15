using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>
/// Sección de salud de la ficha (M3). Quien la lee debe tener el permiso <c>ViewMemberHealth</c> (el
/// controller lo exige y aquí se repite). Sin autorización de datos del miembro está bloqueada. Al
/// entregar el detalle se registra la consulta (§10), salvo la del SuperAdmin: esa va a su auditoría.
/// </summary>
public sealed class GetMemberHealthHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    IConsentRepository consents,
    ILogger<GetMemberHealthHandler> logger)
{
    public async Task<MemberHealthView> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        MemberHealthRules.RequirePermission(currentUser);

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        if (!await MemberHealthRules.HasDataConsentAsync(consents, memberId, cancellationToken))
        {
            return new MemberHealthView(ConsentRequired: true, Health: null);
        }

        // Se registra antes de entregar: si no queda constancia, no se entregan los datos.
        if (!currentUser.IsSuperAdmin && currentUser.UserId is { } userId)
        {
            await members.LogHealthAccessAsync(
                MemberHealthAccess.Record(
                    gymId, memberId, userId,
                    currentUser.Role == UserRole.Owner ? "owner" : "staff",
                    clock.UtcNow, clock.Today),
                cancellationToken);
        }

        logger.LogInformation("Sección de salud consultada del miembro {MemberId}", memberId);
        return new MemberHealthView(ConsentRequired: false, MemberHealthRules.ToHealth(member));
    }
}

public sealed class UpdateMemberHealthHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    IConsentRepository consents,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    ILogger<UpdateMemberHealthHandler> logger)
{
    public async Task<MemberHealth> HandleAsync(
        Guid memberId, UpdateMemberHealthRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        MemberHealthRules.RequirePermission(currentUser);

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        if (!await MemberHealthRules.HasDataConsentAsync(consents, memberId, cancellationToken))
        {
            throw new ConflictException(
                "El miembro no ha aceptado la autorización de tratamiento de datos: sin ella no se pueden guardar datos de salud.");
        }

        var fieldChanges = member.UpdateHealth(
            new MemberHealthData(
                request.MedicalConditions, request.Medications, request.TrainingRestrictions, request.MedicalClearanceDate),
            clock.Today);
        changes.Record(member, fieldChanges);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Solo el número de campos: el contenido de salud no se loguea.
        logger.LogInformation(
            "Salud del miembro {MemberId} actualizada ({ChangedFields} campos)", memberId, fieldChanges.Count);
        return MemberHealthRules.ToHealth(member);
    }
}

/// <summary>Quién vio los datos de salud del miembro. Solo el Owner (el controller exige la política).</summary>
public sealed class ListMemberHealthAccessHandler(
    ICurrentUserService currentUser,
    IMemberRepository members,
    IIdentityReader identity)
{
    public async Task<PagedResult<MemberHealthAccessItem>> HandleAsync(
        Guid memberId, ListMemberChangesQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await members.GetHealthAccessPageAsync(memberId, page, pageSize, cancellationToken);
        var names = await AuthorNames.ResolveAsync(identity, items.Select(a => (Guid?)a.UserId), cancellationToken);

        return new PagedResult<MemberHealthAccessItem>(
            items.Select(a => new MemberHealthAccessItem(
                a.Id, a.UserId, names.GetValueOrDefault(a.UserId), a.Role, a.AccessedAt)).ToList(),
            page, pageSize, total);
    }
}

internal static class MemberHealthRules
{
    public static void RequirePermission(ICurrentUserService currentUser)
    {
        if (!currentUser.HasPermission(StaffPermission.ViewMemberHealth))
        {
            throw new ForbiddenDomainException("No tienes permiso para ver ni editar los datos de salud.");
        }
    }

    /// <summary>La autorización de datos más reciente existe y no está revocada (cualquier versión basta).</summary>
    public static async Task<bool> HasDataConsentAsync(
        IConsentRepository consents, Guid memberId, CancellationToken cancellationToken)
    {
        var latest = await consents.GetLatestAsync(memberId, ConsentType.DataProcessing, cancellationToken);
        return latest is { IsRevoked: false };
    }

    public static MemberHealth ToHealth(Member m) => new(
        m.MedicalConditions, m.Medications, m.TrainingRestrictions, m.MedicalClearanceDate);
}
