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
/// Mediciones de peso y altura (M4, Members v2 §3.2). Son datos de salud: mismo permiso
/// (<c>ViewMemberHealth</c>) y mismo candado (autorización de datos del miembro) que la sección de salud.
/// Los pesos y alturas nunca se loguean.
/// </summary>
public sealed class ListMemberMeasurementsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    IConsentRepository consents,
    IIdentityReader identity)
{
    public async Task<MemberMeasurementsView> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        MemberHealthRules.RequirePermission(currentUser);

        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        if (!await MemberHealthRules.HasDataConsentAsync(consents, memberId, cancellationToken))
        {
            return new MemberMeasurementsView(ConsentRequired: true, Items: []);
        }

        // M4-2: igual que la sección de salud, la consulta deja constancia (salvo la del SuperAdmin, que va a su auditoría).
        if (!currentUser.IsSuperAdmin && currentUser.UserId is { } userId)
        {
            await members.LogHealthAccessAsync(
                MemberHealthAccess.Record(
                    gymId, memberId, userId,
                    currentUser.Role == UserRole.Owner ? "owner" : "staff",
                    clock.UtcNow, clock.Today),
                cancellationToken);
        }

        var rows = await members.GetMeasurementsAsync(memberId, cancellationToken);
        var names = await AuthorNames.ResolveAsync(identity, rows.Select(r => r.RecordedByUserId), cancellationToken);
        return new MemberMeasurementsView(ConsentRequired: false, MeasurementMapper.ToItems(rows, names));
    }
}

public sealed class AddMemberMeasurementHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    IConsentRepository consents,
    IIdentityReader identity,
    IUnitOfWork unitOfWork,
    ILogger<AddMemberMeasurementHandler> logger)
{
    public async Task<MemberMeasurementItem> HandleAsync(
        Guid memberId, AddMemberMeasurementRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        MemberHealthRules.RequirePermission(currentUser);

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");
        AnonymizedGuard.EnsureNotAnonymized(member);

        if (!await MemberHealthRules.HasDataConsentAsync(consents, memberId, cancellationToken))
        {
            throw new ConflictException(
                "El miembro no ha aceptado la autorización de tratamiento de datos: sin ella no se pueden guardar mediciones.");
        }

        var today = clock.Today;
        var measurement = MemberMeasurement.Add(
            gymId, memberId, request.MeasuredOn ?? today, today, request.WeightKg, request.HeightCm,
            currentUser.UserId, RoleText(currentUser.Role));
        members.Add(measurement);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Medición añadida al miembro {MemberId}", memberId);

        // El IMC y la altura vigente dependen de las demás filas: se relee la lista completa.
        var rows = await members.GetMeasurementsAsync(memberId, cancellationToken);
        var names = await AuthorNames.ResolveAsync(identity, [measurement.RecordedByUserId], cancellationToken);
        return MeasurementMapper.ToItems(rows, names).First(i => i.Id == measurement.Id);
    }

    private static string RoleText(UserRole? role) => role switch
    {
        UserRole.Owner => "owner",
        UserRole.Staff => "staff",
        UserRole.SuperAdmin => "superadmin",
        UserRole.Member => "member",
        _ => "staff",
    };
}

/// <summary>Borra una medición mal digitada; el resumen de la fila queda en el historial de cambios.</summary>
public sealed class DeleteMemberMeasurementHandler(
    ICurrentUserService currentUser,
    IMemberRepository members,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    ILogger<DeleteMemberMeasurementHandler> logger)
{
    public async Task HandleAsync(Guid memberId, Guid measurementId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        MemberHealthRules.RequirePermission(currentUser);

        var measurement = await members.GetMeasurementAsync(memberId, measurementId, cancellationToken)
            ?? throw new NotFoundException("Medición no encontrada.");

        // Misma transacción: no hay borrado sin su fila de historial.
        changes.RecordMeasurementDeleted(gymId, memberId, measurement.Summary());
        members.Remove(measurement);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Medición {MeasurementId} borrada del miembro {MemberId}", measurementId, memberId);
    }
}

internal static class MeasurementMapper
{
    /// <summary>
    /// Recorre las filas de la más antigua a la más reciente arrastrando la última altura registrada
    /// (una medición sin altura usa la anterior) y devuelve la lista de la más reciente a la más antigua.
    /// </summary>
    public static IReadOnlyList<MemberMeasurementItem> ToItems(
        IReadOnlyList<MemberMeasurement> rowsNewestFirst, IReadOnlyDictionary<Guid, string> names)
    {
        decimal? lastHeight = null;
        var items = new List<MemberMeasurementItem>(rowsNewestFirst.Count);

        foreach (var m in rowsNewestFirst.Reverse())
        {
            lastHeight = m.HeightCm ?? lastHeight;
            items.Add(new MemberMeasurementItem(
                m.Id,
                m.MeasuredOn,
                m.WeightKg,
                m.HeightCm,
                lastHeight,
                MemberMeasurement.Bmi(m.WeightKg, lastHeight),
                m.RecordedByUserId,
                m.RecordedByUserId is { } id && names.TryGetValue(id, out var name) ? name : null,
                m.RecordedByRole,
                m.CreatedAt));
        }

        items.Reverse();
        return items;
    }
}
