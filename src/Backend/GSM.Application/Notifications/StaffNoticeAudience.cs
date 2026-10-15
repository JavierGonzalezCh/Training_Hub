using GSM.Application.Common.Abstractions;
using GSM.Domain.Identity;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>
/// N1b: a quién le llega cada aviso de acción del staff. Un aviso puede exigir permisos
/// (<see cref="Notification.RequiredPermissions"/>): lo ve quien tenga alguno; el Owner y el SuperAdmin
/// impersonando los tienen todos. Sin requisitos, lo ve todo el staff.
/// </summary>
public static class StaffNoticeAudience
{
    /// <summary>
    /// Solicitud de anulación: quien puede resolverla (<c>VoidPayments</c>) y quien solo la ve (<c>ViewFinances</c>).
    /// </summary>
    public static readonly IReadOnlyCollection<StaffPermission> VoidRequest =
        [StaffPermission.VoidPayments, StaffPermission.ViewFinances];

    /// <summary>Nombres de los permisos que tiene quien consulta (lo que usa la consulta de la campana).</summary>
    public static IReadOnlyCollection<string> Held(ICurrentUserService currentUser)
        => StaffPermissions.All.Where(currentUser.HasPermission).Select(p => p.ToString()).ToArray();

    public static IReadOnlyCollection<StaffPermission> Parse(IEnumerable<string> names)
        => names.Select(n => Enum.TryParse<StaffPermission>(n, out var p) ? (StaffPermission?)p : null)
            .OfType<StaffPermission>().ToArray();

    public static bool CanSee(Notification notice, IReadOnlyCollection<string> held)
        => notice.RequiredPermissions is not { Length: > 0 } required || required.Any(held.Contains);

    /// <summary>
    /// Atender a mano un aviso exige verlo; y el de anulación, además, poder resolverla (<c>VoidPayments</c>):
    /// quien solo ve las finanzas no cierra el aviso sin que la solicitud esté resuelta.
    /// </summary>
    public static bool CanResolve(Notification notice, IReadOnlyCollection<string> held)
        => CanSee(notice, held)
           && (notice.Type != NotificationType.VoidRequested || held.Contains(nameof(StaffPermission.VoidPayments)));
}
