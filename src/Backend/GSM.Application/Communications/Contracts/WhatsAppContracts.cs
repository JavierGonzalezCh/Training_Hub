namespace GSM.Application.Communications.Contracts;

/// <param name="Kind"><c>ExpiringSoon</c>, <c>Expired</c>, <c>AppInvitation</c> o <c>Birthday</c> (texto, no entero).</param>
/// <param name="Text">El texto vigente: el del gym o, sin fila, el de <paramref name="DefaultText"/>.</param>
/// <param name="IsDefault"><c>true</c> si el gym no la ha editado.</param>
public sealed record WhatsAppTemplateResponse(string Kind, string Text, string DefaultText, bool IsDefault);

/// <summary>Las plantillas del gym y las variables que admiten (<c>{nombre}</c>, <c>{gym}</c>…).</summary>
public sealed record WhatsAppTemplatesResponse(
    IReadOnlyList<WhatsAppTemplateResponse> Templates, IReadOnlyList<string> Variables);

public sealed record UpdateWhatsAppTemplateRequest(string Text);

/// <param name="Url">Enlace <c>wa.me/&lt;número&gt;?text=…</c> ya codificado.</param>
/// <param name="Message">El texto ya resuelto, para mostrarlo o editarlo antes de enviarlo.</param>
/// <param name="Phone">El celular en E.164.</param>
public sealed record WhatsAppMessageResponse(string Url, string Message, string Phone);

public sealed record RegisterWhatsAppContactRequest(Guid MemberId, string Kind);

/// <summary>Último contacto por WhatsApp. El sistema no sabe si el mensaje realmente se envió.</summary>
public sealed record WhatsAppContactResponse(
    Guid MemberId, string Kind, Guid ContactedByUserId, string ContactedByName, DateTimeOffset ContactedAt);

/// <summary>Estado de WhatsApp de un miembro para pintar el botón y el "contactó hace…".</summary>
/// <param name="HasValidPhone">Sin celular válido el botón va deshabilitado ("Falta el celular").</param>
public sealed record WhatsAppMemberStateResponse(
    Guid MemberId, bool HasValidPhone, WhatsAppContactResponse? LastContact);
