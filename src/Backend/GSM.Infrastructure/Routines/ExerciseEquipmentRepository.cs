using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

public sealed class ExerciseEquipmentRepository(GsmDbContext dbContext) : IExerciseEquipmentRepository
{
    private DbSet<ExerciseEquipmentModel> ModelLinks => dbContext.Set<ExerciseEquipmentModel>();

    private DbSet<ExerciseEquipmentItem> ItemLinks => dbContext.Set<ExerciseEquipmentItem>();

    public async Task<IReadOnlyList<ExerciseEquipmentModel>> ListModelLinksAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default)
        => exerciseIds.Count == 0
            ? []
            : await ModelLinks.AsNoTracking()
                .Where(l => exerciseIds.Contains(l.ExerciseId))
                .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ExerciseEquipmentItem>> ListItemLinksAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default)
        => exerciseIds.Count == 0
            ? []
            : await ItemLinks.AsNoTracking()
                .Where(l => exerciseIds.Contains(l.ExerciseId))
                .ToListAsync(cancellationToken);

    public async Task ReplaceModelLinksAsync(
        Guid exerciseId, IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken = default)
    {
        var wanted = modelIds.ToHashSet();
        var existing = await ModelLinks.Where(l => l.ExerciseId == exerciseId).ToListAsync(cancellationToken);

        ModelLinks.RemoveRange(existing.Where(l => !wanted.Contains(l.ModelId)));
        var present = existing.Select(l => l.ModelId).ToHashSet();
        ModelLinks.AddRange(wanted.Where(id => !present.Contains(id))
            .Select(id => ExerciseEquipmentModel.Create(exerciseId, id)));
    }

    public async Task ReplaceItemLinksAsync(
        Guid gymId, Guid exerciseId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        var wanted = itemIds.ToHashSet();
        var existing = await ItemLinks.Where(l => l.ExerciseId == exerciseId).ToListAsync(cancellationToken);

        ItemLinks.RemoveRange(existing.Where(l => !wanted.Contains(l.EquipmentItemId)));
        var present = existing.Select(l => l.EquipmentItemId).ToHashSet();
        ItemLinks.AddRange(wanted.Where(id => !present.Contains(id))
            .Select(id => ExerciseEquipmentItem.Create(gymId, exerciseId, id)));
    }
}
