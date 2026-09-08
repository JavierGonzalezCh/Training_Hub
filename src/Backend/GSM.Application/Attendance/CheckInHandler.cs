using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Application.Attendance;

public sealed class CheckInHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMembershipReader memberships,
    IAchievementService achievements,
    IAttendanceRepository attendance,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CheckInHandler> logger)
{
    public async Task<(CheckInResponse Response, bool Created)> HandleAsync(
        CheckInRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var member = await ResolveMemberAsync(request, cancellationToken);

        if (member.Status != MemberStatus.Active)
        {
            throw new ConflictException("El miembro está inactivo.");
        }

        var method = currentUser.Role == UserRole.Member
            ? AttendanceMethod.Qr
            : request.Method ?? AttendanceMethod.Manual;

        var today = clock.Today;
        var existing = await attendance.GetForMemberOnDateAsync(member.Id, today, cancellationToken);
        var created = existing is null;

        AttendanceEntity record;
        if (existing is not null)
        {
            record = existing;
        }
        else
        {
            record = AttendanceEntity.Create(gymId, member.Id, method, clock.UtcNow, today);
            attendance.Add(record);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await events.PublishAsync(new CheckInRegistered(record.Id, gymId, member.Id), cancellationToken);
            logger.LogInformation("Check-in {AttendanceId} de miembro {MemberId}", record.Id, member.Id);
        }

        var membershipExpired = !await memberships.IsActiveAsync(member.Id, cancellationToken);
        var unlocked = await achievements.EvaluateForMemberAsync(member.Id, cancellationToken);

        var response = new CheckInResponse(
            record.Id,
            member.Id,
            $"{member.FirstName} {member.LastName}",
            record.CheckedInAt,
            record.Method.ToString(),
            AlreadyCheckedInToday: !created,
            membershipExpired,
            unlocked);

        return (response, created);
    }

    private async Task<MemberInfo> ResolveMemberAsync(CheckInRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.Role == UserRole.Member)
        {
            var userId = currentUser.UserId
                ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
            return await members.ResolveByUserIdAsync(userId, cancellationToken)
                ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");
        }

        if (!string.IsNullOrWhiteSpace(request.QrCode))
        {
            return await members.ResolveByQrAsync(request.QrCode.Trim(), cancellationToken)
                ?? throw new NotFoundException("QR no reconocido.");
        }

        if (request.MemberId is { } memberId)
        {
            return await members.GetAsync(memberId, cancellationToken)
                ?? throw new NotFoundException("Miembro no encontrado.");
        }

        throw new ValidationDomainException("Indica un qrCode o un memberId.");
    }
}
