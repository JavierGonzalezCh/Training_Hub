using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

public sealed class ListClassesHandler(ICurrentUserService currentUser, IClassRepository classes)
{
    public async Task<IReadOnlyList<ClassListItem>> HandleAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var list = await classes.ListAsync(new ClassFilter(includeInactive), cancellationToken);
        return list.Select(ClassMapper.ToListItem).ToList();
    }
}

public sealed class GetClassHandler(ICurrentUserService currentUser, IClassRepository classes)
{
    public async Task<ClassResponse> HandleAsync(Guid classId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        return ClassMapper.ToResponse(gymClass);
    }
}

public sealed class CreateClassHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IClassRepository classes,
    IUnitOfWork unitOfWork,
    ILogger<CreateClassHandler> logger)
{
    public async Task<ClassResponse> HandleAsync(SaveClassRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gymClass = GymClass.Create(gymId, clock.Today, ClassMapper.ToDraft(request));

        classes.Add(gymClass);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Clase {ClassId} creada en el gym {GymId} (recurrente: {IsRecurring})",
            gymClass.Id, gymId, gymClass.IsRecurring);

        return ClassMapper.ToResponse(gymClass);
    }
}

public sealed class UpdateClassHandler(
    ICurrentUserService currentUser,
    IClassRepository classes,
    IUnitOfWork unitOfWork,
    ILogger<UpdateClassHandler> logger)
{
    public async Task<ClassResponse> HandleAsync(
        Guid classId, SaveClassRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        gymClass.Update(ClassMapper.ToDraft(request));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clase {ClassId} actualizada", gymClass.Id);
        return ClassMapper.ToResponse(gymClass);
    }
}

public sealed class DeactivateClassHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IClassRepository classes,
    IUnitOfWork unitOfWork,
    ILogger<DeactivateClassHandler> logger)
{
    public async Task HandleAsync(Guid classId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        if (!gymClass.IsActive)
        {
            return;
        }

        gymClass.Deactivate(clock.Today);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clase {ClassId} dada de baja el {Date}", gymClass.Id, gymClass.DeactivatedOn);
    }
}
