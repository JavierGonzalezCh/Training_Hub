using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Communications;

/// <summary>Para qué se contacta a un miembro por WhatsApp (una plantilla por tipo y gym).</summary>
public enum WhatsAppTemplateKind
{
    ExpiringSoon,
    Expired,
    AppInvitation,
    Birthday,
}

/// <summary>Textos por defecto de las plantillas (§B) y variables que admiten.</summary>
public static class WhatsAppTemplateDefaults
{
    public const int MaxLength = 1000;

    public static readonly IReadOnlyList<string> Variables =
        ["{nombre}", "{gym}", "{vence}", "{gracia_hasta}", "{enlace}"];

    public static string TextFor(WhatsAppTemplateKind kind) => kind switch
    {
        WhatsAppTemplateKind.ExpiringSoon =>
            "Hola {nombre}, tu plan en {gym} vence el {vence}. ¿Te lo renovamos?",
        WhatsAppTemplateKind.Expired =>
            "Hola {nombre}, tu plan venció el {vence}. Si renuevas antes del {gracia_hasta} conservas tu fecha.",
        WhatsAppTemplateKind.AppInvitation =>
            "Hola {nombre}, activa tu cuenta de {gym}: {enlace}",
        WhatsAppTemplateKind.Birthday =>
            "¡Feliz cumpleaños, {nombre}! Hoy tu entrenamiento va por cuenta de la casa 🎉",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>Plantilla de WhatsApp editada por el gym. Sin fila vale el texto por defecto.</summary>
public sealed class WhatsAppTemplate : BaseEntity, ITenantEntity
{
    private WhatsAppTemplate()
    {
    }

    public Guid GymId { get; private set; }

    public WhatsAppTemplateKind Kind { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public static WhatsAppTemplate Create(Guid gymId, WhatsAppTemplateKind kind, string text)
    {
        var template = new WhatsAppTemplate { GymId = gymId, Kind = kind };
        template.SetText(text);
        return template;
    }

    public void SetText(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ValidationDomainException("El texto de la plantilla no puede estar vacío.");
        }

        if (trimmed.Length > WhatsAppTemplateDefaults.MaxLength)
        {
            throw new ValidationDomainException(
                $"El texto de la plantilla admite hasta {WhatsAppTemplateDefaults.MaxLength} caracteres.");
        }

        Text = trimmed;
    }
}

/// <summary>
/// Registro del clic en el botón de WhatsApp: quién del equipo contactó a qué miembro, con qué plantilla y
/// cuándo. El sistema no sabe si el mensaje realmente se envió.
/// </summary>
public sealed class WhatsAppContact : BaseEntity, ITenantEntity
{
    private WhatsAppContact()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public WhatsAppTemplateKind Kind { get; private set; }

    public Guid ContactedByUserId { get; private set; }

    public DateTimeOffset ContactedAt { get; private set; }

    public static WhatsAppContact Register(
        Guid gymId, Guid memberId, WhatsAppTemplateKind kind, Guid contactedByUserId, DateTimeOffset contactedAt)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || contactedByUserId == Guid.Empty)
        {
            throw new ValidationDomainException("Contacto inválido.");
        }

        return new WhatsAppContact
        {
            GymId = gymId,
            MemberId = memberId,
            Kind = kind,
            ContactedByUserId = contactedByUserId,
            ContactedAt = contactedAt,
        };
    }
}
