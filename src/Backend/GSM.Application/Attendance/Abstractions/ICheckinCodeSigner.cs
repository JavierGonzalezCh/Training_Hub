using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Abstractions;

public static class CheckinCodeWindow
{
    /// <summary>Duración de cada código rotativo.</summary>
    public const int Seconds = 30;
}

public interface ICheckinCodeSigner
{
    string Generate(Guid gymId, CheckinMode mode, int staticVersion, DateTimeOffset now);

    /// <summary>Rotativo: ventana actual o la anterior. Fijo: solo la versión vigente.</summary>
    bool Validate(Guid gymId, string? code, CheckinMode mode, int staticVersion, DateTimeOffset now);
}
