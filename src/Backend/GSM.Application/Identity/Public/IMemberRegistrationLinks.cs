namespace GSM.Application.Identity.Public;

/// <summary>
/// Enlace público de registro del miembro (<c>/app/registro?code=…</c>) sobre la dirección pública del
/// panel (<c>Identity:PublicWebBaseUrl</c>). Lo consume Comunicaciones para el mensaje de invitación por WhatsApp.
/// </summary>
public interface IMemberRegistrationLinks
{
    /// <param name="gymSlug">Si viene, se añade <c>&amp;gym=&lt;slug&gt;</c> para que la pantalla pinte la marca del gym desde la primera vez (I5-4).</param>
    string RegistrationUrl(string code, string? gymSlug = null);
}
