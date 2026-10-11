using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

// --- Textos del gym (solo Owner: lo impone el controller) ---

public sealed class ListConsentDocumentsHandler(ICurrentUserService currentUser, ConsentService consents)
{
    public async Task<IReadOnlyList<ConsentDocumentItem>> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var items = new List<ConsentDocumentItem>();
        foreach (var type in Enum.GetValues<ConsentType>())
        {
            items.Add(await consents.GetCurrentDocumentAsync(type, cancellationToken));
        }

        return items;
    }
}

/// <summary>
/// Publica una versión nueva del texto. No toca las aceptaciones anteriores: quien aceptó la versión
/// previa queda "pendiente de aceptar la vigente".
/// </summary>
public sealed class PublishConsentDocumentHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IConsentRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<PublishConsentDocumentHandler> logger)
{
    public async Task<ConsentDocumentItem> HandleAsync(
        PublishConsentDocumentRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var type = ConsentService.ParseType(request.Type);

        var latest = await repository.GetLatestDocumentAsync(type, cancellationToken);
        var document = ConsentDocument.Publish(
            gymId, type, (latest?.Version ?? ConsentDocument.BaseVersion) + 1, request.Text, clock.UtcNow, currentUser.UserId);
        repository.Add(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Texto de autorización {Type} publicado en la versión {Version}", type, document.Version);
        return new ConsentDocumentItem(
            EnumText.Snake(type)!, document.Version, document.Text, document.EffectiveFrom, IsDefault: false);
    }
}

// --- Staff sobre la ficha de un miembro ---

public sealed class ListMemberConsentsHandler(
    ICurrentUserService currentUser, IMemberRepository members, ConsentService consents)
{
    public async Task<IReadOnlyList<MemberConsentStatus>> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        _ = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");
        return await consents.GetStatusesAsync(memberId, cancellationToken);
    }
}

/// <summary>
/// El staff registra una autorización firmada en papel (canal <c>front_desk</c>). Si el miembro es
/// menor de edad, la firmó su acudiente.
/// </summary>
public sealed class RegisterMemberConsentHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberRepository members,
    ConsentService consents,
    IUnitOfWork unitOfWork,
    ILogger<RegisterMemberConsentHandler> logger)
{
    public async Task<IReadOnlyList<MemberConsentStatus>> HandleAsync(
        Guid memberId, AcceptConsentRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var type = ConsentService.ParseType(request.Type);
        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        await consents.AcceptAsync(
            member, type, request.DocumentVersion, ConsentChannel.FrontDesk,
            byGuardian: member.IsMinor(clock.Today), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Autorización {Type} del miembro {MemberId} registrada en recepción", type, memberId);
        return await consents.GetStatusesAsync(memberId, cancellationToken);
    }
}

/// <summary>Revoca la autorización de datos a pedido del titular (derecho de la Ley 1581), registrada por el staff.</summary>
public sealed class RevokeMemberConsentHandler(
    ICurrentUserService currentUser,
    IMemberRepository members,
    ConsentService consents,
    IUnitOfWork unitOfWork,
    ILogger<RevokeMemberConsentHandler> logger)
{
    public async Task<IReadOnlyList<MemberConsentStatus>> HandleAsync(
        Guid memberId, string typeText, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var type = ConsentService.ParseType(typeText);
        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        await consents.RevokeAsync(member, type, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Autorización {Type} del miembro {MemberId} revocada por el staff", type, memberId);
        return await consents.GetStatusesAsync(memberId, cancellationToken);
    }
}

// --- El propio miembro (app) ---

/// <summary>Resuelve la ficha del miembro autenticado y rechaza a los menores en las acciones que firman.</summary>
internal static class MyMember
{
    public static async Task<Member> ResolveAsync(
        ICurrentUserService currentUser, IMemberReader reader, IMemberRepository members, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
        var info = await reader.ResolveByUserIdAsync(userId, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");
        return await members.GetByIdAsync(info.Id, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");
    }
}

public sealed class GetMyConsentsHandler(
    ICurrentUserService currentUser, IMemberReader reader, IMemberRepository members, ConsentService consents)
{
    public async Task<IReadOnlyList<MemberConsentStatus>> HandleAsync(CancellationToken cancellationToken)
    {
        var member = await MyMember.ResolveAsync(currentUser, reader, members, cancellationToken);
        return await consents.GetStatusesAsync(member.Id, cancellationToken);
    }
}

/// <summary>
/// El miembro acepta en la app la versión vigente. Un menor de edad no puede aceptar por sí mismo
/// (403): la acepta su acudiente en recepción, donde el staff la registra con
/// <see cref="RegisterMemberConsentHandler"/>.
/// </summary>
public sealed class AcceptMyConsentHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader reader,
    IMemberRepository members,
    ConsentService consents,
    IUnitOfWork unitOfWork,
    ILogger<AcceptMyConsentHandler> logger)
{
    public async Task<IReadOnlyList<MemberConsentStatus>> HandleAsync(
        AcceptConsentRequest request, CancellationToken cancellationToken)
    {
        var type = ConsentService.ParseType(request.Type);
        var member = await MyMember.ResolveAsync(currentUser, reader, members, cancellationToken);

        if (member.IsMinor(clock.Today))
        {
            throw new ForbiddenDomainException(
                "Eres menor de edad: tu acudiente debe aceptar la autorización en recepción.");
        }

        await consents.AcceptAsync(
            member, type, request.DocumentVersion, ConsentChannel.App, byGuardian: false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Autorización {Type} aceptada en la app por el miembro {MemberId}", type, member.Id);
        return await consents.GetStatusesAsync(member.Id, cancellationToken);
    }
}

public sealed class RevokeMyConsentHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader reader,
    IMemberRepository members,
    ConsentService consents,
    IUnitOfWork unitOfWork,
    ILogger<RevokeMyConsentHandler> logger)
{
    public async Task<IReadOnlyList<MemberConsentStatus>> HandleAsync(string typeText, CancellationToken cancellationToken)
    {
        var type = ConsentService.ParseType(typeText);
        var member = await MyMember.ResolveAsync(currentUser, reader, members, cancellationToken);

        if (member.IsMinor(clock.Today))
        {
            throw new ForbiddenDomainException(
                "Eres menor de edad: tu acudiente debe pedir la revocación en recepción.");
        }

        await consents.RevokeAsync(member, type, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Autorización {Type} revocada por el miembro {MemberId}", type, member.Id);
        return await consents.GetStatusesAsync(member.Id, cancellationToken);
    }
}
