namespace GSM.Domain.Identity;

/// <summary>Roles del sistema. El valor de texto (minúscula) es el que viaja en el claim del JWT.</summary>
public enum UserRole
{
    SuperAdmin,
    Owner,
    Staff,
    Member,
}
