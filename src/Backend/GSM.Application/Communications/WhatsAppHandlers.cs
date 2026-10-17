using System.Globalization;
using GSM.Application.Common.Abstractions;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

internal static class WhatsAppKinds
{
    public static bool IsValid(string? value)
        => Enum.TryParse<WhatsAppTemplateKind>(value, ignoreCase: true, out var kind) && Enum.IsDefined(kind);

    public static WhatsAppTemplateKind Parse(string? value)
        => IsValid(value)
            ? Enum.Parse<WhatsAppTemplateKind>(value!, ignoreCase: true)
            : throw new ValidationDomainException("Tipo de plantilla desconocido.");
}

public sealed class ListWhatsAppTemplatesHandler(IWhatsAppRepository whatsApp)
{
    public async Task<WhatsAppTemplatesResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var stored = (await whatsApp.ListTemplatesAsync(cancellationToken)).ToDictionary(t => t.Kind);
        var templates = Enum.GetValues<WhatsAppTemplateKind>()
            .Select(kind =>
            {
                var defaultText = WhatsAppTemplateDefaults.TextFor(kind);
                return stored.TryGetValue(kind, out var template)
                    ? new WhatsAppTemplateResponse(kind.ToString(), template.Text, defaultText, false)
                    : new WhatsAppTemplateResponse(kind.ToString(), defaultText, defaultText, true);
            })
            .ToList();
        return new WhatsAppTemplatesResponse(templates, WhatsAppTemplateDefaults.Variables);
    }
}

public sealed class UpdateWhatsAppTemplateHandler(
    ICurrentUserService currentUser,
    IWhatsAppRepository whatsApp,
    IUnitOfWork unitOfWork,
    ILogger<UpdateWhatsAppTemplateHandler> logger)
{
    public async Task<WhatsAppTemplateResponse> HandleAsync(
        string kindName, UpdateWhatsAppTemplateRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var kind = WhatsAppKinds.Parse(kindName);

        var template = await whatsApp.GetTemplateAsync(kind, cancellationToken);
        if (template is null)
        {
            whatsApp.Add(WhatsAppTemplate.Create(gymId, kind, request.Text));
        }
        else
        {
            template.SetText(request.Text);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Gym {GymId}: plantilla de WhatsApp {Kind} actualizada", gymId, kind);

        var saved = await whatsApp.GetTemplateAsync(kind, cancellationToken);
        return new WhatsAppTemplateResponse(
            kind.ToString(), saved!.Text, WhatsAppTemplateDefaults.TextFor(kind), false);
    }
}

/// <summary>
/// Arma el mensaje de WhatsApp para un miembro y una plantilla y devuelve el enlace <c>wa.me</c>. No envía nada:
/// el staff lo abre en su propio WhatsApp.
/// </summary>
public sealed class BuildWhatsAppMessageHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IWhatsAppRepository whatsApp,
    IMemberReader members,
    IMembershipReader memberships,
    IGymReader gyms,
    IMemberRegistrationLinks registrationLinks)
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task<WhatsAppMessageResponse> HandleAsync(
        Guid memberId, string kindName, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var kind = WhatsAppKinds.Parse(kindName);

        var member = await members.GetContactAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        var phones = await members.GetWhatsAppPhonesAsync([memberId], cancellationToken);
        if (!phones.TryGetValue(memberId, out var phone))
        {
            throw new ValidationDomainException("Falta el celular del miembro.");
        }

        var template = await whatsApp.GetTemplateAsync(kind, cancellationToken);
        var text = template?.Text ?? WhatsAppTemplateDefaults.TextFor(kind);

        var gymName = (await gyms.GetBrandingAsync(gymId, cancellationToken))?.Name ?? string.Empty;
        var endsOn = await memberships.GetLatestEndDateAsync(memberId, cancellationToken);
        string? graceUntil = null;
        if (endsOn is { } end)
        {
            var settings = await memberships.GetSettingsAsync(gymId, cancellationToken);
            graceUntil = Day(end.AddDays(settings.GraceDays));
        }
        else if (kind is WhatsAppTemplateKind.ExpiringSoon or WhatsAppTemplateKind.Expired)
        {
            throw new ConflictException("El miembro no tiene una membresía con fecha de vencimiento.");
        }

        var link = string.Empty;
        if (kind == WhatsAppTemplateKind.AppInvitation)
        {
            var invitation = await members.GetPendingInvitationAsync(memberId, cancellationToken);
            if (invitation is null || invitation.ExpiresAt <= clock.UtcNow)
            {
                throw new ConflictException("Genera primero la invitación del miembro: no tiene una vigente.");
            }

            // El slug va en el enlace para que la pantalla de registro muestre la marca del gym (I5-4).
            var slug = (await gyms.GetAdminViewAsync(gymId, cancellationToken))?.Slug;
            link = registrationLinks.RegistrationUrl(invitation.Code, slug);
        }

        var message = text
            .Replace("{nombre}", member.FirstName, StringComparison.Ordinal)
            .Replace("{gym}", gymName, StringComparison.Ordinal)
            .Replace("{vence}", endsOn is { } e ? Day(e) : string.Empty, StringComparison.Ordinal)
            .Replace("{gracia_hasta}", graceUntil ?? string.Empty, StringComparison.Ordinal)
            .Replace("{enlace}", link, StringComparison.Ordinal);

        var url = $"https://wa.me/{phone.TrimStart('+')}?text={Uri.EscapeDataString(message)}";
        return new WhatsAppMessageResponse(url, message, phone);
    }

    private static string Day(DateOnly day) => day.ToString("d 'de' MMMM 'de' yyyy", Colombia);
}

public sealed class RegisterWhatsAppContactHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IWhatsAppRepository whatsApp,
    IMemberReader members,
    IIdentityReader identity,
    IUnitOfWork unitOfWork,
    ILogger<RegisterWhatsAppContactHandler> logger)
{
    public async Task<WhatsAppContactResponse> HandleAsync(
        RegisterWhatsAppContactRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new ForbiddenDomainException("Sin sesión.");
        var kind = WhatsAppKinds.Parse(request.Kind);

        if (!await members.ExistsAsync(request.MemberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var contact = WhatsAppContact.Register(gymId, request.MemberId, kind, userId, clock.UtcNow);
        whatsApp.Add(contact);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Miembro {MemberId}: contacto por WhatsApp registrado ({Kind})", request.MemberId, kind);

        var name = (await identity.GetUserAsync(userId, cancellationToken))?.FullName ?? string.Empty;
        return new WhatsAppContactResponse(request.MemberId, kind.ToString(), userId, name, contact.ContactedAt);
    }
}

/// <summary>Para varios miembros a la vez: ¿tiene celular válido? y ¿quién lo contactó por última vez?</summary>
public sealed class GetWhatsAppStatesHandler(
    IWhatsAppRepository whatsApp,
    IMemberReader members,
    IIdentityReader identity)
{
    private const int MaxMembers = 200;

    public async Task<IReadOnlyList<WhatsAppMemberStateResponse>> HandleAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken)
    {
        var ids = memberIds.Distinct().Take(MaxMembers).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var phones = await members.GetWhatsAppPhonesAsync(ids, cancellationToken);
        var contacts = await whatsApp.GetLastContactsAsync(ids, cancellationToken);

        var names = new Dictionary<Guid, string>();
        foreach (var userId in contacts.Values.Select(c => c.ContactedByUserId).Distinct())
        {
            names[userId] = (await identity.GetUserAsync(userId, cancellationToken))?.FullName ?? string.Empty;
        }

        return ids
            .Select(id => new WhatsAppMemberStateResponse(
                id,
                phones.ContainsKey(id),
                contacts.TryGetValue(id, out var c)
                    ? new WhatsAppContactResponse(id, c.Kind.ToString(), c.ContactedByUserId, names[c.ContactedByUserId], c.ContactedAt)
                    : null))
            .ToList();
    }
}
