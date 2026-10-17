using GSM.Application.Common.Abstractions;
using GSM.Domain.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Persistence;

public sealed class UnitOfWork(GsmDbContext dbContext) : IUnitOfWork
{
    /// <summary>Índice único de la caja abierta por gym (<c>payments.cash_shifts</c>).</summary>
    internal const string OpenCashShiftIndex = "ux_cash_shifts_open_gym";

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } pg
            && pg.ConstraintName == OpenCashShiftIndex)
        {
            // P3-6: dos aperturas simultáneas; la segunda choca con el índice único y no es un error del servidor.
            throw new ConflictException("Ya hay una caja abierta.");
        }
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
