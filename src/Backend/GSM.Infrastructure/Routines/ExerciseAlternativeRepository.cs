using GSM.Application.Common.Abstractions;
using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

public sealed class ExerciseAlternativeRepository(GsmDbContext dbContext, ICurrentUserService currentUser)
    : IExerciseAlternativeRepository
{
    private DbSet<ExerciseAlternative> Rows => dbContext.Set<ExerciseAlternative>();

    public async Task<IReadOnlyList<ExerciseAlternative>> ListForAsync(
        Guid exerciseId, CancellationToken cancellationToken = default)
    {
        var gymId = currentUser.GymId;
        return await Rows
            .Where(a => (a.GymId == null || a.GymId == gymId)
                        && (a.ExerciseId == exerciseId || a.AlternativeId == exerciseId))
            .ToListAsync(cancellationToken);
    }

    public Task<ExerciseAlternative?> GetAsync(
        Guid? gymId, Guid first, Guid second, CancellationToken cancellationToken = default)
    {
        var (low, high) = ExerciseAlternative.Order(first, second);
        return Rows.FirstOrDefaultAsync(
            a => a.GymId == gymId && a.ExerciseId == low && a.AlternativeId == high, cancellationToken);
    }

    public void Add(ExerciseAlternative alternative) => Rows.Add(alternative);

    public void Remove(ExerciseAlternative alternative) => Rows.Remove(alternative);
}
