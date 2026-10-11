namespace GSM.Application.Identity.Public;

/// <summary>
/// Enlace público de registro del miembro (<c>/app/registro?code=…</c>) sobre la dirección pública del
/// panel (<c>Identity:PublicWebBaseUrl</c>). Lo consume Comunicaciones para el mensaje de invitación por WhatsApp.
/// </summary>
public interface IMemberRegistrationLinks
{
    string RegistrationUrl(string code);
}
