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

    /// <summary>Invitación del staff: el enlace de activación (72 h) abre el formulario para definir la contraseña.</summary>
    public static EmailMessage StaffInvitation(string to, string fullName, string activationUrl)
    {
        var html = $"""
            <p>Hola {WebUtility.HtmlEncode(fullName)},</p>
            <p>Te dieron acceso al panel de tu gimnasio en GSM.</p>
            <p><a href="{WebUtility.HtmlEncode(activationUrl)}">Activar mi cuenta</a></p>
            <p>El enlace vence en 72 horas. Si vence, usa "¿Olvidaste tu contraseña?" en la pantalla de entrada para pedir otro.</p>
            """;
        return new EmailMessage(to, "Tu acceso al panel del gimnasio", html,
            $"Te dieron acceso al panel. Activa tu cuenta (vence en 72 horas): {activationUrl}");
    }

    /// <summary>Acceso nuevo para quien ya tiene cuenta: el enlace lo lleva a entrar con su contraseña de siempre.</summary>
    public static EmailMessage StaffAccessGranted(string to, string fullName, string loginUrl)
    {
        var html = $"""
            <p>Hola {WebUtility.HtmlEncode(fullName)},</p>
            <p>Te dieron acceso al panel de un gimnasio más en GSM.</p>
            <p><a href="{WebUtility.HtmlEncode(loginUrl)}">Entrar</a> con tu email y tu contraseña de siempre: al iniciar sesión podrás elegir a qué gimnasio entrar.</p>
            """;
        return new EmailMessage(to, "Nuevo acceso al panel de un gimnasio", html,
            $"Te dieron acceso al panel de un gimnasio más. Entra con tu contraseña de siempre y elige el gimnasio: {loginUrl}");
    }

    /// <summary>Invitación al Owner de un gym nuevo (Gyms §4): el enlace de activación abre el formulario de contraseña.</summary>
    public static EmailMessage OwnerInvitation(string to, string fullName, string gymName, string activationUrl)
    {
        var html = $"""
            <p>Hola {WebUtility.HtmlEncode(fullName)},</p>
            <p>Bienvenido a GSM. Creamos el gimnasio <strong>{WebUtility.HtmlEncode(gymName)}</strong> y eres su Owner.</p>
            <p><a href="{WebUtility.HtmlEncode(activationUrl)}">Activar mi cuenta</a></p>
            <p>El enlace vence en 72 horas. Si vence, pide otro a quien te dio de alta o usa "¿Olvidaste tu contraseña?" en la pantalla de entrada.</p>
            """;
        return new EmailMessage(to, "Bienvenido a GSM, activa tu cuenta", html,
            $"Bienvenido a GSM. Eres el Owner de {gymName}. Activa tu cuenta (vence en 72 horas): {activationUrl}");
    }

    /// <summary>Owner de un gym nuevo que ya tenía cuenta: el enlace lo lleva a entrar con su contraseña de siempre.</summary>
    public static EmailMessage OwnerAccessGranted(string to, string fullName, string gymName, string loginUrl)
    {
        var html = $"""
            <p>Hola {WebUtility.HtmlEncode(fullName)},</p>
            <p>Ahora eres Owner del gimnasio <strong>{WebUtility.HtmlEncode(gymName)}</strong> en GSM.</p>
            <p><a href="{WebUtility.HtmlEncode(loginUrl)}">Entrar</a> con tu email y tu contraseña de siempre: al iniciar sesión podrás elegir a qué gimnasio entrar.</p>
            """;
        return new EmailMessage(to, "Nuevo gimnasio en GSM", html,
            $"Ahora eres Owner de {gymName}. Entra con tu contraseña de siempre y elige el gimnasio: {loginUrl}");
    }

    /// <summary>Aviso al dueño de la cuenta cuando 5 contraseñas incorrectas la bloquean 15 minutos.</summary>
    public static EmailMessage AccountLockedOut(string to, string fullName, int minutes)
    {
        var html = $"""
            <p>Hola {WebUtility.HtmlEncode(fullName)},</p>
            <p>Hubo 5 intentos fallidos de entrar a tu cuenta, así que la bloqueamos {minutes} minutos.</p>
            <p>Si no fuiste tú, cambia tu contraseña con "¿Olvidaste tu contraseña?" en la pantalla de entrada.</p>
            """;
        return new EmailMessage(to, "Intentos fallidos de entrar a tu cuenta", html,
            $"Hubo 5 intentos fallidos de entrar a tu cuenta; la bloqueamos {minutes} minutos. Si no fuiste tú, cambia tu contraseña.");
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

    /// <summary>
    /// Código de 6 dígitos para entrar sin contraseña (Identity §6). Marcado <c>Sensitive</c>: ningún sender
    /// lo vuelca al log.
    /// </summary>
    public static EmailMessage LoginCode(string to, string fullName, string code, string? gymName, int minutes)
    {
        var where = string.IsNullOrWhiteSpace(gymName) ? "GSM" : gymName;
        var html = $"""
            <p>Hola {WebUtility.HtmlEncode(fullName)},</p>
            <p>Tu código para entrar a <strong>{WebUtility.HtmlEncode(where)}</strong> es:</p>
            <p><strong style="font-size:28px;letter-spacing:6px">{code}</strong></p>
            <p>Vence en {minutes} minutos y sirve una sola vez. Si no lo pediste tú, ignora este correo.</p>
            """;
        return new EmailMessage(to, $"Tu código para entrar a {where}", html,
            $"Tu código para entrar a {where}: {code} (vence en {minutes} minutos, sirve una sola vez).",
            Sensitive: true);
    }

    /// <summary>Restablecer contraseña: el enlace (24 h) abre el formulario precargado.</summary>
    public static EmailMessage PasswordReset(string to, string resetUrl)
    {
        var body = $"""
            <p>Recibimos una solicitud para restablecer tu contraseña.</p>
            <p><a href="{WebUtility.HtmlEncode(resetUrl)}">Restablecer contraseña</a></p>
            <p>El enlace vence en 24 horas. Si no fuiste tú, ignora este correo.</p>
            """;
        return new EmailMessage(to, "Restablece tu contraseña", body,
            $"Restablece tu contraseña (el enlace vence en 24 horas): {resetUrl}");
    }
}
