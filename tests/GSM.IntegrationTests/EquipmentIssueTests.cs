using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Equipment.Events;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Notifications;
using GSM.Domain.Equipment;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// E4: reportes de falla de máquinas. El miembro (o el staff) reporta; Equipment publica eventos y Notifications crea
/// los avisos (campana, urgente, correo al Owner por «insegura») y avisa al miembro cuando se resuelve.
/// Todas las pruebas usan la identidad real (patrón <c>SubscriptionGateHttpTests</c>).
/// </summary>
public sealed class EquipmentIssueTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    // --- reportar y llegar a la campana ---

    [Fact]
    public async Task A_member_report_reaches_the_staff_bell_and_does_not_change_the_machine_state()
    {
        var w = await SeedAsync(1);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var member = await LoggedInAsync(host, w.Members[0].Email);
        var machine = await CreateMachineAsync(staff, "Prensa 45");

        var response = await member.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason = "DamagedPart", description = "Se soltó un tornillo" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Open", created.GetProperty("status").GetString());
        Assert.Equal("DamagedPart", created.GetProperty("reason").GetString());

        // La campana del staff: un aviso de acción, normal, con la máquina, el motivo y quién reportó.
        var bell = await BellAsync(staff);
        var notice = Assert.Single(bell, n => n.GetProperty("type").GetString() == "EquipmentIssueReported"
                                              && n.GetProperty("title").GetString()!.Contains("Prensa 45"));
        Assert.True(notice.GetProperty("requiresAction").GetBoolean());
        Assert.Equal("Normal", notice.GetProperty("priority").GetString());
        Assert.Contains("pieza dañada", notice.GetProperty("title").GetString());
        Assert.Contains("Miembro 0", notice.GetProperty("body").GetString());
        Assert.Contains("Se soltó un tornillo", notice.GetProperty("body").GetString());
        Assert.StartsWith("/equipment/issues/", notice.GetProperty("link").GetString());

        // El owner también la ve (todo el staff).
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        Assert.Contains(await BellAsync(owner), n => n.GetProperty("type").GetString() == "EquipmentIssueReported");

        // Un reporte no saca la máquina de servicio por sí solo.
        var item = await staff.GetFromJsonAsync<JsonElement>($"/api/equipment/{machine}");
        Assert.Equal("Available", item.GetProperty("status").GetString());

        // Un reporte normal no genera correo.
        Assert.Empty(await DeliveriesOfAsync(w.GymId, NotificationType.EquipmentIssueReported));
    }

    [Fact]
    public async Task With_the_flag_off_the_member_gets_403_but_the_staff_can_still_report()
    {
        var w = await SeedAsync(1, membersCanReport: false);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var member = await LoggedInAsync(host, w.Members[0].Email);
        var machine = await CreateMachineAsync(staff, "Remo");

        var forbidden = await member.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason = "NotWorking" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Empty(await IssuesAsync(w.GymId));

        // El miembro puede ver la máquina, pero se le dice que no puede reportarla.
        var view = await member.GetFromJsonAsync<JsonElement>($"/api/me/equipment/{machine}");
        Assert.False(view.GetProperty("canReport").GetBoolean());

        var byStaff = await staff.PostAsJsonAsync($"/api/equipment/{machine}/issues", new { reason = "Dirty" });
        Assert.Equal(HttpStatusCode.Created, byStaff.StatusCode);
        var issue = Assert.Single(await IssuesAsync(w.GymId));
        Assert.Equal(IssueReporterRole.Staff, issue.ReportedByRole);
        Assert.Equal(w.StaffId, issue.ReportedByUserId);
    }

    [Fact]
    public async Task A_member_has_at_most_one_unclosed_report_per_machine_and_sees_its_status()
    {
        var w = await SeedAsync(2);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var a = await LoggedInAsync(host, w.Members[0].Email);
        var b = await LoggedInAsync(host, w.Members[1].Email);
        var machine = await CreateMachineAsync(staff, "Polea");

        var before = await a.GetFromJsonAsync<JsonElement>($"/api/me/equipment/{machine}");
        Assert.True(before.GetProperty("canReport").GetBoolean());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("openIssue").ValueKind);

        Assert.Equal(HttpStatusCode.Created, (await a.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason = "StrangeNoise" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason = "Dirty" })).StatusCode);

        // Otro miembro sí puede reportar la misma máquina.
        Assert.Equal(HttpStatusCode.Created, (await b.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason = "Dirty" })).StatusCode);

        var after = await a.GetFromJsonAsync<JsonElement>($"/api/me/equipment/{machine}");
        Assert.Equal("Open", after.GetProperty("openIssue").GetProperty("status").GetString());

        // Sus reportes: solo los suyos.
        var mine = await a.GetFromJsonAsync<JsonElement>("/api/me/equipment/issues");
        var row = Assert.Single(mine.EnumerateArray());
        Assert.Equal("StrangeNoise", row.GetProperty("reason").GetString());
        Assert.Equal("Polea", row.GetProperty("itemName").GetString());
    }

    [Fact]
    public async Task Other_needs_a_description_and_the_shape_is_validated()
    {
        var w = await SeedAsync(1);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var member = await LoggedInAsync(host, w.Members[0].Email);
        var machine = await CreateMachineAsync(staff, "Banco");

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await member.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason = "Other" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await member.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason = "Other", description = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await member.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason = "Roto" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await member.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason = "Dirty", description = new string('x', 1001) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await member.PostAsJsonAsync($"/api/me/equipment/{Guid.NewGuid()}/issues", new { reason = "Dirty" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await member.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason = "Other", description = "Chirría al bajar" })).StatusCode);
    }

    [Fact]
    public async Task A_member_cannot_reach_the_staff_endpoints()
    {
        var w = await SeedAsync(1);
        await using var host = RealIdentityHost();
        var member = await LoggedInAsync(host, w.Members[0].Email);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/equipment/issues")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/equipment/summary")).StatusCode);
    }

    // --- agrupar y urgente ---

    [Fact]
    public async Task Several_open_reports_of_the_same_machine_share_one_bell_notice()
    {
        var w = await SeedAsync(2);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var a = await LoggedInAsync(host, w.Members[0].Email);
        var b = await LoggedInAsync(host, w.Members[1].Email);
        var machine = await CreateMachineAsync(staff, "Prensa 45");

        var first = await ReportAsync(a, machine, "NotWorking");
        await ReportAsync(b, machine, "StrangeNoise");

        var notice = Assert.Single(await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported));
        Assert.Equal("2 reportes de Prensa 45", notice.Title);
        Assert.Null(notice.ResolvedAt);

        // Cerrar uno de los dos no atiende el aviso; cerrar el último, sí.
        var firstIssue = first;
        var second = (await IssuesAsync(w.GymId)).Single(i => i.Id != firstIssue).Id;
        await ResolveAsync(staff, firstIssue, "Se ajustó el tornillo");
        Assert.Null((await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported)).Single().ResolvedAt);

        await ResolveAsync(staff, second, "Se lubricó");
        Assert.NotNull((await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported)).Single().ResolvedAt);
    }

    [Fact]
    public async Task An_unsafe_report_is_urgent_first_in_the_bell_and_queues_an_email_to_the_owner()
    {
        var w = await SeedAsync(2);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var a = await LoggedInAsync(host, w.Members[0].Email);
        var b = await LoggedInAsync(host, w.Members[1].Email);
        var machine = await CreateMachineAsync(staff, "Multipower");
        var other = await CreateMachineAsync(staff, "Remo");

        var sender = factory.Services.GetRequiredService<CapturingEmailSender>();
        sender.Clear();

        var unsafeId = await ReportAsync(a, machine, "Unsafe", "La barra se traba");
        await ReportAsync(b, other, "Dirty");   // un reporte normal, más reciente

        // El aviso urgente existe, queda marcado y NO se envió dentro de la petición.
        var unsafeNotice = Assert.Single(await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported), n => n.ReferenceId == unsafeId);
        Assert.Equal(NotificationPriority.Urgent, unsafeNotice.Priority);
        Assert.Contains("URGENTE", unsafeNotice.Title);
        Assert.Null(sender.LastTo(w.OwnerEmail));

        // El correo al Owner quedó en la cola (y solo para el reporte inseguro).
        var deliveries = await DeliveriesOfAsync(w.GymId, NotificationType.EquipmentIssueReported);
        var delivery = Assert.Single(deliveries);
        Assert.Equal(unsafeNotice.Id, delivery.NotificationId);
        Assert.Equal(DeliveryChannel.Email, delivery.Channel);
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.Equal(w.OwnerEmail, delivery.ToAddress, ignoreCase: true);

        // La campana pone el urgente primero aunque haya uno más reciente.
        var bell = await BellAsync(staff);
        var ours = bell.Where(n => n.GetProperty("type").GetString() == "EquipmentIssueReported").ToList();
        Assert.Equal("Urgent", ours[0].GetProperty("priority").GetString());

        // Y el dispatcher lo envía después, a su ritmo.
        for (var i = 0; i < 5 && sender.LastTo(w.OwnerEmail) is null; i++)
        {
            using var scope = factory.CreateScopeWithMigration();
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();
            await scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>().RunOnceAsync();
        }

        var mail = sender.LastTo(w.OwnerEmail);
        Assert.NotNull(mail);
        Assert.Contains("Multipower", mail!.Subject + mail.HtmlBody);

        // Tampoco cambia el estado: «insegura» solo sube la urgencia.
        var item = await staff.GetFromJsonAsync<JsonElement>($"/api/equipment/{machine}");
        Assert.Equal("Available", item.GetProperty("status").GetString());
    }

    // --- atender, resolver, descartar ---

    [Fact]
    public async Task Resolving_requires_a_resolution_thanks_the_member_and_clears_the_bell()
    {
        var w = await SeedAsync(1);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var member = await LoggedInAsync(host, w.Members[0].Email);
        var machine = await CreateMachineAsync(staff, "Prensa 45");
        var issueId = await ReportAsync(member, machine, "NotWorking");

        // Resolver sin explicación: 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await staff.PutAsJsonAsync($"/api/equipment/issues/{issueId}", new { status = "Resolved" })).StatusCode);

        // En revisión primero, luego resuelto.
        var review = await staff.PutAsJsonAsync($"/api/equipment/issues/{issueId}", new { status = "InProgress" });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Equal("InProgress", (await review.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict,
            (await staff.PutAsJsonAsync($"/api/equipment/issues/{issueId}", new { status = "InProgress" })).StatusCode);

        var done = await staff.PutAsJsonAsync($"/api/equipment/issues/{issueId}", new { status = "Resolved", resolution = "Cambié el cable" });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var body = await done.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Resolved", body.GetProperty("status").GetString());
        Assert.Equal("Cambié el cable", body.GetProperty("resolution").GetString());
        Assert.Equal("Eq Staff", body.GetProperty("resolvedByName").GetString());

        // Un reporte cerrado no se vuelve a tocar.
        Assert.Equal(HttpStatusCode.Conflict,
            (await staff.PutAsJsonAsync($"/api/equipment/issues/{issueId}", new { status = "Dismissed", resolution = "x" })).StatusCode);

        // El miembro recibe "gracias por avisar" en su bandeja.
        var thanks = Assert.Single(await NoticesAsync(w.GymId, NotificationType.EquipmentIssueResolved));
        Assert.Equal(w.Members[0].MemberId, thanks.RecipientMemberId);
        Assert.Contains("Prensa 45", thanks.Body);
        Assert.Contains("Gracias por avisar", thanks.Body, StringComparison.OrdinalIgnoreCase);

        // Su vista del reporte ya no está abierto y trae la resolución.
        var mine = (await member.GetFromJsonAsync<JsonElement>("/api/me/equipment/issues")).EnumerateArray().Single();
        Assert.Equal("Resolved", mine.GetProperty("status").GetString());
        Assert.Equal("Cambié el cable", mine.GetProperty("resolution").GetString());

        // El aviso del staff quedó atendido.
        var staffNotice = Assert.Single(await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported));
        Assert.NotNull(staffNotice.ResolvedAt);
    }

    [Fact]
    public async Task A_dismissed_report_clears_the_bell_but_does_not_notify_the_member()
    {
        var w = await SeedAsync(1);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var member = await LoggedInAsync(host, w.Members[0].Email);
        var machine = await CreateMachineAsync(staff, "Banco");
        var issueId = await ReportAsync(member, machine, "Dirty");

        var dismissed = await staff.PutAsJsonAsync($"/api/equipment/issues/{issueId}", new { status = "Dismissed", resolution = "Estaba limpio" });
        Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);

        Assert.Empty(await NoticesAsync(w.GymId, NotificationType.EquipmentIssueResolved));
        Assert.NotNull(Assert.Single(await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported)).ResolvedAt);

        // Descartado, no se explica al miembro.
        var mine = (await member.GetFromJsonAsync<JsonElement>("/api/me/equipment/issues")).EnumerateArray().Single();
        Assert.Equal("Dismissed", mine.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("resolution").ValueKind);
    }

    [Fact]
    public async Task Opening_a_report_marks_it_seen_and_attends_its_bell_notice()
    {
        var w = await SeedAsync(1);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var member = await LoggedInAsync(host, w.Members[0].Email);
        var machine = await CreateMachineAsync(staff, "Prensa 45");
        var issueId = await ReportAsync(member, machine, "NotWorking");

        var listed = (await staff.GetFromJsonAsync<JsonElement>("/api/equipment/issues?openOnly=true")).EnumerateArray().Single();
        Assert.False(listed.GetProperty("isSeen").GetBoolean());
        Assert.Null((await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported)).Single().ResolvedAt);

        var opened = await staff.GetFromJsonAsync<JsonElement>($"/api/equipment/issues/{issueId}");
        Assert.True(opened.GetProperty("isSeen").GetBoolean());
        Assert.Equal("Miembro 0", opened.GetProperty("reportedByName").GetString());

        var notice = (await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported)).Single();
        Assert.NotNull(notice.ResolvedAt);
        Assert.Equal(w.StaffId, notice.ResolvedBy);
        Assert.Equal(0, (await staff.GetFromJsonAsync<JsonElement>("/api/staff/notifications/open-count")).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Attending_the_bell_notice_by_hand_marks_the_open_reports_as_seen()
    {
        var w = await SeedAsync(1);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var member = await LoggedInAsync(host, w.Members[0].Email);
        var machine = await CreateMachineAsync(staff, "Prensa 45");
        await ReportAsync(member, machine, "NotWorking");

        var notice = (await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported)).Single();
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/staff/notifications/{notice.Id}/resolve", null)).StatusCode);

        Assert.NotNull((await IssuesAsync(w.GymId)).Single().SeenAt);
    }

    // --- idempotencia ---

    [Fact]
    public async Task The_event_handlers_are_idempotent()
    {
        var w = await SeedAsync(1);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var member = await LoggedInAsync(host, w.Members[0].Email);
        var machine = await CreateMachineAsync(staff, "Prensa 45");
        var issueId = await ReportAsync(member, machine, "Unsafe");

        var reported = new EquipmentIssueReported(
            w.GymId, issueId, machine, "Prensa 45", EquipmentIssueReason.Unsafe, null,
            w.Members[0].UserId, IssueReporterRole.Member, [], DateTimeOffset.UtcNow);
        await PublishAsync(reported);
        await PublishAsync(reported);

        Assert.Single(await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported));
        Assert.Single(await DeliveriesOfAsync(w.GymId, NotificationType.EquipmentIssueReported));

        await ResolveAsync(staff, issueId, "Listo");
        var closed = new EquipmentIssueClosed(
            w.GymId, issueId, machine, "Prensa 45", false, w.Members[0].UserId, IssueReporterRole.Member,
            w.StaffId, 0, [issueId], DateTimeOffset.UtcNow);
        await PublishAsync(closed);
        await PublishAsync(closed);

        Assert.Single(await NoticesAsync(w.GymId, NotificationType.EquipmentIssueResolved));
        Assert.NotNull(Assert.Single(await NoticesAsync(w.GymId, NotificationType.EquipmentIssueReported)).ResolvedAt);
    }

    // --- tarjeta del dashboard ---

    [Fact]
    public async Task The_summary_counts_machines_out_of_service_and_open_reports()
    {
        var w = await SeedAsync(2);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var a = await LoggedInAsync(host, w.Members[0].Email);
        var b = await LoggedInAsync(host, w.Members[1].Email);
        var broken = await CreateMachineAsync(staff, "Prensa 45");
        var fixing = await CreateMachineAsync(staff, "Remo");
        await CreateMachineAsync(staff, "Banco");

        var empty = await staff.GetFromJsonAsync<JsonElement>("/api/equipment/summary");
        Assert.Equal(0, empty.GetProperty("openIssues").GetInt32());
        Assert.Equal(0, empty.GetProperty("outOfService").GetInt32());

        Assert.Equal(HttpStatusCode.OK,
            (await staff.PostAsJsonAsync($"/api/equipment/{broken}/status", new { status = "OutOfService", reason = "Motor quemado" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await staff.PostAsJsonAsync($"/api/equipment/{fixing}/status", new { status = "Maintenance", reason = "Lubricar" })).StatusCode);
        await ReportAsync(a, broken, "NotWorking");
        await ReportAsync(b, fixing, "Unsafe");

        var summary = await staff.GetFromJsonAsync<JsonElement>("/api/equipment/summary");
        Assert.Equal(1, summary.GetProperty("outOfService").GetInt32());
        Assert.Equal(1, summary.GetProperty("inMaintenance").GetInt32());
        Assert.Equal(2, summary.GetProperty("openIssues").GetInt32());
        Assert.Equal(1, summary.GetProperty("urgentIssues").GetInt32());
    }

    [Fact]
    public async Task Another_gym_staff_cannot_see_or_close_the_report()
    {
        var w = await SeedAsync(1);
        var other = await SeedAsync(0);
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var member = await LoggedInAsync(host, w.Members[0].Email);
        var intruder = await LoggedInAsync(host, other.StaffEmail);
        var machine = await CreateMachineAsync(staff, "Prensa 45");
        var issueId = await ReportAsync(member, machine, "Dirty");

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/equipment/issues/{issueId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.PutAsJsonAsync($"/api/equipment/issues/{issueId}", new { status = "Resolved", resolution = "x" })).StatusCode);
        Assert.Empty((await intruder.GetFromJsonAsync<JsonElement>("/api/equipment/issues")).EnumerateArray());
    }

    // --- helpers ---

    private static async Task<Guid> CreateMachineAsync(HttpClient client, string name)
    {
        var create = await client.PostAsJsonAsync("/api/equipment", new { name, type = "Machine", zone = "Pierna", quantity = 1 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        return (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> ReportAsync(HttpClient member, Guid machine, string reason, string? description = null)
    {
        var response = await member.PostAsJsonAsync($"/api/me/equipment/{machine}/issues", new { reason, description });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task ResolveAsync(HttpClient staff, Guid issueId, string resolution)
    {
        var response = await staff.PutAsJsonAsync($"/api/equipment/issues/{issueId}", new { status = "Resolved", resolution });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<List<JsonElement>> BellAsync(HttpClient client)
    {
        var json = await client.GetFromJsonAsync<JsonElement>("/api/staff/notifications?openOnly=true&pageSize=50");
        return json.GetProperty("items").EnumerateArray().ToList();
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private async Task<List<Notification>> NoticesAsync(Guid gymId, NotificationType type)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await db.Set<Notification>().AsNoTracking()
            .Where(n => n.GymId == gymId && n.Type == type)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync();
    }

    private async Task<List<NotificationDelivery>> DeliveriesOfAsync(Guid gymId, NotificationType type)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var ids = await db.Set<Notification>().AsNoTracking()
            .Where(n => n.GymId == gymId && n.Type == type).Select(n => n.Id).ToListAsync();
        return await db.Set<NotificationDelivery>().AsNoTracking().Where(d => ids.Contains(d.NotificationId)).ToListAsync();
    }

    private async Task<List<EquipmentIssue>> IssuesAsync(Guid gymId)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await db.Set<EquipmentIssue>().AsNoTracking().Where(i => i.GymId == gymId).OrderBy(i => i.CreatedAt).ToListAsync();
    }

    private async Task PublishAsync(IDomainEvent domainEvent)
    {
        using var scope = System();
        await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().PublishAsync(domainEvent);
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, GSM.API.Auth.CurrentUser>();
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

    private sealed record WorldMember(string Email, Guid MemberId, Guid UserId);

    private sealed record World(Guid GymId, string OwnerEmail, string StaffEmail, Guid StaffId, List<WorldMember> Members);

    private async Task<World> SeedAsync(int members, bool membersCanReport = true)
    {
        using var scope = System();
        scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Equipo Reportes", $"eq-is-{suffix}");
        var flags = GymFeatureFlags.Default(gym.Id);
        if (!membersCanReport)
        {
            flags.Update(true, true, true, true, true, true, false, membersCanReportEquipment: false);
        }

        db.Add(gym);
        db.Add(flags);
        await db.SaveChangesAsync();

        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var ownerEmail = $"eqi-owner-{suffix}@test.local";
        var staffEmail = $"eqi-staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Eq Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Eq Staff", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        var list = new List<WorldMember>();
        for (var i = 0; i < members; i++)
        {
            var email = $"eqi-m{i}-{suffix}@test.local";
            var userId = await provisioning.CreateMemberUserAsync(email, $"Miembro {i}", Password, gym.Id);
            var member = Member.Create(gym.Id, "Miembro", $"{i}", $"doc-{suffix}-{i}", new DateOnly(2000, 1, 1));
            member.LinkUser(userId);
            db.Add(member);
            await db.SaveChangesAsync();
            list.Add(new WorldMember(email, member.Id, userId));
        }

        return new World(gym.Id, ownerEmail, staffEmail, staffId, list);
    }
}
