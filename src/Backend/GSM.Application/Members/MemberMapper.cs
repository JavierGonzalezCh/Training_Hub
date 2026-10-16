using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;

namespace GSM.Application.Members;

/// <summary>
/// Mapeo a mano de la ficha: los enums viajan como texto snake_case (ver <see cref="IMemberProfileInput"/>),
/// una conversión que Mapperly no resuelve sin más configuración de la que ahorra.
/// </summary>
public static class MemberMapper
{
    // CurrentMembership y RecentPayments las compone el controller (IMembershipReader / IPaymentReader).
    public static MemberDetail ToDetail(Member m, DateOnly today) => new(
        m.Id,
        m.GymId,
        m.UserId,
        m.FirstName,
        m.LastName,
        EnumText.Snake(m.DocumentType)!,
        m.DocumentNumber,
        m.BirthDate,
        EnumText.Snake(m.Gender),
        m.PhotoUrl,
        m.Phone,
        m.Email,
        m.Address,
        m.City,
        m.EmergencyContactName,
        m.EmergencyContactRelationship,
        m.EmergencyContactPhone,
        EnumText.Snake(m.Source),
        m.ReferredByMemberId,
        EnumText.Snake(m.Goal),
        m.Tags,
        m.GuardianName,
        EnumText.Snake(m.GuardianDocumentType),
        m.GuardianDocumentNumber,
        m.GuardianPhone,
        m.GuardianRelationship,
        m.Status.ToString(),
        m.JoinedAt,
        m.CreatedAt,
        m.IsMinor(today),
        m.IsProfileIncomplete)
    {
        TrainerUserId = m.TrainerUserId,
        HasHealthAlerts = m.HasHealthAlerts,
        IsAnonymized = m.IsAnonymized,
        DeactivatedOn = m.DeactivatedOn,
    };

    public static MemberListItem ToListItem(Member m) => new(
        m.Id,
        m.FirstName,
        m.LastName,
        EnumText.Snake(m.DocumentType)!,
        m.DocumentNumber,
        m.Phone,
        m.Status.ToString(),
        m.JoinedAt,
        m.Tags,
        m.IsProfileIncomplete,
        TrainerUserId: m.TrainerUserId,
        HasHealthAlerts: m.HasHealthAlerts);

    public static MemberProfile ToProfile(IMemberProfileInput r) => new(
        r.FirstName,
        r.LastName,
        Parse<DocumentType>(r.DocumentType, "document_type") ?? DocumentType.Cc,
        r.DocumentNumber,
        r.BirthDate,
        Parse<Gender>(r.Gender, "gender"),
        r.PhotoUrl,
        r.Phone,
        r.Email,
        r.Address,
        r.City,
        r.EmergencyContactName,
        r.EmergencyContactRelationship,
        r.EmergencyContactPhone,
        Parse<MemberSource>(r.Source, "source"),
        r.ReferredByMemberId,
        Parse<MemberGoal>(r.Goal, "goal"),
        r.Tags,
        r.GuardianName,
        Parse<DocumentType>(r.GuardianDocumentType, "guardian_document_type"),
        r.GuardianDocumentNumber,
        r.GuardianPhone,
        r.GuardianRelationship);

    private static T? Parse<T>(string? text, string field) where T : struct, Enum
        => EnumText.TryParse<T>(text, out var value)
            ? value
            : throw new ValidationDomainException($"Valor no válido para {field}: '{text}'.");
}
