using GSM.API.Auth;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.Application.Common.Abstractions;
using GSM.Application.Equipment;
using GSM.Application.Equipment.Abstractions;
using GSM.Application.Equipment.Contracts;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
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
/// E1: catálogo de modelos del sistema (solo lectura aquí: es dato sembrado por la migración), inventario
/// del gym y el traslado de <see cref="EquipmentType"/> desde Routines.
/// </summary>
public sealed class EquipmentInventoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    // --- Traslado del enum (criterio 1) ---

    [Fact]
    public async Task Equipment_type_lives_in_the_equipment_schema_and_exercises_keep_their_equipment()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var schemas = await db.Database
            .SqlQueryRaw<string>(
                "select n.nspname as \"Value\" from pg_type t join pg_namespace n on n.oid = t.typnamespace "
                + "where t.typname in ('equipment', 'equipment_type')")
            .ToListAsync();
        Assert.Equal(["equipment"], schemas);   // ya no existe routines.equipment, solo equipment.equipment_type

        var exercises = await db.Set<Exercise>().Where(e => e.GymId == null).ToListAsync();
        Assert.True(exercises.Count >= 40);
        Assert.All(exercises, e => Assert.NotEmpty(e.Equipment));
        Assert.Contains(exercises, e => e.Equipment.Contains(EquipmentType.Barbell));
        Assert.Contains(exercises, e => e.Equipment.Contains(EquipmentType.Dumbbell));
    }

    [Fact]
    public async Task Api_keeps_serializing_equipment_with_the_same_names_as_before()
    {
        var world = await SeedHttpAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, world.OwnerEmail);

        // Un ejercicio propio con el valor que más cambia de forma ("Smith_machine"), ida y vuelta.
        var name = $"Press multipower {Guid.NewGuid():N}"[..24];
        var create = await owner.PostAsJsonAsync("/api/exercises", new
        {
            name,
            primaryMuscle = "Chest",
            equipment = new[] { "Smith_machine", "Bench" },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        var equipment = body.GetProperty("equipment").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["Smith_machine", "Bench"], equipment);

        // Y el filtro por query string sigue aceptando el mismo texto.
        var filtered = await owner.GetFromJsonAsync<JsonElement>("/api/exercises?equipment=Smith_machine");
        Assert.Contains(filtered.EnumerateArray(), e => e.GetProperty("name").GetString() == name);

        // Todo el catálogo del sistema sale con nombres del enum.
        var all = await owner.GetFromJsonAsync<JsonElement>("/api/exercises");
        var valid = Enum.GetNames<EquipmentType>().ToHashSet();
        Assert.All(
            all.EnumerateArray().SelectMany(e => e.GetProperty("equipment").EnumerateArray()),
            v => Assert.Contains(v.GetString(), valid));
    }

    // --- Catálogo del sistema (criterio 2) ---

    [Fact]
    public async Task System_catalog_is_seeded_and_filters_by_type()
    {
        var (gymId, staffId) = await SeedGymAsync("cat-models");
        using var scope = Staff(gymId, staffId);
        var list = scope.ServiceProvider.GetRequiredService<ListEquipmentModelsHandler>();

        var all = await list.HandleAsync(null, default);
        Assert.True(all.Count >= 40, $"esperaba >= 40 modelos, hay {all.Count}");
        Assert.Equal(all.Count, all.Select(m => m.Name).Distinct().Count());

        // Cubre los tipos principales que pide la especificación.
        foreach (var type in new[] { EquipmentType.Machine, EquipmentType.Cable, EquipmentType.Smith_machine,
                     EquipmentType.Bench, EquipmentType.Rack, EquipmentType.Cardio_machine,
                     EquipmentType.Dumbbell, EquipmentType.Barbell, EquipmentType.Kettlebell })
        {
            Assert.Contains(all, m => m.Type == type.ToString());
        }

        var cardio = await list.HandleAsync(EquipmentType.Cardio_machine, default);
        Assert.NotEmpty(cardio);
        Assert.All(cardio, m => Assert.Equal("Cardio_machine", m.Type));
        Assert.Contains(cardio, m => m.Name == "Bicicleta estática" && m.TrackedPerUnit);

        var dumbbells = await list.HandleAsync(EquipmentType.Dumbbell, default);
        Assert.All(dumbbells, m => Assert.False(m.TrackedPerUnit));
        Assert.All(dumbbells, m => Assert.Null(m.ImageUrl));   // sin imágenes: la UI muestra un ícono por tipo
    }

    // --- Agregar del catálogo (criterio 3) ---

    [Fact]
    public async Task Adding_from_the_catalog_creates_numbered_units_or_one_row_with_quantity()
    {
        var (gymId, staffId) = await SeedGymAsync("cat-add");
        var (bike, dumbbells, press) = await ModelIdsAsync();

        using var scope = Staff(gymId, staffId);
        var add = scope.ServiceProvider.GetRequiredService<AddEquipmentFromModelsHandler>();

        var created = await add.HandleAsync(new AddEquipmentFromModelsRequest(
        [
            new EquipmentFromModelItem(bike, 3, "Cardio"),
            new EquipmentFromModelItem(dumbbells, 20, "Peso libre"),
            new EquipmentFromModelItem(press, 1, null),
        ]), default);

        var bikes = created.Where(c => c.ModelId == bike).OrderBy(c => c.Name).ToList();
        Assert.Equal(
            ["Bicicleta estática 1", "Bicicleta estática 2", "Bicicleta estática 3"],
            bikes.Select(b => b.Name));
        Assert.All(bikes, b =>
        {
            Assert.Equal(1, b.Quantity);
            Assert.Equal("Cardio", b.Zone);
            Assert.Equal("Available", b.Status);
            Assert.True(b.IsActive);
            Assert.Equal("Cardio_machine", b.Type);
        });

        var loose = Assert.Single(created, c => c.ModelId == dumbbells);
        Assert.Equal("Mancuernas", loose.Name);
        Assert.Equal(20, loose.Quantity);

        // Una sola unidad y ninguna previa: el nombre queda sin número, con las instrucciones del modelo.
        var single = Assert.Single(created, c => c.ModelId == press);
        Assert.Equal("Prensa de piernas 45°", single.Name);
        Assert.False(string.IsNullOrWhiteSpace(single.Instructions));

        // Agregar más bicicletas sigue la numeración en vez de repetirla.
        var more = await add.HandleAsync(new AddEquipmentFromModelsRequest(
            [new EquipmentFromModelItem(bike, 2, "Cardio")]), default);
        Assert.Equal(
            ["Bicicleta estática 4", "Bicicleta estática 5"],
            more.OrderBy(m => m.Name).Select(m => m.Name));

        var inventory = await scope.ServiceProvider.GetRequiredService<ListEquipmentHandler>()
            .HandleAsync(new EquipmentFilter(), default);
        Assert.Equal(7, inventory.Count);
    }

    [Fact]
    public async Task Adding_an_unknown_model_or_too_many_units_is_rejected()
    {
        var (gymId, staffId) = await SeedGymAsync("cat-bad");
        var (bike, _, _) = await ModelIdsAsync();

        using var scope = Staff(gymId, staffId);
        var add = scope.ServiceProvider.GetRequiredService<AddEquipmentFromModelsHandler>();

        await Assert.ThrowsAsync<NotFoundException>(() => add.HandleAsync(
            new AddEquipmentFromModelsRequest([new EquipmentFromModelItem(Guid.CreateVersion7(), 1, null)]), default));

        await Assert.ThrowsAsync<ValidationDomainException>(() => add.HandleAsync(
            new AddEquipmentFromModelsRequest([new EquipmentFromModelItem(bike, 51, null)]), default));

        // Nada quedó a medias.
        var inventory = await scope.ServiceProvider.GetRequiredService<ListEquipmentHandler>()
            .HandleAsync(new EquipmentFilter(), default);
        Assert.Empty(inventory);
    }

    // --- Inventario por gym, filtros y zonas (criterio 4) ---

    [Fact]
    public async Task Inventory_is_per_gym_and_filters_by_type_zone_and_status()
    {
        var (gymA, staffA, gymB, staffB) = await SeedTwoGymsAsync();
        var (bike, dumbbells, press) = await ModelIdsAsync();

        using (var scope = Staff(gymA, staffA))
        {
            await scope.ServiceProvider.GetRequiredService<AddEquipmentFromModelsHandler>().HandleAsync(
                new AddEquipmentFromModelsRequest(
                [
                    new EquipmentFromModelItem(bike, 2, "Cardio"),
                    new EquipmentFromModelItem(dumbbells, 10, "Peso libre"),
                    new EquipmentFromModelItem(press, 1, "Zona de pierna"),
                ]), default);

            await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                new SaveEquipmentRequest("Máquina hecha en casa", EquipmentType.Machine, "zona de PIERNA",
                    "Taller", "Prototipo", "Con cuidado", 1, new DateOnly(2025, 3, 1), "Soldada"), default);
        }

        // El estado se cambia por E2; aquí se fuerza en la base para probar el filtro.
        using (var scope = System())
        {
            await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Database.ExecuteSqlRawAsync(
                "update equipment.equipment_items set status = 'maintenance' where name = 'Bicicleta estática 2'");
        }

        using (var scope = Staff(gymB, staffB))
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<ListEquipmentHandler>()
                .HandleAsync(new EquipmentFilter(), default));
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<ListEquipmentZonesHandler>()
                .HandleAsync(default));
        }

        using (var scope = Staff(gymA, staffA))
        {
            var list = scope.ServiceProvider.GetRequiredService<ListEquipmentHandler>();

            Assert.Equal(5, (await list.HandleAsync(new EquipmentFilter(), default)).Count);

            var cardio = await list.HandleAsync(new EquipmentFilter(Type: EquipmentType.Cardio_machine), default);
            Assert.Equal(2, cardio.Count);

            // La zona compara sin distinguir mayúsculas.
            var leg = await list.HandleAsync(new EquipmentFilter(Zone: "zona de pierna"), default);
            Assert.Equal(["Máquina hecha en casa", "Prensa de piernas 45°"], leg.Select(l => l.Name).Order());

            var maintenance = await list.HandleAsync(new EquipmentFilter(Status: EquipmentStatus.Maintenance), default);
            Assert.Equal("Bicicleta estática 2", Assert.Single(maintenance).Name);

            var available = await list.HandleAsync(new EquipmentFilter(Status: EquipmentStatus.Available), default);
            Assert.Equal(4, available.Count);

            var combined = await list.HandleAsync(
                new EquipmentFilter(EquipmentType.Cardio_machine, "Cardio", EquipmentStatus.Maintenance), default);
            Assert.Single(combined);

            var zones = await scope.ServiceProvider.GetRequiredService<ListEquipmentZonesHandler>()
                .HandleAsync(default);
            Assert.Contains("Cardio", zones);
            Assert.Contains("Peso libre", zones);
            Assert.Contains("Zona de pierna", zones);

            // No puede leer ni editar una máquina de otro gym (la del gym B no existe aquí; id ajeno → 404).
            await Assert.ThrowsAsync<NotFoundException>(() =>
                scope.ServiceProvider.GetRequiredService<GetEquipmentHandler>()
                    .HandleAsync(Guid.CreateVersion7(), default));
        }
    }

    [Fact]
    public async Task One_gym_cannot_read_edit_or_remove_another_gyms_machine()
    {
        var (gymA, staffA, gymB, staffB) = await SeedTwoGymsAsync();

        Guid machineA;
        using (var scope = Staff(gymA, staffA))
        {
            machineA = (await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                Own("Prensa del gym A"), default)).Id;
        }

        using (var scope = Staff(gymB, staffB))
        {
            await Assert.ThrowsAsync<NotFoundException>(() =>
                scope.ServiceProvider.GetRequiredService<GetEquipmentHandler>().HandleAsync(machineA, default));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                scope.ServiceProvider.GetRequiredService<UpdateEquipmentHandler>()
                    .HandleAsync(machineA, Own("Robada"), default));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                scope.ServiceProvider.GetRequiredService<DeactivateEquipmentHandler>().HandleAsync(machineA, default));
        }
    }

    [Fact]
    public async Task Own_machine_can_be_created_edited_and_validated()
    {
        var (gymId, staffId) = await SeedGymAsync("cat-own");
        using var scope = Staff(gymId, staffId);

        var created = await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
            Own("Mi máquina"), default);
        Assert.Null(created.ModelId);
        Assert.Null(created.ModelImageUrl);
        Assert.False(created.HasOwnPhoto);
        Assert.Equal("Available", created.Status);

        var edited = await scope.ServiceProvider.GetRequiredService<UpdateEquipmentHandler>().HandleAsync(
            created.Id,
            new SaveEquipmentRequest("Mi máquina 2", EquipmentType.Cable, "Segundo piso", "Marca", "M-1",
                "Ajusta el asiento", 2, null, "Nota"),
            default);
        Assert.Equal("Mi máquina 2", edited.Name);
        Assert.Equal("Cable", edited.Type);
        Assert.Equal(2, edited.Quantity);
        Assert.Equal("Segundo piso", edited.Zone);

        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                new SaveEquipmentRequest("X", EquipmentType.Machine, null, null, null, null, 1, null, null), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                new SaveEquipmentRequest("Con cantidad cero", EquipmentType.Machine, null, null, null, null, 0, null, null),
                default));
    }

    // --- Baja lógica solo Owner (criterio 5), por HTTP con la identidad real ---

    [Fact]
    public async Task Only_the_owner_can_deactivate_and_deactivated_machines_leave_the_default_inventory()
    {
        var world = await SeedHttpAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, world.OwnerEmail);
        var staff = await LoggedInAsync(host, world.StaffEmail);

        // El staff crea (puede gestionar el inventario).
        var create = await staff.PostAsJsonAsync("/api/equipment", new
        {
            name = "Remo en polea", type = "Cable", zone = "Espalda", quantity = 1,
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // ...pero no da de baja: 403.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"/api/equipment/{id}")).StatusCode);
        Assert.Contains(await ListNamesAsync(staff, "/api/equipment"), n => n == "Remo en polea");

        // El Owner sí: 204, y sale del inventario por defecto.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/equipment/{id}")).StatusCode);
        Assert.DoesNotContain(await ListNamesAsync(staff, "/api/equipment"), n => n == "Remo en polea");
        Assert.DoesNotContain(await ListNamesAsync(staff, "/api/equipment?zone=Espalda"), n => n == "Remo en polea");

        // Ver las bajas es solo del Owner (P18): el staff recibe 403.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/equipment?includeInactive=true")).StatusCode);

        // Con includeInactive el Owner la ve, marcada como baja; no se puede editar.
        var withInactive = await owner.GetFromJsonAsync<JsonElement>("/api/equipment?includeInactive=true");
        var row = Assert.Single(withInactive.EnumerateArray(), e => e.GetProperty("id").GetGuid() == id);
        Assert.False(row.GetProperty("isActive").GetBoolean());
        var edit = await staff.PutAsJsonAsync($"/api/equipment/{id}", new
        {
            name = "Editada", type = "Cable", quantity = 1,
        });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);

        // Y la zona de una máquina dada de baja ya no se sugiere.
        var zones = await staff.GetFromJsonAsync<string[]>("/api/equipment/zones");
        Assert.DoesNotContain("Espalda", zones!);
    }

    [Fact]
    public async Task Members_cannot_manage_the_inventory_and_http_routes_work_end_to_end()
    {
        var world = await SeedHttpAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, world.StaffEmail);
        var anonymous = host.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/equipment")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/equipment/models")).StatusCode);

        var models = await staff.GetFromJsonAsync<JsonElement>("/api/equipment/models?type=Cardio_machine");
        var bike = Assert.Single(models.EnumerateArray(), m => m.GetProperty("name").GetString() == "Bicicleta estática");

        var add = await staff.PostAsJsonAsync("/api/equipment/from-models", new
        {
            items = new[] { new { modelId = bike.GetProperty("id").GetGuid(), quantity = 3, zone = "Cardio" } },
        });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var rows = await add.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, rows.GetArrayLength());

        // Validación de forma → 400 (lista vacía / cantidad fuera de rango).
        Assert.Equal(HttpStatusCode.BadRequest,
            (await staff.PostAsJsonAsync("/api/equipment/from-models", new { items = Array.Empty<object>() })).StatusCode);

        var byType = await ListNamesAsync(staff, "/api/equipment?type=Cardio_machine&status=Available");
        Assert.Equal(3, byType.Count);
    }

    // --- Foto (criterio de la tarjeta: se construye, sin verificar contra R2) ---

    [Fact]
    public async Task Photo_upload_replaces_the_previous_one_and_rejects_bad_files()
    {
        var (gymId, staffId) = await SeedGymAsync("cat-photo");
        var storage = new FakeStorage();

        Guid id;
        using (var scope = Staff(gymId, staffId))
        {
            id = (await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                Own("Con foto"), default)).Id;

            var upload = new UploadEquipmentPhotoHandler(
                scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
                scope.ServiceProvider.GetRequiredService<IEquipmentItemRepository>(),
                scope.ServiceProvider.GetRequiredService<IEquipmentModelRepository>(),
                scope.ServiceProvider.GetRequiredService<IClock>(),
                storage,
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

            var first = await upload.HandleAsync(id, Png(), default);
            Assert.True(first.HasOwnPhoto);
            Assert.StartsWith("https://files.test/equipment/", first.PhotoUrl);

            var second = await upload.HandleAsync(id, Png(), default);
            Assert.NotEqual(first.PhotoUrl, second.PhotoUrl);
            Assert.Equal([first.PhotoUrl], storage.Deleted);   // la anterior se borra

            await Assert.ThrowsAsync<ValidationDomainException>(() => upload.HandleAsync(
                id, new FileToStore(new MemoryStream([1]), "x.gif", "image/gif", 1), default));
            await Assert.ThrowsAsync<NotFoundException>(() => upload.HandleAsync(Guid.CreateVersion7(), Png(), default));
        }
    }

    // --- helpers ---

    private static SaveEquipmentRequest Own(string name)
        => new(name, EquipmentType.Machine, "Zona X", null, null, null, 1, null, null);

    private static FileToStore Png() => new(new MemoryStream([1, 2, 3]), "m.png", "image/png", 3);

    private static async Task<List<string>> ListNamesAsync(HttpClient client, string url)
    {
        var json = await client.GetFromJsonAsync<JsonElement>(url);
        return json.EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList();
    }

    private async Task<(Guid Bike, Guid Dumbbells, Guid Press)> ModelIdsAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        async Task<Guid> Id(string name) => await db.Set<EquipmentModel>()
            .Where(m => m.Name == name).Select(m => m.Id).SingleAsync();

        return (await Id("Bicicleta estática"), await Id("Mancuernas"), await Id("Prensa de piernas 45°"));
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

    private async Task<(Guid GymId, Guid StaffId)> SeedGymAsync(string slugPrefix)
    {
        var (a, s, _, _) = await SeedTwoGymsAsync(slugPrefix);
        return (a, s);
    }

    private async Task<(Guid GymA, Guid StaffA, Guid GymB, Guid StaffB)> SeedTwoGymsAsync(string slugPrefix = "eq")
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, equipment.equipment_items restart identity cascade;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var a = Gym.Create("Equipo A", $"{slugPrefix}-a-{suffix}");
        var b = Gym.Create("Equipo B", $"{slugPrefix}-b-{suffix}");
        db.AddRange(a, b, GymFeatureFlags.Default(a.Id), GymFeatureFlags.Default(b.Id));
        await db.SaveChangesAsync();

        return (a.Id, Guid.CreateVersion7(), b.Id, Guid.CreateVersion7());
    }

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

    private async Task<HttpWorld> SeedHttpAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, superadmin.gym_subscriptions, "
            + "equipment.equipment_items restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from routines.exercises where gym_id is not null;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Equipo Http", $"eq-http-{suffix}");
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var ownerEmail = $"eq-owner-{suffix}@test.local";
        var staffEmail = $"eq-staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Eq Owner", Password, UserRole.Owner, gym.Id);
        await provisioning.CreateGymUserAsync(staffEmail, "Eq Staff", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new HttpWorld(gym.Id, ownerEmail, staffEmail);
    }

    private sealed record HttpWorld(Guid GymId, string OwnerEmail, string StaffEmail);

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
