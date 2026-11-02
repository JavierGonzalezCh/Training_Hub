namespace GSM.Application.Identity.Contracts;

/// <summary>Invita a un nuevo Owner del gym. Nace sin contraseña y recibe un enlace de activación (72 h).</summary>
public sealed record InviteOwnerRequest(string Email, string FullName);

/// <summary>Una cuenta del gym en la ficha del SuperAdmin. <c>Role</c>: <c>owner</c>, <c>staff</c> o <c>member</c>.</summary>
public sealed record GymAccountResponse(
    Guid UserId, string Email, string FullName, string Role, bool IsActive, bool HasPassword);

/// <summary>"Cambiar clave" del SuperAdmin: la contraseña nueva de la persona.</summary>
public sealed record AdminSetPasswordRequest(string NewPassword);
