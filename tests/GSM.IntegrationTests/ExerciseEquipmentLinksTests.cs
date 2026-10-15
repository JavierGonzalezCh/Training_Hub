using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Equipment;
using GSM.Application.Equipment.Contracts;
using GSM.Application.Equipment.Public;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// E3: ligas ejercicio ↔ modelo (sistema) y ejercicio ↔ máquina (gym), disponibilidad "hoy en mi gym" (nivel 1),
/// aviso en el editor y foto/zona/instrucciones en la rutina del miembro. Los ejercicios y modelos del sistema
/// sembrados solo se leen: cada test crea los suyos desechables y los borra en un <c>finally</c>.
/// </summary>
public sealed class ExerciseEquipmentLinksTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    // --- Siembra de ligas de sistema ---

    [Fact]
    public async Task Migration_seeds_system_links_between_system_exercises_and_catalog_models()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var press = await db.Set<Exercise>().SingleAsync(e => e.GymId == null && e.Name == "Prensa de piernas");
        var model45 = await db.Set<EquipmentModel>().SingleAsync(m => m.Name == "Prensa de piernas 45°");

        var links = await scope.ServiceProvider.GetRequiredService<IExerciseEquipmentRepository>()
            .ListModelLinksAsync([press.Id], default);
        Assert.Contains(links, l => l.ModelId == model45.Id);
    }

    // --- Ligado por modelo: la máquina del gym queda ligada sola ---

    [Fact]
    public async Task Choosing_a_catalog_model_links_the_system_exercise_to_the_gym_machine_and_its_status_decides()
    {
        var world = await SeedAsync();
        var (exerciseId, modelId) = await CreateSystemExerciseWithModelLinkAsync(world, EquipmentType.Machine);
        try
        {
            // Otra máquina del mismo tipo, sin relación con el modelo: no debe salvar el ejercicio (regla 1 manda sobre la 2).
            Guid linked;
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                var created = await scope.ServiceProvider.GetRequiredService<AddEquipmentFromModelsHandler>().HandleAsync(
                    new AddEquipmentFromModelsRequest([new EquipmentFromModelItem(modelId, 1, "Zona de pierna")]), default);
                linked = created.Single().Id;
                await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                    new SaveEquipmentRequest("Otra máquina " + world.Suffix, EquipmentType.Machine, null, null, null, null, 1, null, null), default);
            }

            var available = await GetExerciseAsync(world.GymA, world.StaffA, exerciseId);
            Assert.True(available.IsAvailableHere);
            var machine = Assert.Single(available.Machines!);
            Assert.Equal(linked, machine.Id);

            using (var scope = Staff(world.GymA, world.StaffA))
            {
                await scope.ServiceProvider.GetRequiredService<ChangeEquipmentStatusHandler>().HandleAsync(
                    linked, new ChangeEquipmentStatusRequest(EquipmentStatus.Maintenance, "Cable", null), default);
            }

            var broken = await GetExerciseAsync(world.GymA, world.StaffA, exerciseId);
            Assert.False(broken.IsAvailableHere);
            Assert.Equal("Maintenance", Assert.Single(broken.Machines!).Status);

            using (var scope = Staff(world.GymA, world.StaffA))
            {
                await scope.ServiceProvider.GetRequiredService<ChangeEquipmentStatusHandler>().HandleAsync(
                    linked, new ChangeEquipmentStatusRequest(EquipmentStatus.Available, null, null), default);
            }

            Assert.True((await GetExerciseAsync(world.GymA, world.StaffA, exerciseId)).IsAvailableHere);
        }
        finally
        {
            await CleanupAsync([exerciseId], [modelId]);
        }
    }

    [Fact]
    public async Task Gym_links_replace_the_model_links_for_that_gym_only_and_empty_goes_back_to_the_model()
    {
        var world = await SeedAsync();
        var (exerciseId, modelId) = await CreateSystemExerciseWithModelLinkAsync(world, EquipmentType.Machine);
        try
        {
            Guid fromModel;
            Guid own;
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                fromModel = (await scope.ServiceProvider.GetRequiredService<AddEquipmentFromModelsHandler>().HandleAsync(
                    new AddEquipmentFromModelsRequest([new EquipmentFromModelItem(modelId, 1, null)]), default)).Single().Id;
                own = (await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                    new SaveEquipmentRequest("Mi prensa casera " + world.Suffix, EquipmentType.Machine, "Fondo", null, null, null, 1, null, null), default)).Id;
            }

            // Corrige el ligado automático: solo su máquina propia cuenta.
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                await scope.ServiceProvider.GetRequiredService<SetExerciseEquipmentItemsHandler>().HandleAsync(
                    exerciseId, new SetExerciseEquipmentItemsRequest([own]), default);
            }

            var corrected = await GetExerciseAsync(world.GymA, world.StaffA, exerciseId);
            Assert.Equal(own, Assert.Single(corrected.Machines!).Id);

            using (var scope = Staff(world.GymA, world.StaffA))
            {
                var links = await scope.ServiceProvider.GetRequiredService<GetExerciseEquipmentHandler>().HandleAsync(exerciseId, default);
                Assert.Equal([own], links.ItemIds);
                Assert.Equal(modelId, Assert.Single(links.Models).Id);
                Assert.Equal(own, Assert.Single(links.Machines).Id);
            }

            // Otro gym con una máquina de ese modelo no se entera de las ligas del gym A.
            Guid otherGymMachine;
            using (var scope = Staff(world.GymB, world.StaffB))
            {
                otherGymMachine = (await scope.ServiceProvider.GetRequiredService<AddEquipmentFromModelsHandler>().HandleAsync(
                    new AddEquipmentFromModelsRequest([new EquipmentFromModelItem(modelId, 1, null)]), default)).Single().Id;
            }

            Assert.Equal(otherGymMachine, Assert.Single((await GetExerciseAsync(world.GymB, world.StaffB, exerciseId)).Machines!).Id);

            // Vacío = volver al ligado por modelo.
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                await scope.ServiceProvider.GetRequiredService<SetExerciseEquipmentItemsHandler>().HandleAsync(
                    exerciseId, new SetExerciseEquipmentItemsRequest([]), default);
            }

            Assert.Equal(fromModel, Assert.Single((await GetExerciseAsync(world.GymA, world.StaffA, exerciseId)).Machines!).Id);
        }
        finally
        {
            await CleanupAsync([exerciseId], [modelId]);
        }
    }

    [Fact]
    public async Task Linking_to_machines_validates_the_gym_inventory()
    {
        var world = await SeedAsync();
        var (exerciseId, modelId) = await CreateSystemExerciseWithModelLinkAsync(world, EquipmentType.Machine);
        try
        {
            Guid otherGymMachine;
            Guid inactive;
            using (var scope = Staff(world.GymB, world.StaffB))
            {
                otherGymMachine = (await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                    new SaveEquipmentRequest("De B " + world.Suffix, EquipmentType.Machine, null, null, null, null, 1, null, null), default)).Id;
            }

            using (var scope = Staff(world.GymA, world.StaffA))
            {
                inactive = (await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                    new SaveEquipmentRequest("De baja " + world.Suffix, EquipmentType.Machine, null, null, null, null, 1, null, null), default)).Id;
                await scope.ServiceProvider.GetRequiredService<DeactivateEquipmentHandler>().HandleAsync(inactive, default);

                var set = scope.ServiceProvider.GetRequiredService<SetExerciseEquipmentItemsHandler>();
                await Assert.ThrowsAsync<ValidationDomainException>(() =>
                    set.HandleAsync(exerciseId, new SetExerciseEquipmentItemsRequest([otherGymMachine]), default));
                await Assert.ThrowsAsync<ValidationDomainException>(() =>
                    set.HandleAsync(exerciseId, new SetExerciseEquipmentItemsRequest([inactive]), default));
                await Assert.ThrowsAsync<ValidationDomainException>(() =>
                    set.HandleAsync(exerciseId, new SetExerciseEquipmentItemsRequest(Enumerable.Range(0, 21).Select(_ => Guid.CreateVersion7()).ToList()), default));
                await Assert.ThrowsAsync<NotFoundException>(() =>
                    set.HandleAsync(Guid.CreateVersion7(), new SetExerciseEquipmentItemsRequest([]), default));
            }

            // Un superadmin no tiene gym desde el que ligar máquinas.
            using (var scope = SuperAdmin())
            {
                await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                    scope.ServiceProvider.GetRequiredService<SetExerciseEquipmentItemsHandler>()
                        .HandleAsync(exerciseId, new SetExerciseEquipmentItemsRequest([]), default));
            }
        }
        finally
        {
            await CleanupAsync([exerciseId], [modelId]);
        }
    }

    // --- Ligas de sistema: solo el SuperAdmin ---

    [Fact]
    public async Task Only_the_superadmin_links_system_exercises_to_models_and_the_model_must_exist()
    {
        var world = await SeedAsync();
        var (exerciseId, modelId) = await CreateSystemExerciseWithModelLinkAsync(world, EquipmentType.Machine);
        Guid gymExercise;
        try
        {
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                gymExercise = (await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>().HandleAsync(
                    new CreateExerciseRequest("Propio " + world.Suffix, null, Muscle.Chest, null, null, null, null), default)).Id;

                // Un staff no toca el catálogo del sistema.
                await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                    scope.ServiceProvider.GetRequiredService<SetExerciseEquipmentModelsHandler>()
                        .HandleAsync(exerciseId, new SetExerciseEquipmentModelsRequest([modelId]), default));
            }

            using (var scope = SuperAdmin())
            {
                var set = scope.ServiceProvider.GetRequiredService<SetExerciseEquipmentModelsHandler>();

                await Assert.ThrowsAsync<ValidationDomainException>(() =>
                    set.HandleAsync(exerciseId, new SetExerciseEquipmentModelsRequest([Guid.CreateVersion7()]), default));
                // Un ejercicio de un gym no se liga a modelos.
                await Assert.ThrowsAsync<NotFoundException>(() =>
                    set.HandleAsync(gymExercise, new SetExerciseEquipmentModelsRequest([modelId]), default));

                await set.HandleAsync(exerciseId, new SetExerciseEquipmentModelsRequest([]), default);
                var cleared = await scope.ServiceProvider.GetRequiredService<GetExerciseEquipmentHandler>().HandleAsync(exerciseId, default);
                Assert.Empty(cleared.Models);

                await set.HandleAsync(exerciseId, new SetExerciseEquipmentModelsRequest([modelId, modelId]), default);
                var again = await scope.ServiceProvider.GetRequiredService<GetExerciseEquipmentHandler>().HandleAsync(exerciseId, default);
                Assert.Equal(modelId, Assert.Single(again.Models).Id);
                Assert.Empty(again.Machines);   // un superadmin no tiene gym: sin máquinas resueltas
            }
        }
        finally
        {
            await CleanupAsync([exerciseId], [modelId]);
            using var scope = System();
            await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Exercise>()
                .Where(e => e.Name == "Propio " + world.Suffix).ExecuteDeleteAsync();
        }
    }

    // --- Nivel 1: el filtro "Solo lo que se puede hacer en mi gym" ---

    [Fact]
    public async Task Filter_only_available_here_uses_the_gym_inventory_and_a_gym_without_inventory_filters_nothing()
    {
        var world = await SeedAsync();
        var barbell = await CreateGymExerciseAsync(world, "Con barra", [EquipmentType.Barbell]);
        var machine = await CreateGymExerciseAsync(world, "En máquina", [EquipmentType.Machine]);
        var free = await CreateGymExerciseAsync(world, "Sin equipo", []);
        var body = await CreateGymExerciseAsync(world, "Peso corporal", [EquipmentType.Bodyweight]);
        try
        {
            // Sin inventario: nada se filtra, aunque se pida el filtro.
            var empty = await ListAsync(world.GymA, world.StaffA, world.Suffix, onlyHere: true);
            Assert.Equal(4, empty.Count);
            Assert.All(empty, e => Assert.True(e.IsAvailableHere));

            using (var scope = Staff(world.GymA, world.StaffA))
            {
                await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                    new SaveEquipmentRequest("Prensa " + world.Suffix, EquipmentType.Machine, null, null, null, null, 1, null, null), default);
            }

            // Con inventario (solo máquinas): la barra no se puede; la máquina, lo sin equipo y el peso corporal sí.
            var filtered = await ListAsync(world.GymA, world.StaffA, world.Suffix, onlyHere: true);
            Assert.Equal(new[] { machine, free, body }.Order(), filtered.Select(e => e.Id).Order());

            var all = await ListAsync(world.GymA, world.StaffA, world.Suffix, onlyHere: false);
            Assert.Equal(4, all.Count);
            Assert.False(all.Single(e => e.Id == barbell).IsAvailableHere);

        }
        finally
        {
            await CleanupAsync([barbell, machine, free, body], []);
        }
    }

    // --- Rutina: aviso en el editor y foto, zona e instrucciones en la app ---

    [Fact]
    public async Task Routine_lines_carry_the_machine_photo_zone_instructions_and_a_maintenance_warning()
    {
        var world = await SeedAsync();
        var (exerciseId, modelId) = await CreateSystemExerciseWithModelLinkAsync(world, EquipmentType.Machine, modelImage: "https://img.test/modelo.png");
        var plainId = await CreateGymExerciseAsync(world, "Sin ligas", []);
        try
        {
            Guid machineId;
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                machineId = (await scope.ServiceProvider.GetRequiredService<AddEquipmentFromModelsHandler>().HandleAsync(
                    new AddEquipmentFromModelsRequest([new EquipmentFromModelItem(modelId, 1, "Zona de pierna")]), default)).Single().Id;
                await scope.ServiceProvider.GetRequiredService<UpdateEquipmentHandler>().HandleAsync(
                    machineId,
                    new SaveEquipmentRequest("Prensa 45°", EquipmentType.Machine, "Zona de pierna", null, null, "Asiento en el hueco 4", 1, null, null),
                    default);
            }

            var routineId = await AssignAsync(world, exerciseId, plainId);

            // Editor del staff: la respuesta de la rutina trae la máquina; la foto del modelo cubre la falta de foto propia.
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                var routine = await scope.ServiceProvider.GetRequiredService<GetRoutineHandler>().HandleAsync(routineId, default);
                var lines = routine.Days.SelectMany(d => d.Exercises).ToList();
                var linked = lines.Single(l => l.ExerciseId == exerciseId);
                Assert.True(linked.Equipment!.IsAvailable);
                var info = Assert.Single(linked.Equipment.Machines);
                Assert.Equal("Prensa 45°", info.Name);
                Assert.Equal("Zona de pierna", info.Zone);
                Assert.Equal("Asiento en el hueco 4", info.Instructions);
                Assert.Equal("https://img.test/modelo.png", info.PhotoUrl);
                Assert.Equal("Available", info.Status);

                // Un ejercicio sin ligas y sin tipos de equipo en un gym con inventario: disponible y sin máquinas.
                Assert.Empty(lines.Single(l => l.ExerciseId == plainId).Equipment!.Machines);
            }

            // La foto propia manda sobre la del modelo.
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
                var item = await db.Set<EquipmentItem>().SingleAsync(i => i.Id == machineId);
                item.SetPhoto("https://img.test/propia.jpg");
                await db.SaveChangesAsync();
            }

            using (var scope = Staff(world.GymA, world.StaffA))
            {
                var routine = await scope.ServiceProvider.GetRequiredService<GetRoutineHandler>().HandleAsync(routineId, default);
                Assert.Equal("https://img.test/propia.jpg",
                    routine.Days.SelectMany(d => d.Exercises).Single(l => l.ExerciseId == exerciseId).Equipment!.Machines.Single().PhotoUrl);
            }

            // Mantenimiento: el aviso del editor y la app del miembro; guardar sigue permitido (la rutina dura semanas).
            using (var scope = Staff(world.GymA, world.StaffA))
            {
                await scope.ServiceProvider.GetRequiredService<ChangeEquipmentStatusHandler>().HandleAsync(
                    machineId,
                    new ChangeEquipmentStatusRequest(EquipmentStatus.Maintenance, "Cable", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))),
                    default);
            }

            using (var scope = AsMember(world))
            {
                var mine = await scope.ServiceProvider.GetRequiredService<GetMyRoutineHandler>().HandleAsync(default);
                var line = mine.Routine!.Days.SelectMany(d => d.Exercises).Single(l => l.ExerciseId == exerciseId);
                Assert.False(line.Equipment!.IsAvailable);
                Assert.Equal("Maintenance", line.Equipment.Machines.Single().Status);
                Assert.NotNull(line.Equipment.Machines.Single().ExpectedBackOn);

                var today = mine.Today!.Exercises.SingleOrDefault(l => l.ExerciseId == exerciseId);
                if (today is not null)
                {
                    Assert.False(today.Equipment!.IsAvailable);
                }
            }

            using (var scope = Staff(world.GymA, world.StaffA))
            {
                var saved = await scope.ServiceProvider.GetRequiredService<UpdateRoutineHandler>().HandleAsync(
                    routineId,
                    new SaveRoutineRequest("Rutina E3 v2", null,
                    [
                        new RoutineDayInput("D1", false,
                            [new RoutineExerciseInput(exerciseId, TestSets.Normal(3, 10), 60, null)]),
                    ]),
                    default);
                Assert.Equal("Rutina E3 v2", saved.Name);   // guardar no se impide
                Assert.False(saved.Days.Single().Exercises.Single().Equipment!.IsAvailable);
            }
        }
        finally
        {
            await CleanupAsync([exerciseId, plainId], [modelId]);
        }
    }

    [Fact]
    public async Task A_gym_without_inventory_gets_no_equipment_info_in_its_routines()
    {
        var world = await SeedAsync();
        var exerciseId = await CreateGymExerciseAsync(world, "Cualquiera", [EquipmentType.Machine]);
        try
        {
            var routineId = await AssignAsync(world, exerciseId);
            using var scope = Staff(world.GymA, world.StaffA);
            var routine = await scope.ServiceProvider.GetRequiredService<GetRoutineHandler>().HandleAsync(routineId, default);
            Assert.All(routine.Days.SelectMany(d => d.Exercises), l => Assert.Null(l.Equipment));
        }
        finally
        {
            await CleanupAsync([exerciseId], []);
        }
    }

    // --- Contrato HTTP ---

    [Fact]
    public async Task Http_endpoints_link_machines_and_filter_the_catalog()
    {
        var world = await SeedHttpAsync();
        var exerciseId = await CreateGymExerciseAsync(
            new World(world.GymId, world.StaffId, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, world.Suffix), "Remo http", [EquipmentType.Barbell]);
        try
        {
            await using var host = RealIdentityHost();
            var staff = await LoggedInAsync(host, world.StaffEmail);
            var member = await LoggedInAsync(host, world.MemberEmail);

            var machine = await staff.PostAsJsonAsync("/api/equipment", new { name = "Polea http", type = "Cable", quantity = 1 });
            Assert.Equal(HttpStatusCode.Created, machine.StatusCode);
            var machineId = (await machine.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            // El catálogo filtrado: el gym solo tiene una polea, así que la barra queda fuera.
            var filtered = await staff.GetFromJsonAsync<JsonElement>($"/api/exercises?search=Remo%20http&onlyAvailableHere=true");
            Assert.Empty(filtered.EnumerateArray());
            var unfiltered = await staff.GetFromJsonAsync<JsonElement>("/api/exercises?search=Remo%20http");
            Assert.False(unfiltered[0].GetProperty("isAvailableHere").GetBoolean());

            // Ligarlo a la polea lo vuelve posible (regla 1 sobre la 2).
            var put = await staff.PutAsJsonAsync($"/api/exercises/{exerciseId}/equipment-items", new { itemIds = new[] { machineId } });
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

            var links = await staff.GetFromJsonAsync<JsonElement>($"/api/exercises/{exerciseId}/equipment");
            Assert.Equal(machineId, links.GetProperty("itemIds")[0].GetGuid());
            Assert.True(links.GetProperty("isAvailableHere").GetBoolean());
            Assert.Equal("Polea http", links.GetProperty("machines")[0].GetProperty("name").GetString());
            var nowFiltered = await staff.GetFromJsonAsync<JsonElement>("/api/exercises?search=Remo%20http&onlyAvailableHere=true");
            Assert.Single(nowFiltered.EnumerateArray());

            // El miembro no administra las ligas; ligar a modelos exige superadmin.
            Assert.Equal(HttpStatusCode.Forbidden,
                (await member.PutAsJsonAsync($"/api/exercises/{exerciseId}/equipment-items", new { itemIds = Array.Empty<Guid>() })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await staff.PutAsJsonAsync($"/api/exercises/{exerciseId}/equipment-models", new { modelIds = Array.Empty<Guid>() })).StatusCode);

            // Una máquina inexistente es 422.
            Assert.Equal(HttpStatusCode.UnprocessableEntity,
                (await staff.PutAsJsonAsync($"/api/exercises/{exerciseId}/equipment-items", new { itemIds = new[] { Guid.NewGuid() } })).StatusCode);
        }
        finally
        {
            await CleanupAsync([exerciseId], []);
        }
    }

    // --- Ayudas ---

    private async Task<(Guid ExerciseId, Guid ModelId)> CreateSystemExerciseWithModelLinkAsync(
        World world, EquipmentType type, string? modelImage = null)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var model = EquipmentModel.Create($"Modelo E3 {world.Suffix}", type, null, "Ajusta el respaldo", true, modelImage);
        var exercise = Exercise.Create(
            null, $"Ejercicio sistema E3 {world.Suffix}", null, Muscle.Quadriceps, null, [type], null, null,
            ExerciseType.Strength, null, false, Difficulty.Intermediate);
        db.AddRange(model, exercise);
        await db.SaveChangesAsync();

        db.Add(ExerciseEquipmentModel.Create(exercise.Id, model.Id));
        await db.SaveChangesAsync();
        return (exercise.Id, model.Id);
    }

    private async Task<Guid> CreateGymExerciseAsync(World world, string name, IReadOnlyList<EquipmentType> equipment)
    {
        using var scope = Staff(world.GymA, world.StaffA);
        return (await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>().HandleAsync(
            new CreateExerciseRequest($"{name} {world.Suffix}", null, Muscle.Chest, null, equipment, null, null), default)).Id;
    }

    private async Task<ExerciseResponse> GetExerciseAsync(Guid gymId, Guid staffId, Guid exerciseId)
    {
        using var scope = Staff(gymId, staffId);
        return await scope.ServiceProvider.GetRequiredService<GetExerciseHandler>().HandleAsync(exerciseId, default);
    }

    private async Task<IReadOnlyList<ExerciseResponse>> ListAsync(Guid gymId, Guid staffId, string search, bool onlyHere)
    {
        using var scope = Staff(gymId, staffId);
        return await scope.ServiceProvider.GetRequiredService<ListExercisesHandler>().HandleAsync(
            new ExerciseFilter(search, null, null, false, OnlyAvailableHere: onlyHere), default);
    }

    private async Task<Guid> AssignAsync(World world, params Guid[] exerciseIds)
    {
        using var scope = Staff(world.GymA, world.StaffA);
        var routine = await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>().HandleAsync(
            world.MemberId,
            new SaveRoutineRequest("Rutina E3", null,
            [
                new RoutineDayInput("D1", false,
                    exerciseIds.Select(id => new RoutineExerciseInput(id, TestSets.Normal(3, 10), 60, null)).ToList()),
            ]),
            default);
        return routine.Id;
    }

    /// <summary>Borra lo desechable: las ligas caen con el ejercicio (cascada) y las máquinas del gym con sus gyms.</summary>
    private async Task CleanupAsync(IEnumerable<Guid> exerciseIds, IEnumerable<Guid> modelIds)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var ex = exerciseIds.ToList();
        var models = modelIds.ToList();

        await db.Database.ExecuteSqlRawAsync(
            "delete from routines.routine_exercises where exercise_id = any({0})", ex.ToArray());
        await db.Set<Exercise>()
            .Where(e => ex.Contains(e.Id) && (e.GymId != null || e.Name.StartsWith("Ejercicio sistema E3")))
            .ExecuteDeleteAsync();
        if (models.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                "delete from equipment.equipment_status_changes where item_id in "
                + "(select id from equipment.equipment_items where model_id = any({0}))", models.ToArray());
            await db.Database.ExecuteSqlRawAsync(
                "delete from equipment.equipment_items where model_id = any({0})", models.ToArray());
            await db.Set<EquipmentModel>().Where(m => models.Contains(m.Id) && m.Name.StartsWith("Modelo E3")).ExecuteDeleteAsync();
        }
    }

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

    private IServiceScope SuperAdmin()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), null, UserRole.SuperAdmin);
        return scope;
    }

    private IServiceScope AsMember(World world)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(world.MemberUserId, world.GymA, UserRole.Member);
        return scope;
    }

    private sealed record World(
        Guid GymA, Guid StaffA, Guid MemberId, Guid MemberUserId, Guid GymB, Guid StaffB, string Suffix);

    private async Task<World> SeedAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gymA = Gym.Create("E3 A", $"e3-a-{suffix}");
        var gymB = Gym.Create("E3 B", $"e3-b-{suffix}");
        var memberUser = MakeUser($"e3-m-{suffix}");
        var member = Member.Create(gymA.Id, "Miembro", "E3", $"doc-e3-{suffix}", new DateOnly(2026, 1, 1));
        member.LinkUser(memberUser.Id);

        db.AddRange(gymA, gymB, GymFeatureFlags.Default(gymA.Id), GymFeatureFlags.Default(gymB.Id), memberUser, member);
        await db.SaveChangesAsync();

        return new World(gymA.Id, Guid.CreateVersion7(), member.Id, memberUser.Id, gymB.Id, Guid.CreateVersion7(), suffix);
    }

    private static GSM.Infrastructure.Identity.AppUser MakeUser(string local)
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

    // --- Identidad real (patrón SubscriptionGateHttpTests) ---

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();

        var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.Tokens!.AccessToken);
        return client;
    }

    private sealed record HttpWorld(Guid GymId, Guid StaffId, string StaffEmail, string MemberEmail, string Suffix);

    private async Task<HttpWorld> SeedHttpAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("E3 Http", $"e3-http-{suffix}");
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var ownerEmail = $"e3-owner-{suffix}@test.local";
        var staffEmail = $"e3-staff-{suffix}@test.local";
        var memberEmail = $"e3-member-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "E3 Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "E3 Staff", Password, UserRole.Staff, gym.Id);
        await provisioning.CreateMemberUserAsync(memberEmail, "E3 Member", Password, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new HttpWorld(gym.Id, staffId, staffEmail, memberEmail, suffix);
    }
}
