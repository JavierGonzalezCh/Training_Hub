namespace GSM.Infrastructure.Identity;

/// <summary>Datos del superadmin bootstrap. Solo se siembra si <see cref="Email"/> tiene valor.</summary>
public sealed class IdentitySeedOptions
{
    public const string SectionName = "Seed:SuperAdmin";

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FullName { get; init; } = "GSM Super Admin";
}
