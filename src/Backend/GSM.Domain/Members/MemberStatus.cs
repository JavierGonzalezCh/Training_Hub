namespace GSM.Domain.Members;

/// <summary>Estado del miembro. La baja es un soft delete a <see cref="Inactive"/>.</summary>
public enum MemberStatus
{
    Active,
    Inactive,
}
