using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// R3: catálogo de ejercicios v2 (tipo, qué se mide, unilateral, nivel, grupo muscular, ocultar y duplicar).
/// Los ejercicios del sistema solo se leen: los cambia la migración, nunca un test.
/// </summary>
public sealed class ExerciseCatalogV2Tests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // --- Migración ---

    [Fact]
    public async Task Migration_removed_cardio_as_a_muscle_and_reclassified_the_system_catalog()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var labels = await db.Database.SqlQueryRaw<string>(
            "select e.enumlabel as \"Value\" from pg_enum e join pg_type t on t.oid = e.enumtypid " +
            "join pg_namespace n on n.oid = t.typnamespace where n.nspname = 'routines' and t.typname = 'muscle'")
            .ToListAsync();
        Assert.DoesNotContain("cardio", labels);
        Assert.Contains("traps", labels);
        Assert.Contains("obliques", labels);
        Assert.Contains("adductors", labels);
        Assert.Contains("abductors", labels);

        var system = await db.Set<Exercise>().Where(e => e.GymId == null).ToListAsync();
        Assert.All(system.Where(e => e.ExerciseType == ExerciseType.Cardio && e.Name != "Salto a la cuerda"),
            e => Assert.Equal(Muscle.Full_body, e.PrimaryMuscle));

        var treadmill = system.Single(e => e.Name == "Cinta de correr");
        Assert.Equal(Muscle.Full_body, treadmill.PrimaryMuscle);
        Assert.Equal(ExerciseType.Cardio, treadmill.ExerciseType);
        Assert.Equal(TrackingType.TimeDistance, treadmill.TrackingType);

        Assert.Equal(Muscle.Traps, system.Single(e => e.Name.StartsWith("Encogimientos de trapecio")).PrimaryMuscle);
        Assert.Equal(TrackingType.Time, system.Single(e => e.Name == "Plancha").TrackingType);
        Assert.Equal(TrackingType.Reps, system.Single(e => e.Name == "Dominadas").TrackingType);
        Assert.True(system.Single(e => e.Name == "Zancadas con mancuernas").IsUnilateral);
        Assert.Equal(Difficulty.Beginner, system.Single(e => e.Name == "Flexiones de pecho").Difficulty);
    }

    // --- Tipo, qué se mide, unilateral, nivel ---

    [Fact]
    public async Task Tracking_type_is_suggested_by_exercise_type_when_not_sent_and_can_be_overridden()
    {
        var world = await SeedAsync();
        var created = new List<Guid>();
        try
        {
            using var scope = Staff(world.GymA, world.StaffA);
            var create = scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>();

            var mobility = await create.HandleAsync(Request(world, "Movilidad de cadera", ExerciseType.Mobility), default);
            var stretching = await create.HandleAsync(Request(world, "Estirar isquios", ExerciseType.Stretching), default);
            var cardio = await create.HandleAsync(Request(world, "Trote suave", ExerciseType.Cardio), default);
            var strength = await create.HandleAsync(Request(world, "Press raro", ExerciseType.Strength), default);
            var overridden = await create.HandleAsync(
                Request(world, "Movilidad con repeticiones", ExerciseType.Mobility) with { TrackingType = TrackingType.Reps }, default);
            created.AddRange([mobility.Id, stretching.Id, cardio.Id, strength.Id, overridden.Id]);

            Assert.Equal("Time", mobility.TrackingType);
            Assert.Equal("Time", stretching.TrackingType);
            Assert.Equal("TimeDistance", cardio.TrackingType);
            Assert.Equal("RepsWeight", strength.TrackingType);
            Assert.Equal("Reps", overridden.TrackingType);
            Assert.Equal("Mobility", overridden.ExerciseType);

            // Sin mandar tipo ni nivel: fuerza e intermedio, bilateral.
            Assert.Equal("Strength", strength.ExerciseType);
            Assert.Equal("Intermediate", strength.Difficulty);
            Assert.False(strength.IsUnilateral);
        }
        finally
        {
            await DeleteAsync(created);
        }
    }

    [Fact]
    public async Task Update_changes_type_tracking_unilateral_and_difficulty()
    {
        var world = await SeedAsync();
        var created = new List<Guid>();
        try
        {
            using var scope = Staff(world.GymA, world.StaffA);
            var exercise = await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>()
                .HandleAsync(Request(world, "Remo a un brazo", ExerciseType.Strength), default);
            created.Add(exercise.Id);

            var updated = await scope.ServiceProvider.GetRequiredService<UpdateExerciseHandler>().HandleAsync(
                exercise.Id,
                new UpdateExerciseRequest(
                    "Remo a un brazo", null, Muscle.Back, null, null, null, null,
                    ExerciseType.Plyometrics, TrackingType.Reps, true, Difficulty.Advanced),
                default);

            Assert.Equal("Plyometrics", updated.ExerciseType);
            Assert.Equal("Reps", updated.TrackingType);
            Assert.True(updated.IsUnilateral);
            Assert.Equal("Advanced", updated.Difficulty);
        }
        finally
        {
            await DeleteAsync(created);
        }
    }

    // --- Grupo muscular ---

    [Fact]
    public void Muscle_group_is_derived_from_the_primary_muscle()
    {
        Assert.Equal(MuscleGroup.LowerBody, MuscleGroups.Of(Muscle.Hamstrings));
        Assert.Equal(MuscleGroup.Core, MuscleGroups.Of(Muscle.Lower_back));
        Assert.Equal(MuscleGroup.UpperBody, MuscleGroups.Of(Muscle.Traps));
        Assert.Equal(MuscleGroup.UpperBody, MuscleGroups.Of(Muscle.Neck));
        Assert.Equal(MuscleGroup.LowerBody, MuscleGroups.Of(Muscle.Adductors));
        Assert.Equal(MuscleGroup.Core, MuscleGroups.Of(Muscle.Obliques));
        Assert.Equal(MuscleGroup.FullBody, MuscleGroups.Of(Muscle.Full_body));

        // Todo músculo tiene grupo, y los cuatro grupos juntos cubren todos los músculos.
        var covered = Enum.GetValues<MuscleGroup>().SelectMany(MuscleGroups.MusclesOf).ToList();
        Assert.Equal(Enum.GetValues<Muscle>().OrderBy(m => m), covered.OrderBy(m => m));
    }

    [Fact]
    public async Task Responses_expose_the_muscle_group_and_the_routine_exposes_tracking_and_day_groups()
    {
        var world = await SeedAsync();
        var created = new List<Guid>();
        try
        {
            Guid hamstringId, lumbarId;
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                var create = scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>();
                var hamstring = await create.HandleAsync(
                    Request(world, "Curl nórdico", ExerciseType.Strength) with { PrimaryMuscle = Muscle.Hamstrings }, default);
                var lumbar = await create.HandleAsync(
                    Request(world, "Extensión lumbar", ExerciseType.Mobility) with { PrimaryMuscle = Muscle.Lower_back }, default);
                created.AddRange([hamstring.Id, lumbar.Id]);
                hamstringId = hamstring.Id;
                lumbarId = lumbar.Id;

                Assert.Equal("LowerBody", hamstring.MuscleGroup);
                Assert.Equal("Core", lumbar.MuscleGroup);

                var routine = await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>().HandleAsync(
                    world.MemberA,
                    new SaveRoutineRequest("Mixta", null,
                    [
                        new RoutineDayInput(null, false,
                        [
                            new RoutineExerciseInput(hamstringId, 3, "8", null, 60, null),
                            new RoutineExerciseInput(lumbarId, 3, "30", null, 60, null),
                            new RoutineExerciseInput(hamstringId, 3, "8", null, 60, null),
                        ]),
                    ]),
                    default);

                var day = Assert.Single(routine.Days);
                Assert.Equal(["LowerBody", "Core"], day.MuscleGroups);
                Assert.Equal("RepsWeight", day.Exercises[0].TrackingType);
                Assert.Equal("Time", day.Exercises[1].TrackingType);
                Assert.Equal("Core", day.Exercises[1].MuscleGroup);
            }
        }
        finally
        {
            await DeleteAsync(created);
        }
    }

    // --- Ocultar ---

    [Fact]
    public async Task Hiding_a_system_exercise_removes_it_from_that_gyms_search_only_and_routines_keep_it()
    {
        var world = await SeedAsync();
        Guid systemId;
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            systemId = await db.Set<Exercise>().Where(e => e.GymId == null && e.Name == "Remo con barra")
                .Select(e => e.Id).SingleAsync();
        }

        try
        {
            // El gym A tiene una rutina que ya usa el ejercicio.
            Guid routineId;
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                var routine = await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>().HandleAsync(
                    world.MemberA,
                    new SaveRoutineRequest("Con remo", null,
                        [new RoutineDayInput("Espalda", false, [new RoutineExerciseInput(systemId, 3, "8-12", null, 60, null)])]),
                    default);
                routineId = routine.Id;
            }

            using (var scope = Staff(world.GymA, world.StaffA))
            {
                await scope.ServiceProvider.GetRequiredService<HideExerciseHandler>().HandleAsync(systemId, default);
                await scope.ServiceProvider.GetRequiredService<HideExerciseHandler>().HandleAsync(systemId, default); // idempotente
            }

            using (var scope = Staff(world.GymA, world.StaffA))
            {
                var visible = await scope.ServiceProvider.GetRequiredService<ListExercisesHandler>()
                    .HandleAsync(new ExerciseFilter(null, null, null, false), default);
                Assert.DoesNotContain(visible, e => e.Id == systemId);

                var withHidden = await scope.ServiceProvider.GetRequiredService<ListExercisesHandler>()
                    .HandleAsync(new ExerciseFilter(null, null, null, false, IncludeHidden: true), default);
                var hidden = Assert.Single(withHidden, e => e.Id == systemId);
                Assert.True(hidden.IsHidden);
                Assert.All(withHidden.Where(e => e.Id != systemId), e => Assert.False(e.IsHidden));

                // La rutina que ya lo usa no cambia.
                var routine = await scope.ServiceProvider.GetRequiredService<GetRoutineHandler>()
                    .HandleAsync(routineId, default);
                Assert.Equal("Remo con barra", routine.Days[0].Exercises[0].ExerciseName);
            }

            // Otro gym lo sigue viendo.
            using (var scope = Staff(world.GymB, world.StaffB))
            {
                var visible = await scope.ServiceProvider.GetRequiredService<ListExercisesHandler>()
                    .HandleAsync(new ExerciseFilter(null, null, null, false), default);
                var other = Assert.Single(visible, e => e.Id == systemId);
                Assert.False(other.IsHidden);
            }

            // Se puede volver a mostrar.
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                await scope.ServiceProvider.GetRequiredService<ShowExerciseHandler>().HandleAsync(systemId, default);
                var visible = await scope.ServiceProvider.GetRequiredService<ListExercisesHandler>()
                    .HandleAsync(new ExerciseFilter(null, null, null, false), default);
                Assert.Contains(visible, e => e.Id == systemId);
            }
        }
        finally
        {
            using var scope = System();
            await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Database
                .ExecuteSqlRawAsync("delete from routines.gym_hidden_exercises where exercise_id = {0}", systemId);
        }
    }

    [Fact]
    public async Task Hiding_an_exercise_that_belongs_to_the_gym_is_rejected_with_422()
    {
        var world = await SeedAsync();
        var created = new List<Guid>();
        try
        {
            using var scope = Staff(world.GymA, world.StaffA);
            var own = await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>()
                .HandleAsync(Request(world, "Propio del gym", ExerciseType.Strength), default);
            created.Add(own.Id);

            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<HideExerciseHandler>().HandleAsync(own.Id, default));
        }
        finally
        {
            await DeleteAsync(created);
        }
    }

    // --- Duplicar ---

    [Fact]
    public async Task Duplicating_a_system_exercise_copies_every_field_into_the_gym_and_can_hide_the_original()
    {
        var world = await SeedAsync();
        var created = new List<Guid>();
        Guid systemId;
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            systemId = await db.Set<Exercise>().Where(e => e.GymId == null && e.Name == "Zancadas con mancuernas")
                .Select(e => e.Id).SingleAsync();
        }

        try
        {
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                var original = await scope.ServiceProvider.GetRequiredService<GetExerciseHandler>()
                    .HandleAsync(systemId, default);

                var copy = await scope.ServiceProvider.GetRequiredService<DuplicateExerciseHandler>()
                    .HandleAsync(systemId, new DuplicateExerciseRequest(HideOriginal: true), default);
                created.Add(copy.Id);

                Assert.NotEqual(systemId, copy.Id);
                Assert.Equal(world.GymA, copy.GymId);
                Assert.False(copy.IsSystem);
                Assert.Equal("Zancadas con mancuernas (copia)", copy.Name);
                Assert.Equal(original.PrimaryMuscle, copy.PrimaryMuscle);
                Assert.Equal(original.SecondaryMuscles, copy.SecondaryMuscles);
                Assert.Equal(original.Equipment, copy.Equipment);
                Assert.Equal(original.ExerciseType, copy.ExerciseType);
                Assert.Equal(original.TrackingType, copy.TrackingType);
                Assert.Equal(original.IsUnilateral, copy.IsUnilateral);
                Assert.Equal(original.Difficulty, copy.Difficulty);
                Assert.Equal(original.Description, copy.Description);
                Assert.Equal(original.VideoUrl, copy.VideoUrl);
                Assert.Equal(original.ImageUrl, copy.ImageUrl);

                // El original quedó oculto; la copia del gym sí sale.
                var visible = await scope.ServiceProvider.GetRequiredService<ListExercisesHandler>()
                    .HandleAsync(new ExerciseFilter(null, null, null, false), default);
                Assert.DoesNotContain(visible, e => e.Id == systemId);
                Assert.Contains(visible, e => e.Id == copy.Id);

                // Duplicar otra vez no choca con el nombre: numera la copia.
                var second = await scope.ServiceProvider.GetRequiredService<DuplicateExerciseHandler>()
                    .HandleAsync(systemId, new DuplicateExerciseRequest(), default);
                created.Add(second.Id);
                Assert.Equal("Zancadas con mancuernas (copia 2)", second.Name);
            }

            // Sin "ocultar el original", el original se queda visible.
            using (var scope = Staff(world.GymB, world.StaffB))
            {
                var copy = await scope.ServiceProvider.GetRequiredService<DuplicateExerciseHandler>()
                    .HandleAsync(systemId, new DuplicateExerciseRequest(HideOriginal: false), default);
                created.Add(copy.Id);
                var visible = await scope.ServiceProvider.GetRequiredService<ListExercisesHandler>()
                    .HandleAsync(new ExerciseFilter(null, null, null, false), default);
                Assert.Contains(visible, e => e.Id == systemId);
            }
        }
        finally
        {
            await DeleteAsync(created);
            using var scope = System();
            await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Database
                .ExecuteSqlRawAsync("delete from routines.gym_hidden_exercises where exercise_id = {0}", systemId);
        }
    }

    // --- Filtros ---

    [Fact]
    public async Task Catalog_filters_by_type_difficulty_muscle_group_and_muscle()
    {
        var world = await SeedAsync();
        using var scope = Staff(world.GymA, world.StaffA);
        var list = scope.ServiceProvider.GetRequiredService<ListExercisesHandler>();

        var cardio = await list.HandleAsync(new ExerciseFilter(null, null, null, false, ExerciseType: ExerciseType.Cardio), default);
        Assert.NotEmpty(cardio);
        Assert.All(cardio, e => Assert.Equal("Cardio", e.ExerciseType));

        var advanced = await list.HandleAsync(new ExerciseFilter(null, null, null, false, Difficulty: Difficulty.Advanced), default);
        Assert.NotEmpty(advanced);
        Assert.All(advanced, e => Assert.Equal("Advanced", e.Difficulty));

        var lower = await list.HandleAsync(new ExerciseFilter(null, null, null, false, MuscleGroup: MuscleGroup.LowerBody), default);
        Assert.NotEmpty(lower);
        Assert.All(lower, e => Assert.Equal("LowerBody", e.MuscleGroup));
        Assert.Contains(lower, e => e.PrimaryMuscle == "Hamstrings");

        var core = await list.HandleAsync(new ExerciseFilter(null, null, null, false, MuscleGroup: MuscleGroup.Core), default);
        Assert.Contains(core, e => e.PrimaryMuscle == "Lower_back");
        Assert.DoesNotContain(core, e => e.MuscleGroup != "Core");

        // Segundo paso del filtro: un músculo concreto dentro de la zona.
        var hamstrings = await list.HandleAsync(
            new ExerciseFilter(null, Muscle.Hamstrings, null, false, MuscleGroup: MuscleGroup.LowerBody), default);
        Assert.NotEmpty(hamstrings);
        Assert.All(hamstrings, e => Assert.Equal("Hamstrings", e.PrimaryMuscle));

        // Un músculo que no es de la zona elegida no devuelve nada.
        var mismatch = await list.HandleAsync(
            new ExerciseFilter(null, Muscle.Hamstrings, null, false, MuscleGroup: MuscleGroup.UpperBody), default);
        Assert.Empty(mismatch);

        // Combinados.
        var combined = await list.HandleAsync(
            new ExerciseFilter(null, null, null, false, ExerciseType: ExerciseType.Strength, Difficulty: Difficulty.Beginner), default);
        Assert.All(combined, e => Assert.True(e.ExerciseType == "Strength" && e.Difficulty == "Beginner"));
    }

    // --- Ayudas ---

    private static CreateExerciseRequest Request(World world, string name, ExerciseType type)
        => new(name + " " + world.Suffix, null, Muscle.Chest, null, null, null, null, type);

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Staff(Guid gymId, Guid staffId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        return scope;
    }

    private async Task DeleteAsync(IEnumerable<Guid> exerciseIds)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var ids = exerciseIds.ToList();
        await db.Set<Exercise>().Where(e => ids.Contains(e.Id) && e.GymId != null).ExecuteDeleteAsync();
    }

    private sealed record World(Guid GymA, Guid StaffA, Guid MemberA, Guid GymB, Guid StaffB, string Suffix);

    private async Task<World> SeedAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (gymA, staffA, memberA) = CreateGym(db, $"cv2-a-{suffix}", "CV2 A");
        var (gymB, staffB, _) = CreateGym(db, $"cv2-b-{suffix}", "CV2 B");
        await db.SaveChangesAsync();
        return new World(gymA, staffA, memberA, gymB, staffB, suffix);
    }

    private static (Guid GymId, Guid StaffId, Guid MemberId) CreateGym(GsmDbContext db, string slug, string name)
    {
        var gym = Gym.Create(name, slug);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = $"{slug}@test.local",
            Email = $"{slug}@test.local",
            NormalizedUserName = $"{slug}@TEST.LOCAL",
            NormalizedEmail = $"{slug}@TEST.LOCAL",
            EmailConfirmed = true,
            FullName = $"{name} Staff",
            Role = UserRole.Staff,
            GymId = gym.Id,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Miembro", "Uno", $"doc-{slug}", new DateOnly(2026, 1, 1));

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(member);
        return (gym.Id, staff.Id, member.Id);
    }
}
