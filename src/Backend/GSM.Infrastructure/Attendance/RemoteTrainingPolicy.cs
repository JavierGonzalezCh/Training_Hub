using GSM.Application.Attendance.Public;
using GSM.Application.Routines.Abstractions;

namespace GSM.Infrastructure.Attendance;

/// <summary>Attendance responde al puerto de Routines con su ajuste <c>RemoteTraining</c> (perezoso: sin fila, apagado).</summary>
public sealed class RemoteTrainingPolicy(IAttendanceReader attendance) : IRemoteTrainingPolicy
{
    public Task<bool> IsRemoteTrainingAsync(Guid gymId, CancellationToken cancellationToken = default)
        => attendance.IsRemoteTrainingAsync(gymId, cancellationToken);
}
