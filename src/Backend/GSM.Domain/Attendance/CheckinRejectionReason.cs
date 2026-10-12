namespace GSM.Domain.Attendance;

/// <summary>
/// Motivos por los que se rechaza una entrada (Attendance v2, §3). <see cref="GymClosed"/> no deja
/// intento guardado: no es accionable. Los valores se persisten en snake_case.
/// </summary>
public enum CheckinRejectionReason
{
    MembershipExpired,
    NoMembership,
    GymClosed,
    MembershipFrozen,
    PassExhausted,
    MemberInactive,

    /// <summary>Ya registró su entrada hoy (una por día). No suena en la campana ni genera push.</summary>
    AlreadyCheckedIn,
}
