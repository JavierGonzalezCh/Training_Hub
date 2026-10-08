using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Tenancy del catálogo híbrido: <see cref="Exercise"/> queda fuera del Global Query Filter, así
/// que el aislamiento entre gyms se prueba aparte. Más la autorización global vs del gym.
/// </summary>
public sealed class ExerciseCatalogTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task System_catalog_from_the_migration_exists_on_a_clean_database()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var systemCount = await db.Set<Exercise>().CountAsync(e => e.GymId == null);
        var muscles = await db.Set<Exercise>().Where(e => e.GymId == null)
            .Select(e => e.PrimaryMuscle).Distinct().ToListAsync();

        Assert.True(systemCount >= 40, $"esperaba >= 40 ejercicios del sistema, hay {systemCount}");

        // R3: los músculos nuevos (oblicuos, aductores, abductores) y el cuello aún no tienen ejercicio del
        // sistema —es trabajo de contenido—; todos los demás sí.
        var withoutSystemExercise = Enum.GetValues<Muscle>().Except(muscles).ToList();
        Assert.All(withoutSystemExercise, m => Assert.Contains(
            m, new[] { Muscle.Neck, Muscle.Obliques, Muscle.Adductors, Muscle.Abductors }));
    }

    [Fact]
    public async Task A_gym_sees_system_and_its_own_exercises_never_another_gyms()
    {
        var (gymA, staffA, gymB, staffB) = await SeedTwoGymsAsync();

        Guid ownExerciseA;
        using (var scope = Staff(gymA, staffA))
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>()
                .HandleAsync(new CreateExerciseRequest(
                    "Curl de gym A", null, Muscle.Biceps, null, null, null, null), default);
            ownExerciseA = created.Id;
            Assert.Equal(gymA, created.GymId);
        }

        using (var scope = Staff(gymB, staffB))
        {
            await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>()
                .HandleAsync(new CreateExerciseRequest(
                    "Sentadilla de gym B", null, Muscle.Quadriceps, null, null, null, null), default);

            var visible = await scope.ServiceProvider.GetRequiredService<ListExercisesHandler>()
                .HandleAsync(new ExerciseFilter(null, null, null, true), default);

            Assert.DoesNotContain(visible, e => e.Id == ownExerciseA);
            Assert.DoesNotContain(visible, e => e.Name == "Curl de gym A");
            Assert.Contains(visible, e => e.Name == "Sentadilla de gym B");
            Assert.Contains(visible, e => e.IsSystem);

            // Y no puede leer el de A ni por id directo.
            await Assert.ThrowsAsync<NotFoundException>(() =>
                scope.ServiceProvider.GetRequiredService<GetExerciseHandler>()
                    .HandleAsync(ownExerciseA, default));
        }
    }

    [Fact]
    public async Task Staff_editing_a_system_exercise_is_forbidden_but_superadmin_can()
    {
        var (gymA, staffA, _, _) = await SeedTwoGymsAsync();

        // Se edita un ejercicio de sistema desechable, nunca uno del catálogo de la migración:
        // gsm_test no se trunca entre ejecuciones, así que cada edición quedaba para siempre y,
        // ejecución tras ejecución, acabó dejando un músculo entero sin ejercicios.
        Guid systemExerciseId;
        using (var scope = SuperAdmin())
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>()
                .HandleAsync(new CreateExerciseRequest(
                    $"Desechable {Guid.NewGuid():N}"[..22], null, Muscle.Chest, null, null, null, null), default);
            Assert.True(created.IsSystem);
            systemExerciseId = created.Id;
        }

        try
        {
            var newName = $"Editado {Guid.NewGuid():N}"[..20];
            var edit = new UpdateExerciseRequest(
                newName, "desc", Muscle.Chest, null, null, null, null);

            using (var scope = Staff(gymA, staffA))
            {
                await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                    scope.ServiceProvider.GetRequiredService<UpdateExerciseHandler>()
                        .HandleAsync(systemExerciseId, edit, default));
            }

            using (var scope = SuperAdmin())
            {
                var updated = await scope.ServiceProvider.GetRequiredService<UpdateExerciseHandler>()
                    .HandleAsync(systemExerciseId, edit, default);
                Assert.Equal(newName, updated.Name);
                Assert.True(updated.IsSystem);
            }
        }
        finally
        {
            using var scope = System();
            await scope.ServiceProvider.GetRequiredService<GsmDbContext>()
                .Set<Exercise>().Where(e => e.Id == systemExerciseId).ExecuteDeleteAsync();
        }
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope SuperAdmin()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(Guid.CreateVersion7(), gymId: null, UserRole.SuperAdmin);
        return scope;
    }

    private IServiceScope Staff(Guid gymId, Guid staffId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        return scope;
    }

    private async Task<(Guid GymA, Guid StaffA, Guid GymB, Guid StaffB)> SeedTwoGymsAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync(
            "delete from routines.exercises where gym_id is not null;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (gymA, staffA) = CreateGym(db, $"cat-a-{suffix}", "Cat A");
        var (gymB, staffB) = CreateGym(db, $"cat-b-{suffix}", "Cat B");
        await db.SaveChangesAsync();

        return (gymA, staffA, gymB, staffB);
    }

    private static (Guid GymId, Guid StaffId) CreateGym(GsmDbContext db, string slug, string name)
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
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        return (gym.Id, staff.Id);
    }
}
