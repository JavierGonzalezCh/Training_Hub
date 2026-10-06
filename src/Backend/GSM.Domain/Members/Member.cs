using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Members;

/// <summary>
/// Persona que asiste al gimnasio. Puede existir sin <see cref="UserId"/> (registrada en recepción,
/// aún sin cuenta en la app); el vínculo con Identity se establece después, por código de invitación.
/// </summary>
public sealed class Member : BaseEntity, ITenantEntity
{
    private Member()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Cuenta de Identity vinculada. <c>null</c> hasta que el miembro canjea su invitación.</summary>
    public Guid? UserId { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string DocumentNumber { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    public string? PhotoUrl { get; private set; }

    public string? EmergencyContact { get; private set; }

    public MemberStatus Status { get; private set; } = MemberStatus.Active;

    public DateOnly JoinedAt { get; private set; }

    public static Member Create(
        Guid gymId,
        string firstName,
        string lastName,
        string documentNumber,
        DateOnly joinedAt,
        string? phone = null,
        string? email = null,
        DateOnly? birthDate = null,
        string? emergencyContact = null,
        string? photoUrl = null)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El miembro necesita un gym.");
        }

        return new Member
        {
            GymId = gymId,
            FirstName = RequireText(firstName, "El nombre", 100),
            LastName = RequireText(lastName, "El apellido", 100),
            DocumentNumber = RequireText(documentNumber, "El documento", 30),
            Phone = Clean(phone),
            Email = Clean(email),
            BirthDate = birthDate,
            EmergencyContact = Clean(emergencyContact),
            PhotoUrl = Clean(photoUrl),
            Status = MemberStatus.Active,
            JoinedAt = joinedAt,
        };
    }

    public void UpdateProfile(
        string firstName,
        string lastName,
        string documentNumber,
        string? phone,
        string? email,
        DateOnly? birthDate,
        string? emergencyContact,
        string? photoUrl)
    {
        FirstName = RequireText(firstName, "El nombre", 100);
        LastName = RequireText(lastName, "El apellido", 100);
        DocumentNumber = RequireText(documentNumber, "El documento", 30);
        Phone = Clean(phone);
        Email = Clean(email);
        BirthDate = birthDate;
        EmergencyContact = Clean(emergencyContact);
        PhotoUrl = Clean(photoUrl);
    }

    public void Deactivate() => Status = MemberStatus.Inactive;

    public void Reactivate() => Status = MemberStatus.Active;

    /// <summary>Fija solo la foto (subida de archivo).</summary>
    public void SetPhoto(string? photoUrl) => PhotoUrl = Clean(photoUrl);

    /// <summary>Vincula la cuenta de Identity. Solo una vez (etapa 3b, canje de invitación).</summary>
    public void LinkUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ValidationDomainException("userId inválido.");
        }

        if (UserId is not null)
        {
            throw new ConflictException("El miembro ya tiene una cuenta vinculada.");
        }

        UserId = userId;
    }

    private static string RequireText(string value, string label, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new ValidationDomainException($"{label} es obligatorio y no puede exceder {maxLength} caracteres.");
        }

        return trimmed;
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
