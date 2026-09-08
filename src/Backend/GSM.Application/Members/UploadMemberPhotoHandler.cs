using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Members;

public sealed class UploadMemberPhotoHandler(
    ICurrentUserService currentUser,
    IMemberRepository members,
    IFileStorage storage,
    IUnitOfWork unitOfWork)
{
    public async Task<MemberDetail> HandleAsync(
        Guid memberId, FileToStore file, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        ImageUploadRules.EnsureValid(file);

        var member = await members.GetByIdAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        var previous = member.PhotoUrl;
        var url = await storage.SaveAsync(file, "member-photos", cancellationToken);

        member.SetPhoto(url);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previous))
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return MemberMapper.ToDetail(member);
    }
}
