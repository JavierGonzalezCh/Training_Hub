using GSM.Application.Equipment.Abstractions;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Equipment;
using GSM.Application.Equipment.Contracts;
using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;
using GSM.Domain.Identity;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// S4: catálogos del sistema que administra el SuperAdmin desde <c>/admin/catalog</c>. Los ejercicios y modelos sembrados
/// solo se leen: cada test crea los suyos desechables (prefijo "S4 ") y los borra en un <c>finally</c>.
/// </summary>
public sealed class AdminCatalogTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // --- Modelos de máquina ---

    [Fact]
    public async Task Superadmin_manages_the_machine_catalog_and_gyms_only_see_active_models()
    {
        var name = "S4 Modelo " + Guid.NewGuid().ToString("N")[..8];
        Guid id = Guid.Empty;
        try
        {
            using (var scope = SuperAdmin())
            {
                var created = await scope.ServiceProvider.GetRequiredService<CreateEquipmentModelHandler>().HandleAsync(
                    new SaveEquipmentModelRequest(name, EquipmentType.Machine, "Descripción", "Asiento en 3", true), default);
                id = created.Id;
                Assert.True(created.IsActive);
                Assert.Equal("Machine", created.Type);

                // El nombre no se repite (sin distinguir mayúsculas), ni al crear ni al editar otro.
                await Assert.ThrowsAsync<ConflictException>(() =>
                    scope.ServiceProvider.GetRequiredService<CreateEquipmentModelHandler>().HandleAsync(
                        new SaveEquipmentModelRequest(name.ToUpperInvariant(), EquipmentType.Machine, null, null, true), default));

                var updated = await scope.ServiceProvider.GetRequiredService<UpdateEquipmentModelHandler>().HandleAsync(
                    id, new SaveEquipmentModelRequest(name, EquipmentType.Cable, null, null, false), default);
                Assert.Equal("Cable", updated.Type);
                Assert.False(updated.TrackedPerUnit);
                Assert.Null(updated.Description);

                await Assert.ThrowsAsync<NotFoundException>(() =>
                    scope.ServiceProvider.GetRequiredService<UpdateEquipmentModelHandler>().HandleAsync(
                        Guid.CreateVersion7(), new SaveEquipmentModelRequest("S4 Otro", EquipmentType.Cable, null, null, false), default));
            }

            // Un gym ve el modelo mientras esté activo.
            Assert.Contains(await GymSeesModelsAsync(), m => m.Id == id);

            using (var scope = SuperAdmin())
            {
                var off = await scope.ServiceProvider.GetRequiredService<SetEquipmentModelActiveHandler>().HandleAsync(id, false, default);
                Assert.False(off.IsActive);
                // Idempotente.
                Assert.False((await scope.ServiceProvider.GetRequiredService<SetEquipmentModelActiveHandler>().HandleAsync(id, false, default)).IsActive);

                var list = scope.ServiceProvider.GetRequiredService<ListAdminEquipmentModelsHandler>();
                Assert.DoesNotContain(await list.HandleAsync(false, default), m => m.Id == id);
                Assert.Contains(await list.HandleAsync(true, default), m => m.Id == id && !m.IsActive);
            }

            Assert.DoesNotContain(await GymSeesModelsAsync(), m => m.Id == id);

            using (var scope = SuperAdmin())
            {
                Assert.True((await scope.ServiceProvider.GetRequiredService<SetEquipmentModelActiveHandler>().HandleAsync(id, true, default)).IsActive);
            }

            Assert.Contains(await GymSeesModelsAsync(), m => m.Id == id);
        }
        finally
        {
            await DeleteModelsAsync(name);
        }
    }

    [Fact]
    public async Task Only_the_superadmin_touches_the_machine_catalog()
    {
        using var scope = Staff();
        var request = new SaveEquipmentModelRequest("S4 Prohibido", EquipmentType.Machine, null, null, true);

        await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<CreateEquipmentModelHandler>().HandleAsync(request, default));
        await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<UpdateEquipmentModelHandler>().HandleAsync(Guid.CreateVersion7(), request, default));
        await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<SetEquipmentModelActiveHandler>().HandleAsync(Guid.CreateVersion7(), false, default));
        await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<ListAdminEquipmentModelsHandler>().HandleAsync(true, default));
    }

    [Fact]
    public async Task Model_image_is_replaced_and_the_previous_file_is_deleted()
    {
        var name = "S4 Imagen " + Guid.NewGuid().ToString("N")[..8];
        var storage = new FakeStorage();
        try
        {
            using var scope = SuperAdmin();
            var created = await scope.ServiceProvider.GetRequiredService<CreateEquipmentModelHandler>().HandleAsync(
                new SaveEquipmentModelRequest(name, EquipmentType.Machine, null, null, true), default);

            var upload = new UploadEquipmentModelImageHandler(
                scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
                scope.ServiceProvider.GetRequiredService<IEquipmentModelRepository>(),
                storage,
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

            var first = await upload.HandleAsync(created.Id, Png(), default);
            Assert.StartsWith("https://files.test/equipment-models/", first.ImageUrl);
            Assert.Empty(storage.Deleted);

            var second = await upload.HandleAsync(created.Id, Png(), default);
            Assert.NotEqual(first.ImageUrl, second.ImageUrl);
            Assert.Equal(first.ImageUrl, Assert.Single(storage.Deleted));

            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                upload.HandleAsync(created.Id, new FileToStore(new MemoryStream([1]), "x.gif", "image/gif", 1), default));
        }
        finally
        {
            await DeleteModelsAsync(name);
        }
    }

    // --- Ejercicios del sistema ---

    [Fact]
    public async Task Superadmin_lists_creates_deactivates_and_reactivates_system_exercises()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = "S4 Ejercicio " + suffix;
        try
        {
            using var scope = SuperAdmin();
            var created = await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>().HandleAsync(
                new CreateExerciseRequest(name, null, Muscle.Chest, null, null, null, null), default);
            Assert.True(created.IsSystem);

            var list = scope.ServiceProvider.GetRequiredService<ListExercisesHandler>();
            var filter = new ExerciseFilter("S4 Ejercicio " + suffix, null, null, false);
            Assert.Single(await list.HandleAsync(filter, default));

            await scope.ServiceProvider.GetRequiredService<DeactivateExerciseHandler>().HandleAsync(created.Id, default);
            Assert.Empty(await list.HandleAsync(filter, default));
            Assert.False(Assert.Single(await list.HandleAsync(filter with { IncludeInactive = true }, default)).IsActive);

            var back = await scope.ServiceProvider.GetRequiredService<ActivateExerciseHandler>().HandleAsync(created.Id, default);
            Assert.True(back.IsActive);
            Assert.Single(await list.HandleAsync(filter, default));
        }
        finally
        {
            await DeleteExercisesAsync(suffix);
        }
    }

    [Fact]
    public async Task Only_the_superadmin_reactivates_or_uploads_images_of_system_exercises_and_the_image_is_stored()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var storage = new FakeStorage();
        try
        {
            Guid id;
            using (var scope = SuperAdmin())
            {
                id = (await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>().HandleAsync(
                    new CreateExerciseRequest("S4 Ejercicio " + suffix, null, Muscle.Back, null, null, null, "https://old.test/a.png"), default)).Id;
            }

            using (var scope = Staff())
            {
                await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                    scope.ServiceProvider.GetRequiredService<ActivateExerciseHandler>().HandleAsync(id, default));
                await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                    Upload(scope, storage).HandleAsync(id, Png(), default));
            }

            using (var scope = SuperAdmin())
            {
                var updated = await Upload(scope, storage).HandleAsync(id, Png(), default);
                Assert.StartsWith("https://files.test/exercises/", updated.ImageUrl);
                Assert.Equal("S4 Ejercicio " + suffix, updated.Name);   // el resto no cambia
                Assert.Equal("Back", updated.PrimaryMuscle);
                Assert.Equal("https://old.test/a.png", Assert.Single(storage.Deleted));
            }
        }
        finally
        {
            await DeleteExercisesAsync(suffix);
        }
    }

    // --- Alternativas del sistema ---

    [Fact]
    public async Task Superadmin_saves_and_removes_system_alternatives_in_both_directions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        try
        {
            using var scope = SuperAdmin();
            var create = scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>();
            var a = (await create.HandleAsync(new CreateExerciseRequest("S4 Ejercicio A " + suffix, null, Muscle.Chest, null, null, null, null), default)).Id;
            var b = (await create.HandleAsync(new CreateExerciseRequest("S4 Ejercicio B " + suffix, null, Muscle.Chest, null, null, null, null), default)).Id;

            var add = scope.ServiceProvider.GetRequiredService<AddExerciseAlternativeHandler>();
            await add.HandleAsync(a, b, default);
            await add.HandleAsync(b, a, default);   // idempotente, también al revés

            var get = scope.ServiceProvider.GetRequiredService<GetExerciseAlternativesHandler>();
            Assert.Equal(b, Assert.Single((await get.HandleAsync(a, default)).Where(x => x.Source == "recommended")).Id);
            Assert.Equal(a, Assert.Single((await get.HandleAsync(b, default)).Where(x => x.Source == "recommended")).Id);

            await scope.ServiceProvider.GetRequiredService<RemoveExerciseAlternativeHandler>().HandleAsync(b, a, default);
            Assert.Empty((await get.HandleAsync(a, default)).Where(x => x.Source == "recommended"));
            Assert.Empty((await get.HandleAsync(b, default)).Where(x => x.Source == "recommended"));
        }
        finally
        {
            await DeleteExercisesAsync(suffix);
        }
    }

    // --- Ligas ejercicio <-> modelo ---

    [Fact]
    public async Task Superadmin_links_a_system_exercise_to_catalog_models_including_a_new_one()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var modelName = "S4 Modelo " + suffix;
        try
        {
            using var scope = SuperAdmin();
            var model = await scope.ServiceProvider.GetRequiredService<CreateEquipmentModelHandler>().HandleAsync(
                new SaveEquipmentModelRequest(modelName, EquipmentType.Cable, null, null, true), default);
            var exercise = await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>().HandleAsync(
                new CreateExerciseRequest("S4 Ejercicio " + suffix, null, Muscle.Back, null, null, null, null), default);

            await scope.ServiceProvider.GetRequiredService<SetExerciseEquipmentModelsHandler>().HandleAsync(
                exercise.Id, new SetExerciseEquipmentModelsRequest([model.Id]), default);

            var links = await scope.ServiceProvider.GetRequiredService<GetExerciseEquipmentHandler>().HandleAsync(exercise.Id, default);
            Assert.Equal(model.Id, Assert.Single(links.Models).Id);
            Assert.Empty(links.Machines);   // un superadmin no tiene gym
        }
        finally
        {
            await DeleteExercisesAsync(suffix);
            await DeleteModelsAsync(modelName);
        }
    }

    // --- Autorización por HTTP ---

    [Fact]
    public void Admin_catalog_endpoints_require_the_superadmin_policy()
    {
        var attribute = typeof(GSM.API.Controllers.AdminCatalogController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Single();
        Assert.Equal(AuthPolicies.RequireSuperAdmin, attribute.Policy);
    }

    // --- Ayudas ---

    private async Task<IReadOnlyList<EquipmentModelResponse>> GymSeesModelsAsync()
    {
        using var scope = Staff();
        return await scope.ServiceProvider.GetRequiredService<ListEquipmentModelsHandler>().HandleAsync(null, default);
    }

    private static UploadExerciseImageHandler Upload(IServiceScope scope, IFileStorage storage)
        => new(
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            scope.ServiceProvider.GetRequiredService<IExerciseRepository>(),
            scope.ServiceProvider.GetRequiredService<ExerciseEquipmentResolver>(),
            storage,
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

    private static FileToStore Png() => new(new MemoryStream([1, 2, 3]), "a.png", "image/png", 3);

    private async Task DeleteModelsAsync(string name)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "delete from routines.exercise_equipment_models where model_id in "
            + "(select id from equipment.equipment_models where name = {0})", name);
        await db.Set<EquipmentModel>().Where(m => m.Name == name).ExecuteDeleteAsync();
    }

    private async Task DeleteExercisesAsync(string suffix)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var like = $"S4 Ejercicio%{suffix}";
        await db.Database.ExecuteSqlRawAsync(
            "delete from routines.exercise_alternatives where exercise_id in (select id from routines.exercises where name like {0}) "
            + "or alternative_id in (select id from routines.exercises where name like {0})", like);
        await db.Database.ExecuteSqlRawAsync(
            "delete from routines.exercise_equipment_models where exercise_id in (select id from routines.exercises where name like {0})", like);
        await db.Database.ExecuteSqlRawAsync("delete from routines.exercises where name like {0} and gym_id is null", like);
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
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), null, UserRole.SuperAdmin);
        return scope;
    }

    /// <summary>Un staff de un gym cualquiera: estos casos de uso no leen datos del gym, solo el rol.</summary>
    private IServiceScope Staff()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), Guid.CreateVersion7(), UserRole.Staff);
        return scope;
    }

    private sealed class FakeStorage : IFileStorage
    {
        public List<string> Deleted { get; } = [];

        public Task<string> SaveAsync(FileToStore file, string folder, CancellationToken cancellationToken = default)
            => Task.FromResult($"https://files.test/{folder}/{Guid.NewGuid():N}.png");

        public Task DeleteAsync(string publicUrl, CancellationToken cancellationToken = default)
        {
            Deleted.Add(publicUrl);
            return Task.CompletedTask;
        }
    }
}
