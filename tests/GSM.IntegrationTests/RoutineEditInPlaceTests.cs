using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Routines;
using GSM.Infrastructure.Migrations;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>R1: la edición de una rutina es en el lugar y el historial de entrenamiento nunca se pierde (bug B1).</summary>
public sealed class RoutineEditInPlaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day = new(2026, 6, 10);

    [Fact]
    public async Task B1_renaming_a_routine_keeps_every_training_log()
    {
        var w = await SeedAsync();
        await LogAsync(w, w.Lines[0]);
        await LogAsync(w, w.Lines[1]);

        var updated = await UpdateAsync(w, Request(w, name: "Nombre corregido"));

        Assert.Equal("Nombre corregido", updated.Name);
        Assert.Equal(2, await CountLogsAsync(w));
        Assert.Equal(w.Lines, updated.Days.Single(d => !d.IsRest).Exercises.Select(e => e.Id));
    }

    [Fact]
    public async Task Editing_an_existing_line_keeps_its_id_and_its_logs()
    {
        var w = await SeedAsync();
        await LogAsync(w, w.Lines[0]);

        var request = Request(w, mutateLine: (i, line) => i == 0 ? line with { TargetSets = 5, TargetReps = "6-8" } : line);
        var updated = await UpdateAsync(w, request);

        var first = updated.Days.Single(d => !d.IsRest).Exercises.First();
        Assert.Equal(w.Lines[0], first.Id);
        Assert.Equal(5, first.TargetSets);
        Assert.Equal("6-8", first.TargetReps);
        Assert.Equal(1, await CountLogsAsync(w));
    }

    [Fact]
    public async Task A_line_missing_from_the_body_is_retired_not_deleted_and_its_logs_stay()
    {
        var w = await SeedAsync();
        await LogAsync(w, w.Lines[2]);

        var updated = await UpdateAsync(w, Request(w, dropLine: 2));

        Assert.Equal(2, updated.Days.Single(d => !d.IsRest).Exercises.Count);
        Assert.DoesNotContain(updated.Days.SelectMany(d => d.Exercises), e => e.Id == w.Lines[2]);

        using (var scope = AsStaff(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var line = await db.Set<RoutineExercise>().SingleAsync(e => e.Id == w.Lines[2]);
            Assert.Equal(Day, line.RetiredOn);
            Assert.Equal(1, await db.Set<RoutineExerciseLog>().CountAsync(l => l.RoutineExerciseId == w.Lines[2]));

            var read = await scope.ServiceProvider.GetRequiredService<GetRoutineHandler>().HandleAsync(w.RoutineId, default);
            Assert.DoesNotContain(read.Days.SelectMany(d => d.Exercises), e => e.Id == w.Lines[2]);
        }

        using (var scope = AsMember(w))
        {
            var mine = await scope.ServiceProvider.GetRequiredService<GetMyRoutineHandler>().HandleAsync(default);
            Assert.DoesNotContain(mine.Routine!.Days.SelectMany(d => d.Exercises), e => e.Id == w.Lines[2]);

            // Registrar sobre una línea retirada ya no procede.
            await Assert.ThrowsAsync<ConflictException>(() =>
                scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>().HandleAsync(
                    w.Lines[2], new LogRoutineExerciseRequest(null, null, null, null), default));
        }
    }

    [Fact]
    public async Task A_line_without_id_is_created()
    {
        var w = await SeedAsync();

        var request = Request(w, addLine: new RoutineExerciseInput(w.CatalogIds[3], 2, "15", null, 30, null));
        var updated = await UpdateAsync(w, request);

        var lines = updated.Days.Single(d => !d.IsRest).Exercises;
        Assert.Equal(4, lines.Count);
        Assert.Equal(w.Lines, lines.Take(3).Select(e => e.Id));
        Assert.DoesNotContain(lines[3].Id, w.Lines);
        Assert.Equal(w.CatalogIds[3], lines[3].ExerciseId);
    }

    [Fact]
    public async Task Days_are_kept_created_and_retired_the_same_way()
    {
        var w = await SeedAsync();
        await LogAsync(w, w.Lines[0]);

        // Se conserva el día de entrenamiento, se retira el de descanso y se crea uno nuevo.
        var request = new SaveRoutineRequest("Rutina", null,
        [
            new RoutineDayInput("D1", false, w.Lines.Select((id, i) =>
                new RoutineExerciseInput(w.CatalogIds[i], 3, "10", null, 60, null, id)).ToList(), w.TrainingDayId),
            new RoutineDayInput("Nuevo", false, [new RoutineExerciseInput(w.CatalogIds[3], 3, "10", null, 60, null)]),
        ]);
        var updated = await UpdateAsync(w, request);

        Assert.Equal(2, updated.CycleLength);
        Assert.Equal(w.TrainingDayId, updated.Days[0].Id);
        Assert.NotEqual(w.RestDayId, updated.Days[1].Id);
        Assert.DoesNotContain(updated.Days, d => d.Id == w.RestDayId);
        Assert.Equal([1, 2], updated.Days.Select(d => d.Position));
        Assert.Equal(1, await CountLogsAsync(w));

        using var scope = AsStaff(w);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var rest = await db.Set<RoutineDay>().SingleAsync(d => d.Id == w.RestDayId);
        Assert.Equal(Day, rest.RetiredOn);

        // Retirar el día con registros retira también sus líneas, sin tocar los registros.
        var onlyNew = new SaveRoutineRequest("Rutina", null,
            [new RoutineDayInput("Nuevo", false, [new RoutineExerciseInput(w.CatalogIds[3], 3, "10", null, 60, null)],
                updated.Days[1].Id)]);
        // El contexto de este scope ya cargó el día y sus líneas; se edita desde uno limpio.
        var again = await UpdateAsync(w, onlyNew);
        Assert.Equal(1, again.CycleLength);

        using var check = AsStaff(w);
        var db2 = check.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(1, await db2.Set<RoutineExerciseLog>().CountAsync(l => l.RoutineExerciseId == w.Lines[0]));
        Assert.Equal(Day, (await db2.Set<RoutineExercise>().SingleAsync(e => e.Id == w.Lines[0])).RetiredOn);
        Assert.Equal(Day, (await db2.Set<RoutineDay>().SingleAsync(d => d.Id == w.TrainingDayId)).RetiredOn);
    }

    [Fact]
    public async Task An_unknown_or_repeated_id_in_the_body_is_a_422()
    {
        var w = await SeedAsync();

        var unknown = Request(w, mutateLine: (i, line) => i == 0 ? line with { Id = Guid.NewGuid() } : line);
        await Assert.ThrowsAsync<ValidationDomainException>(() => UpdateAsync(w, unknown));

        var repeated = Request(w, mutateLine: (_, line) => line with { Id = w.Lines[0] });
        await Assert.ThrowsAsync<ValidationDomainException>(() => UpdateAsync(w, repeated));
    }

    [Fact]
    public async Task New_logs_store_the_exercise_id_and_the_migration_script_backfills_existing_ones()
    {
        var w = await SeedAsync();
        await LogAsync(w, w.Lines[0]);

        using var scope = AsStaff(w);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var log = await db.Set<RoutineExerciseLog>().SingleAsync(l => l.RoutineExerciseId == w.Lines[0]);
        Assert.Equal(w.CatalogIds[0], log.ExerciseId);

        // Simula un registro anterior a la columna y corre el script de la migración; DDL transaccional: se revierte.
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("alter table routines.routine_exercise_logs alter column exercise_id drop not null;");
        await db.Database.ExecuteSqlRawAsync("update routines.routine_exercise_logs set exercise_id = null;");
        await db.Database.ExecuteSqlRawAsync(MigrationScripts.Read("R1"));
        var filled = await db.Database
            .SqlQueryRaw<Guid>(
                "select exercise_id as \"Value\" from routines.routine_exercise_logs where routine_exercise_id = {0}",
                w.Lines[0])
            .SingleAsync();
        await tx.RollbackAsync();

        Assert.Equal(w.CatalogIds[0], filled);
    }

    [Fact]
    public async Task Deleting_a_line_that_has_logs_is_blocked_by_the_foreign_key()
    {
        var w = await SeedAsync();
        await LogAsync(w, w.Lines[0]);

        using var scope = AsStaff(w);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.ExecuteSqlRawAsync("delete from routines.routine_exercises where id = {0}", w.Lines[0]));
        var pg = Assert.IsType<PostgresException>(ex is PostgresException ? ex : ex.InnerException);
        // Restrict = ON DELETE RESTRICT (23001); NO ACTION daría 23503. Cualquiera de los dos bloquea el borrado.
        Assert.Contains(pg.SqlState, new[] { PostgresErrorCodes.RestrictViolation, PostgresErrorCodes.ForeignKeyViolation });
    }

    // --- helpers -------------------------------------------------------------------------------

    private static SaveRoutineRequest Request(
        World w,
        string name = "Rutina",
        Func<int, RoutineExerciseInput, RoutineExerciseInput>? mutateLine = null,
        int? dropLine = null,
        RoutineExerciseInput? addLine = null)
    {
        var lines = w.Lines
            .Select((id, i) => new RoutineExerciseInput(w.CatalogIds[i], 3, "10", null, 60, null, id))
            .Select((line, i) => mutateLine?.Invoke(i, line) ?? line)
            .Where((_, i) => i != dropLine)
            .ToList();
        if (addLine is not null)
        {
            lines.Add(addLine);
        }

        return new SaveRoutineRequest(name, null,
        [
            new RoutineDayInput("D1", false, lines, w.TrainingDayId),
            new RoutineDayInput(null, true, [], w.RestDayId),
        ]);
    }

    private async Task<RoutineResponse> UpdateAsync(World w, SaveRoutineRequest request)
    {
        using var scope = AsStaff(w);
        return await scope.ServiceProvider.GetRequiredService<UpdateRoutineHandler>()
            .HandleAsync(w.RoutineId, request, default);
    }

    private async Task LogAsync(World w, Guid lineId)
    {
        using var scope = AsMember(w);
        await scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>()
            .HandleAsync(lineId, new LogRoutineExerciseRequest(3, "10", 20m, null), default);
    }

    private async Task<int> CountLogsAsync(World w)
    {
        using var scope = AsStaff(w);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await db.Set<RoutineExerciseLog>().CountAsync(l => l.MemberId == w.MemberId);
    }

    private IServiceScope AsStaff(World w)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Day);
        return scope;
    }

    private IServiceScope AsMember(World w)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserId, w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Day);
        return scope;
    }

    private async Task<World> SeedAsync()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from routines.routine_exercise_logs; delete from routines.routines;");

        var slug = $"r1-{Guid.NewGuid():N}"[..12];
        var gym = Gym.Create("R1 Gym", slug);
        var staff = MakeUser($"{slug}-s", UserRole.Staff, gym.Id);
        gym.AssignOwner(staff.Id);
        var memberUser = MakeUser($"{slug}-m", UserRole.Member, gym.Id);
        var member = Member.Create(gym.Id, "Miembro", "R1", $"doc-{slug}-1", new DateOnly(2026, 1, 1));
        member.LinkUser(memberUser.Id);

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), staff, memberUser, member);
        await db.SaveChangesAsync();

        var catalog = await db.Set<Exercise>().Where(e => e.GymId == null)
            .OrderBy(e => e.Name).Select(e => e.Id).Take(4).ToListAsync();

        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staff.Id, gym.Id, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Day);
        var routine = await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>().HandleAsync(
            member.Id,
            new SaveRoutineRequest("Rutina", null,
            [
                new RoutineDayInput("D1", false,
                    catalog.Take(3).Select(id => new RoutineExerciseInput(id, 3, "10", null, 60, null)).ToList()),
                new RoutineDayInput(null, true, []),
            ]),
            default);

        var training = routine.Days.Single(d => !d.IsRest);
        var world = new World(
            gym.Id, staff.Id, member.Id, memberUser.Id, routine.Id, training.Id,
            routine.Days.Single(d => d.IsRest).Id,
            training.Exercises.OrderBy(e => e.Order).Select(e => e.Id).ToList(), catalog);
        scope.Dispose();
        return world;
    }

    private static GSM.Infrastructure.Identity.AppUser MakeUser(string local, UserRole role, Guid gymId)
        => new()
        {
            UserName = $"{local}@test.local",
            Email = $"{local}@test.local",
            NormalizedUserName = $"{local}@TEST.LOCAL",
            NormalizedEmail = $"{local}@TEST.LOCAL",
            EmailConfirmed = true,
            FullName = local,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

    private sealed record World(
        Guid GymId, Guid StaffId, Guid MemberId, Guid MemberUserId, Guid RoutineId,
        Guid TrainingDayId, Guid RestDayId, IReadOnlyList<Guid> Lines, IReadOnlyList<Guid> CatalogIds);
}
