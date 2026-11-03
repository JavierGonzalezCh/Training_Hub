namespace GSM.API.Filters;

/// <summary>
/// FL1: la acción se atiende aunque el token lleve <c>pwd_change</c> (la cuenta aún debe crear su contraseña):
/// crearla, ver la sesión y salir. Todo lo demás responde 403 con el código <c>password_change_required</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhilePasswordChangeRequiredAttribute : Attribute;
