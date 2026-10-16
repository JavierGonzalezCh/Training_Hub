using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.IntegrationTests;

/// <summary>
/// El registro de entrada es idempotente durante <see cref="GSM.Application.Attendance.CheckInHandler.DuplicateWindow"/>
/// (A1b): un test que quiere probar el rechazo de una segunda entrada "más tarde el mismo día" corre hacia atrás las
/// entradas ya hechas, en vez de mover el reloj (que cambiaría el código del QR rotativo).
/// </summary>
internal static class CheckInTestClock
{
    public static async Task AgeEntriesAsync(this ApiFactory factory, Guid memberId, int minutes = 5)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<AttendanceEntity>()
            .Where(a => a.MemberId == memberId)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.CheckedInAt, a => a.CheckedInAt.AddMinutes(-minutes)));
    }
}
