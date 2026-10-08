using System.Net;
using GSM.Application.Common.Abstractions;

namespace GSM.Application.Common.Email;

/// <summary>Plantillas de los emails transaccionales del MVP. HTML mínimo, sin dependencias de render.</summary>
public static class EmailTemplates
{
    public static EmailMessage Invitation(string to, string gymName, string code)
    {
        var gym = WebUtility.HtmlEncode(gymName);
        var html = $"""
            <p>Hola,</p>
            <p><strong>{gym}</strong> te invitó a crear tu cuenta en la app.</p>
            <p>Tu código de invitación es: <strong style="font-size:20px;letter-spacing:2px">{code}</strong></p>
            <p>Vence en 7 días. Ábrela en la app e ingrésalo junto con tu documento.</p>
            """;
        return new EmailMessage(to, $"Tu invitación a {gymName}", html,
            $"{gymName} te invitó a la app. Código: {code} (vence en 7 días).");
    }

    public static EmailMessage StaffInvitation(string to, string fullName, string token)
    {
        var html = $"""
            <p>Hola {WebUtility.HtmlEncode(fullName)},</p>
            <p>Te dieron acceso al panel de tu gimnasio en GSM.</p>
            <p>Para entrar, define tu contraseña en la pantalla de restablecimiento con tu email y este código:</p>
            <p><code>{WebUtility.HtmlEncode(token)}</code></p>
            <p>El código vence en 24 horas. Si vence, usa "¿Olvidaste tu contraseña?" para pedir otro.</p>
            """;
        return new EmailMessage(to, "Tu acceso al panel del gimnasio", html,
            $"Te dieron acceso al panel. Define tu contraseña con este código: {token}");
    }

    public static EmailMessage StaffAccessGranted(string to, string fullName)
    {
        var html = $"""
            <p>Hola {WebUtility.HtmlEncode(fullName)},</p>
            <p>Te dieron acceso al panel de un gimnasio más en GSM.</p>
            <p>Entra con tu email y tu contraseña de siempre: al iniciar sesión podrás elegir a qué gimnasio entrar.</p>
            """;
        return new EmailMessage(to, "Nuevo acceso al panel de un gimnasio", html,
            "Te dieron acceso al panel de un gimnasio más. Entra con tu contraseña de siempre y elige el gimnasio.");
    }

    public static EmailMessage Welcome(string to, string gymName, string memberName)
    {
        var html = $"""
            <p>Hola {WebUtility.HtmlEncode(memberName)},</p>
            <p>Tu cuenta en <strong>{WebUtility.HtmlEncode(gymName)}</strong> ya está lista. ¡Nos vemos en el gym!</p>
            """;
        return new EmailMessage(to, $"Bienvenido a {gymName}", html,
            $"Tu cuenta en {gymName} ya está lista.");
    }

    public static EmailMessage PasswordReset(string to, string token, string? resetUrl)
    {
        var body = resetUrl is null
            ? $"""
               <p>Recibimos una solicitud para restablecer tu contraseña.</p>
               <p>Usa este código en la pantalla de restablecimiento:</p>
               <p><code style="word-break:break-all">{WebUtility.HtmlEncode(token)}</code></p>
               <p>Si no fuiste tú, ignora este correo.</p>
               """
            : $"""
               <p>Recibimos una solicitud para restablecer tu contraseña.</p>
               <p><a href="{WebUtility.HtmlEncode(resetUrl)}">Restablecer contraseña</a></p>
               <p>Si no fuiste tú, ignora este correo.</p>
               """;
        return new EmailMessage(to, "Restablece tu contraseña", body,
            resetUrl ?? $"Código de restablecimiento: {token}");
    }
}
