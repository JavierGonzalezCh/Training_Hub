namespace GSM.Application.Identity.Contracts;

/// <summary>Invita a un nuevo Owner del gym. Nace sin contraseña y recibe un enlace de activación (72 h).</summary>
public sealed record InviteOwnerRequest(string Email, string FullName);
