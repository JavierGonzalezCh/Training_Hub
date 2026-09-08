using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Email;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Events;

namespace GSM.Application.Members;

/// <summary>Envía el código de invitación por email si el miembro tiene uno en la ficha.</summary>
public sealed class SendInvitationEmailHandler(IGymReader gyms, IEmailSender emailSender)
    : IDomainEventHandler<MemberInvited>
{
    public async Task HandleAsync(MemberInvited domainEvent, CancellationToken cancellationToken)
    {
        var branding = await gyms.GetBrandingAsync(domainEvent.GymId, cancellationToken);
        var gymName = branding?.Name ?? "tu gimnasio";

        await emailSender.SendAsync(
            EmailTemplates.Invitation(domainEvent.Email, gymName, domainEvent.Code), cancellationToken);
    }
}

/// <summary>Email de bienvenida al canjear la invitación.</summary>
public sealed class SendWelcomeEmailHandler(IGymReader gyms, IEmailSender emailSender)
    : IDomainEventHandler<MemberRegistered>
{
    public async Task HandleAsync(MemberRegistered domainEvent, CancellationToken cancellationToken)
    {
        var branding = await gyms.GetBrandingAsync(domainEvent.GymId, cancellationToken);
        var gymName = branding?.Name ?? "tu gimnasio";

        await emailSender.SendAsync(
            EmailTemplates.Welcome(domainEvent.Email, gymName, domainEvent.FullName), cancellationToken);
    }
}
