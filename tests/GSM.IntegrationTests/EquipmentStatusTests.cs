using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Equipment;
using GSM.Application.Equipment.Abstractions;
using GSM.Application.Equipment.Contracts;
using GSM.Application.Equipment.Public;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// E2: estado de las máquinas (disponible, mantenimiento, fuera de servicio), su historial y la
/// disponibilidad de tipos de equipo del gym (nivel 1) que Routines leerá por <see cref="IEquipmentReader"/>.
/// </summary>
public sealed class EquipmentStatusTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    // --- Criterio 1: el cambio deja historial con autor, instante y motivo ---

    [Fact]
    public async Task Changing_status_records_history_with_author_time_and_reason()
    {
        var world = await SeedHttpAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, world.StaffEmail);
        var id = await CreateMachineAsync(staff, "Prensa 45");

        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var toMaintenance = await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new
        {
            status = "Maintenance",
            reason = "Se soltó el cable",
            expectedBackOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)).ToString("yyyy-MM-dd"),
        });
        Assert.Equal(HttpStatusCode.OK, toMaintenance.StatusCode);
        var item = await toMaintenance.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Maintenance", item.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("expectedBackOn").ValueKind);
        Assert.False(item.GetProperty("isOverdue").GetBoolean());

        // Volver a disponible no exige motivo y borra la fecha esperada.
        var back = await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Available" });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        var availableItem = await back.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Available", availableItem.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, availableItem.GetProperty("expectedBackOn").ValueKind);

        var history = await staff.GetFromJsonAsync<JsonElement>($"/api/equipment/{id}/history");
        var rows = history.EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);

        // El más reciente primero.
        Assert.Equal("Maintenance", rows[0].GetProperty("fromStatus").GetString());
        Assert.Equal("Available", rows[0].GetProperty("toStatus").GetString());
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("reason").ValueKind);

        Assert.Equal("Available", rows[1].GetProperty("fromStatus").GetString());
        Assert.Equal("Maintenance", rows[1].GetProperty("toStatus").GetString());
        Assert.Equal("Se soltó el cable", rows[1].GetProperty("reason").GetString());
        Assert.Equal("Eq Staff", rows[1].GetProperty("changedByName").GetString());
        Assert.Equal(world.StaffId, rows[1].GetProperty("changedByUserId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, rows[1].GetProperty("expectedBackOn").ValueKind);

        var at = rows[1].GetProperty("changedAt").GetDateTimeOffset();
        Assert.InRange(at, before, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Equal(TimeSpan.Zero, at.Offset);   // instante UTC
    }

    [Fact]
    public async Task Status_can_move_between_maintenance_and_out_of_service()
    {
        var (gymId, staffId) = await SeedGymAsync("st-move");
        using var scope = Staff(gymId, staffId);
        var id = await CreateAsync(scope, "Polea");
        var change = scope.ServiceProvider.GetRequiredService<ChangeEquipmentStatusHandler>();

        await change.HandleAsync(id, new ChangeEquipmentStatusRequest(EquipmentStatus.Maintenance, "Revisión", null), default);
        var broken = await change.HandleAsync(
            id, new ChangeEquipmentStatusRequest(EquipmentStatus.OutOfService, "Se rompió el eje", null), default);
        Assert.Equal("OutOfService", broken.Status);

        var history = await scope.ServiceProvider.GetRequiredService<GetEquipmentHistoryHandler>().HandleAsync(id, default);
        Assert.Equal(["OutOfService", "Maintenance"], history.Select(h => h.ToStatus));
        Assert.All(history, h => Assert.Equal(staffId, h.ChangedByUserId));
    }

    // --- Criterio 2: transiciones no permitidas → 422, choques → 409 ---

    [Fact]
    public async Task Invalid_changes_are_rejected_and_leave_no_history()
    {
        var world = await SeedHttpAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, world.StaffEmail);
        var owner = await LoggedInAsync(host, world.OwnerEmail);
        var id = await CreateMachineAsync(staff, "Remo");

        // Sin motivo para sacarla de servicio: 422 (también con motivo en blanco).
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Maintenance" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "OutOfService", reason = "   " })).StatusCode);

        // Fecha de regreso en el pasado: 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new
            {
                status = "Maintenance", reason = "x", expectedBackOn = "2020-01-01",
            })).StatusCode);

        // Forma inválida (estado desconocido, motivo larguísimo): 400.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Roto" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new
            {
                status = "Maintenance", reason = new string('x', 501),
            })).StatusCode);

        // Ya está disponible: 409.
        Assert.Equal(HttpStatusCode.Conflict,
            (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Available" })).StatusCode);

        // Estado repetido: 409.
        Assert.Equal(HttpStatusCode.OK,
            (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Maintenance", reason = "ok" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Maintenance", reason = "otra vez" })).StatusCode);

        // Inexistente: 404.
        Assert.Equal(HttpStatusCode.NotFound,
            (await staff.PostAsJsonAsync($"/api/equipment/{Guid.CreateVersion7()}/status", new { status = "Available" })).StatusCode);

        // Solo quedó el único cambio válido.
        Assert.Single((await staff.GetFromJsonAsync<JsonElement>($"/api/equipment/{id}/history")).EnumerateArray());

        // Una máquina dada de baja no cambia de estado: 409.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/equipment/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Available" })).StatusCode);
    }

    [Fact]
    public async Task Repeating_the_status_with_a_new_back_date_extends_the_repair_and_is_recorded()
    {
        var world = await SeedHttpAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, world.StaffEmail);
        var id = await CreateMachineAsync(staff, "Prensa");
        var friday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        var nextWeek = friday.AddDays(7);

        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new
        {
            status = "Maintenance", reason = "Cambio de cable", expectedBackOn = friday.ToString("yyyy-MM-dd"),
        })).StatusCode);

        // El técnico avisa que tarda una semana más: mismo estado, fecha distinta y motivo.
        var extended = await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new
        {
            status = "Maintenance", reason = "El repuesto llega tarde", expectedBackOn = nextWeek.ToString("yyyy-MM-dd"),
        });
        Assert.Equal(HttpStatusCode.OK, extended.StatusCode);
        Assert.Equal(nextWeek.ToString("yyyy-MM-dd"),
            (await extended.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expectedBackOn").GetString());

        // La misma fecha otra vez no es un cambio: 409. Sin motivo: 422.
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new
        {
            status = "Maintenance", reason = "x", expectedBackOn = nextWeek.ToString("yyyy-MM-dd"),
        })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new
        {
            status = "Maintenance", expectedBackOn = nextWeek.AddDays(1).ToString("yyyy-MM-dd"),
        })).StatusCode);

        var history = (await staff.GetFromJsonAsync<JsonElement>($"/api/equipment/{id}/history")).EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal("El repuesto llega tarde", history[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Domain_rules_throw_the_expected_exceptions()
    {
        var (gymId, staffId) = await SeedGymAsync("st-dom");
        using var scope = Staff(gymId, staffId);
        var id = await CreateAsync(scope, "Banco");
        var change = scope.ServiceProvider.GetRequiredService<ChangeEquipmentStatusHandler>();
        var today = scope.ServiceProvider.GetRequiredService<IClock>().Today;

        await Assert.ThrowsAsync<ValidationDomainException>(() => change.HandleAsync(
            id, new ChangeEquipmentStatusRequest(EquipmentStatus.Maintenance, null, null), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() => change.HandleAsync(
            id, new ChangeEquipmentStatusRequest(EquipmentStatus.Maintenance, "x", today.AddDays(-1)), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() => change.HandleAsync(
            id, new ChangeEquipmentStatusRequest((EquipmentStatus)99, "x", null), default));
        await Assert.ThrowsAsync<ConflictException>(() => change.HandleAsync(
            id, new ChangeEquipmentStatusRequest(EquipmentStatus.Available, null, null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => change.HandleAsync(
            Guid.CreateVersion7(), new ChangeEquipmentStatusRequest(EquipmentStatus.Available, null, null), default));

        // La fecha de hoy sí vale; con "disponible" la fecha se ignora.
        var ok = await change.HandleAsync(
            id, new ChangeEquipmentStatusRequest(EquipmentStatus.Maintenance, "x", today), default);
        Assert.Equal(today, ok.ExpectedBackOn);
        var back = await change.HandleAsync(
            id, new ChangeEquipmentStatusRequest(EquipmentStatus.Available, null, today.AddDays(3)), default);
        Assert.Null(back.ExpectedBackOn);
    }

    [Fact]
    public async Task Machines_past_their_expected_date_are_overdue_without_any_job()
    {
        var (gymId, staffId) = await SeedGymAsync("st-over");
        using var scope = Staff(gymId, staffId);
        var clock = scope.ServiceProvider.GetRequiredService<MutableClock>();
        var day = new DateOnly(2026, 11, 10);
        clock.PinToBusinessDay(day);

        try
        {
            var id = await CreateAsync(scope, "Cinta");
            var change = scope.ServiceProvider.GetRequiredService<ChangeEquipmentStatusHandler>();
            await change.HandleAsync(
                id, new ChangeEquipmentStatusRequest(EquipmentStatus.OutOfService, "Motor", day.AddDays(2)), default);

            var get = scope.ServiceProvider.GetRequiredService<GetEquipmentHandler>();
            Assert.False((await get.HandleAsync(id, default)).IsOverdue);

            clock.PinToBusinessDay(day.AddDays(2));   // el día esperado todavía no está vencida
            Assert.False((await get.HandleAsync(id, default)).IsOverdue);

            clock.PinToBusinessDay(day.AddDays(3));
            Assert.True((await get.HandleAsync(id, default)).IsOverdue);

            // Sin fecha esperada nunca vence.
            var other = await CreateAsync(scope, "Cinta 2");
            await change.HandleAsync(other, new ChangeEquipmentStatusRequest(EquipmentStatus.OutOfService, "Motor", null), default);
            Assert.False((await get.HandleAsync(other, default)).IsOverdue);
        }
        finally
        {
            clock.Unpin();
        }
    }

    // --- Criterio 3: filtros por estado ---

    [Fact]
    public async Task Inventory_filters_by_each_status_through_http()
    {
        var world = await SeedHttpAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, world.StaffEmail);

        var a = await CreateMachineAsync(staff, "Máquina A");
        var b = await CreateMachineAsync(staff, "Máquina B");
        await CreateMachineAsync(staff, "Máquina C");

        await staff.PostAsJsonAsync($"/api/equipment/{a}/status", new { status = "Maintenance", reason = "m" });
        await staff.PostAsJsonAsync($"/api/equipment/{b}/status", new { status = "OutOfService", reason = "f" });

        Assert.Equal(["Máquina A"], await ListNamesAsync(staff, "/api/equipment?status=Maintenance"));
        Assert.Equal(["Máquina B"], await ListNamesAsync(staff, "/api/equipment?status=OutOfService"));
        Assert.Equal(["Máquina C"], await ListNamesAsync(staff, "/api/equipment?status=Available"));
        Assert.Equal(3, (await ListNamesAsync(staff, "/api/equipment")).Count);
    }

    // --- Criterio 4: permisos ---

    [Fact]
    public async Task Owner_and_staff_change_status_but_members_and_anonymous_cannot()
    {
        var world = await SeedHttpAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, world.OwnerEmail);
        var staff = await LoggedInAsync(host, world.StaffEmail);
        var member = await LoggedInAsync(host, world.MemberEmail);
        var anonymous = host.CreateClient();

        var id = await CreateMachineAsync(staff, "Prensa");

        Assert.Equal(HttpStatusCode.OK,
            (await owner.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Maintenance", reason = "dueño" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await staff.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Available" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Maintenance", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/api/equipment/{id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync($"/api/equipment/{id}/status", new { status = "Maintenance", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/equipment/{id}/history")).StatusCode);

        // Ver las bajas sigue siendo solo del Owner.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/equipment?includeInactive=true")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/equipment?includeInactive=true")).StatusCode);

        // Y los dos ven el mismo historial, con sus nombres.
        var history = (await owner.GetFromJsonAsync<JsonElement>($"/api/equipment/{id}/history")).EnumerateArray().ToList();
        Assert.Equal(["Eq Staff", "Eq Owner"], history.Select(h => h.GetProperty("changedByName").GetString()));
    }

    [Fact]
    public async Task One_gym_cannot_change_or_read_the_history_of_another_gyms_machine()
    {
        var (gymA, staffA, gymB, staffB) = await SeedTwoGymsAsync();

        Guid machine;
        using (var scope = Staff(gymA, staffA))
        {
            machine = await CreateAsync(scope, "De A");
        }

        using (var scope = Staff(gymB, staffB))
        {
            await Assert.ThrowsAsync<NotFoundException>(() =>
                scope.ServiceProvider.GetRequiredService<ChangeEquipmentStatusHandler>().HandleAsync(
                    machine, new ChangeEquipmentStatusRequest(EquipmentStatus.OutOfService, "x", null), default));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                scope.ServiceProvider.GetRequiredService<GetEquipmentHistoryHandler>().HandleAsync(machine, default));
        }
    }

    // --- Nivel 1: tipos disponibles del gym ---

    [Fact]
    public async Task Gym_availability_follows_the_status_of_its_machines_and_no_inventory_filters_nothing()
    {
        var (gymA, staffA, gymB, _) = await SeedTwoGymsAsync();
        var reader = factory.CreateScopeWithMigration().ServiceProvider.GetRequiredService<IEquipmentReader>();

        // Sin inventario: no se filtra nada.
        var empty = await reader.GetAvailabilityAsync(gymA, default);
        Assert.False(empty.HasInventory);
        Assert.True(empty.CoversAll([EquipmentType.Machine, EquipmentType.Barbell]));

        Guid press, bike;
        using (var scope = Staff(gymA, staffA))
        {
            press = await CreateAsync(scope, "Prensa", EquipmentType.Machine);
            bike = await CreateAsync(scope, "Bici", EquipmentType.Cardio_machine);
        }

        var withInventory = await reader.GetAvailabilityAsync(gymA, default);
        Assert.True(withInventory.HasInventory);
        Assert.True(withInventory.CoversAll([EquipmentType.Machine, EquipmentType.Cardio_machine, EquipmentType.Bodyweight]));
        Assert.False(withInventory.CoversAll([EquipmentType.Barbell]));   // el gym tiene inventario y no tiene barras
        Assert.True(withInventory.CoversAll([]));

        // Una máquina en mantenimiento deja de contar su tipo; el peso corporal siempre cuenta.
        using (var scope = Staff(gymA, staffA))
        {
            await scope.ServiceProvider.GetRequiredService<ChangeEquipmentStatusHandler>().HandleAsync(
                press, new ChangeEquipmentStatusRequest(EquipmentStatus.Maintenance, "arreglo", null), default);
        }

        var afterChange = await reader.GetAvailabilityAsync(gymA, default);
        Assert.False(afterChange.AvailableTypes.Contains(EquipmentType.Machine));
        Assert.True(afterChange.AvailableTypes.Contains(EquipmentType.Cardio_machine));
        Assert.True(afterChange.AvailableTypes.Contains(EquipmentType.Bodyweight));
        Assert.False(afterChange.CoversAll([EquipmentType.Machine]));

        // Dos unidades del mismo tipo: basta con que una esté disponible.
        using (var scope = Staff(gymA, staffA))
        {
            await CreateAsync(scope, "Prensa 2", EquipmentType.Machine);
        }

        Assert.True((await reader.GetAvailabilityAsync(gymA, default)).AvailableTypes.Contains(EquipmentType.Machine));

        // Cada gym ve lo suyo.
        Assert.False((await reader.GetAvailabilityAsync(gymB, default)).HasInventory);
        _ = bike;
    }

    // --- helpers ---

    private static async Task<Guid> CreateMachineAsync(HttpClient client, string name)
    {
        var create = await client.PostAsJsonAsync("/api/equipment", new
        {
            name, type = "Machine", zone = "Pierna", quantity = 1,
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        return (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateAsync(IServiceScope scope, string name, EquipmentType type = EquipmentType.Machine)
        => (await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
            new SaveEquipmentRequest(name, type, "Zona X", null, null, null, 1, null, null), default)).Id;

    private static async Task<List<string>> ListNamesAsync(HttpClient client, string url)
    {
        var json = await client.GetFromJsonAsync<JsonElement>(url);
        return json.EnumerateArray().Select(e => e.GetProperty("name").GetString()!).Order().ToList();
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

    private async Task<(Guid GymA, Guid StaffA, Guid GymB, Guid StaffB)> SeedTwoGymsAsync(string slugPrefix = "eq2")
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

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Equipo Estado", $"eq-st-{suffix}");
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var ownerEmail = $"eqs-owner-{suffix}@test.local";
        var staffEmail = $"eqs-staff-{suffix}@test.local";
        var memberEmail = $"eqs-member-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Eq Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Eq Staff", Password, UserRole.Staff, gym.Id);
        await provisioning.CreateMemberUserAsync(memberEmail, "Eq Member", Password, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new HttpWorld(gym.Id, ownerEmail, staffEmail, memberEmail, staffId);
    }

    private sealed record HttpWorld(Guid GymId, string OwnerEmail, string StaffEmail, string MemberEmail, Guid StaffId);
}
