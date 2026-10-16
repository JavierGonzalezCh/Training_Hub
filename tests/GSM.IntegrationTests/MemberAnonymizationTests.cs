using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Notifications;
using GSM.Domain.Routines;
using GSM.Infrastructure.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// M6, anonimización (Members §11). La tarea periódica avisa a los Owners y anonimiza al vencer el plazo del gym;
/// Members publica <c>MemberAnonymized</c> y Identity, Routines, Equipment y Notifications limpian lo suyo. El tiempo va
/// por <c>MutableClock</c>.
/// </summary>
public sealed class MemberAnonymizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Joined = new(2026, 1, 5);
    private static readonly DateOnly Baja = new(2026, 3, 1);

    // ---------- Dominio ----------

    [Fact]
    public void Anonymize_empties_personal_data_and_keeps_activity_data()
    {
        var member = Member.Create(
            Guid.CreateVersion7(), "Ana", "Pérez", "1234567890", Joined,
            phone: "+573001234567", email: "ana@test.local", birthDate: new DateOnly(1990, 7, 15), photoUrl: "https://x/f.jpg");
        member.UpdateHealth(new MemberHealthData("asma", "inhalador", "sin sentadilla", null), Joined);
        member.Deactivate(Baja);

        var photo = member.Anonymize(DateTimeOffset.UtcNow);

        Assert.Equal("https://x/f.jpg", photo);
        Assert.True(member.IsAnonymized);
        Assert.Equal("Miembro anonimizado", member.FirstName);
        Assert.Equal(4, member.LastName.Length);
        Assert.StartsWith("ANON-", member.DocumentNumber);
        Assert.True(member.DocumentNumber.Length <= 30);
        Assert.Equal(new DateOnly(1990, 1, 1), member.BirthDate);
        Assert.Null(member.Phone);
        Assert.Null(member.Email);
        Assert.Null(member.PhotoUrl);
        Assert.Null(member.MedicalConditions);
        Assert.Null(member.Medications);
        Assert.Null(member.TrainingRestrictions);
        Assert.False(member.HasHealthAlerts);
        Assert.Equal(Joined, member.JoinedAt);
        Assert.Equal(MemberStatus.Inactive, member.Status);

        // Idempotente: la segunda vez no hace nada ni devuelve foto.
        Assert.Null(member.Anonymize(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void An_anonymized_member_cannot_be_reactivated_or_edited()
    {
        var member = Member.Create(Guid.CreateVersion7(), "Ana", "Pérez", "1234567890", Joined);
        member.Deactivate(Baja);
        member.Anonymize(DateTimeOffset.UtcNow);

        Assert.Throws<ConflictException>(() => member.Reactivate());
        Assert.Throws<ConflictException>(() => member.SetPhoto("https://x/f.jpg"));
        Assert.Throws<ConflictException>(() => member.AssignTrainer(Guid.CreateVersion7()));
    }

    [Fact]
    public void An_active_member_cannot_be_anonymized_directly()
    {
        var member = Member.Create(Guid.CreateVersion7(), "Ana", "Pérez", "1234567890", Joined);

        Assert.Throws<ConflictException>(() => member.Anonymize(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Reactivating_stops_the_clock_and_deactivating_again_restarts_it()
    {
        var member = Member.Create(Guid.CreateVersion7(), "Ana", "Pérez", "1234567890", Joined);
        member.Deactivate(Baja);
        Assert.Equal(Baja.AddMonths(24), member.AnonymizesOn(24));

        member.Reactivate();
        Assert.Null(member.DeactivatedOn);
        Assert.Null(member.AnonymizesOn(24));

        member.Deactivate(new DateOnly(2026, 9, 1));
        Assert.Equal(new DateOnly(2028, 9, 1), member.AnonymizesOn(24));
    }

    // ---------- La tarea ----------

    [Fact]
    public async Task The_job_anonymizes_a_member_whose_period_expired_and_every_module_cleans_its_part()
    {
        var w = await SeedAsync();
        var seeded = await SeedFullMemberAsync(w);

        await RunJobOnAsync(Baja.AddMonths(24));

        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var member = await db.Set<Member>().IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == seeded.MemberId);
        Assert.True(member.IsAnonymized);
        Assert.Equal("Miembro anonimizado", member.FirstName);
        Assert.Null(member.Email);
        Assert.Null(member.UserId);
        Assert.Null(member.MedicalConditions);

        // Members: notas y registro de consultas de salud, borrados; mediciones y autorizaciones, conservadas.
        Assert.Empty(await db.Set<MemberNote>().IgnoreQueryFilters().Where(n => n.MemberId == seeded.MemberId).ToListAsync());
        Assert.Empty(await db.Set<MemberHealthAccess>().IgnoreQueryFilters().Where(a => a.MemberId == seeded.MemberId).ToListAsync());
        Assert.Single(await db.Set<MemberMeasurement>().IgnoreQueryFilters().Where(m => m.MemberId == seeded.MemberId).ToListAsync());

        // Historial: se conserva el campo y la fecha; los valores se vacían.
        var changes = await db.Set<MemberChange>().IgnoreQueryFilters().Where(c => c.MemberId == seeded.MemberId).ToListAsync();
        Assert.Contains(changes, c => c.Field == "phone");
        Assert.All(changes, c =>
        {
            Assert.Null(c.OldValue);
            Assert.Null(c.NewValue);
        });
        Assert.Contains(changes, c => c.Field == "anonymized");

        // Routines (R10): notas vaciadas, el registro se conserva.
        var routine = await db.Set<Routine>().IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.MemberId == seeded.MemberId);
        Assert.Null(routine.Notes);
        var log = await db.Set<RoutineExerciseLog>().IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.MemberId == seeded.MemberId);
        Assert.Null(log.Notes);

        // Equipment: el reporte se conserva sin autor.
        var issue = await db.Set<EquipmentIssue>().IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == seeded.IssueId);
        Assert.Null(issue.ReportedByUserId);
        Assert.Equal("Se traba la polea", issue.Description);

        // Identity: la cuenta de la app se eliminó (era su único acceso).
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(seeded.UserId.ToString()));
        Assert.Empty(await db.GymAccesses.Where(a => a.UserId == seeded.UserId).ToListAsync());

        // Notifications: el aviso del miembro queda sin texto.
        var notice = await db.Set<Notification>().IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.Id == seeded.NotificationId);
        Assert.Equal(string.Empty, notice.Body);
    }

    [Fact]
    public async Task The_job_does_not_anonymize_before_the_period_ends_and_the_run_is_idempotent()
    {
        var w = await SeedAsync();
        var memberId = await AddInactiveMemberAsync(w, Baja);

        await RunJobOnAsync(Baja.AddMonths(24).AddDays(-8));
        Assert.False(await IsAnonymizedAsync(memberId));

        await RunJobOnAsync(Baja.AddMonths(24));
        Assert.True(await IsAnonymizedAsync(memberId));

        // Una segunda ejecución no falla ni cambia nada.
        await RunJobOnAsync(Baja.AddMonths(24).AddDays(1));
        Assert.True(await IsAnonymizedAsync(memberId));
    }

    [Fact]
    public async Task A_run_that_was_missed_catches_up_on_the_next_one()
    {
        var w = await SeedAsync();
        var memberId = await AddInactiveMemberAsync(w, Baja);

        await RunJobOnAsync(Baja.AddMonths(24).AddDays(40));

        Assert.True(await IsAnonymizedAsync(memberId));
    }

    [Fact]
    public async Task The_gym_period_decides_when_a_member_is_anonymized()
    {
        var w = await SeedAsync(months: 6);
        var memberId = await AddInactiveMemberAsync(w, Baja);

        await RunJobOnAsync(Baja.AddMonths(6).AddDays(-1));
        Assert.False(await IsAnonymizedAsync(memberId));

        await RunJobOnAsync(Baja.AddMonths(6));
        Assert.True(await IsAnonymizedAsync(memberId));
    }

    [Fact]
    public async Task An_active_or_reactivated_member_is_never_anonymized()
    {
        var w = await SeedAsync(months: 6);
        var active = await AddMemberAsync(w, "Activa", inactiveSince: null);
        var reactivated = await AddInactiveMemberAsync(w, Baja);
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            (await db.Set<Member>().IgnoreQueryFilters().SingleAsync(m => m.Id == reactivated)).Reactivate();
            await db.SaveChangesAsync();
        }

        await RunJobOnAsync(Baja.AddMonths(60));

        Assert.False(await IsAnonymizedAsync(active));
        Assert.False(await IsAnonymizedAsync(reactivated));
    }

    // ---------- Aviso previo ----------

    [Fact]
    public async Task Owners_are_warned_a_week_before_once_and_without_the_member_name()
    {
        var w = await SeedAsync();
        var owner1 = await AddOwnerAsync(w);
        var owner2 = await AddOwnerAsync(w);
        var memberId = await AddInactiveMemberAsync(w, Baja);
        var anonymizesOn = Baja.AddMonths(24);

        await RunJobOnAsync(anonymizesOn.AddDays(-8));
        Assert.Empty(await NoticesAsync(w.GymId));

        await RunJobOnAsync(anonymizesOn.AddDays(-7));
        await RunJobOnAsync(anonymizesOn.AddDays(-6));

        var notices = await NoticesAsync(w.GymId);
        Assert.Equal(2, notices.Count);
        Assert.Contains(notices, n => n.RecipientUserId == owner1);
        Assert.Contains(notices, n => n.RecipientUserId == owner2);
        Assert.All(notices, n =>
        {
            Assert.Equal($"/members/{memberId}", n.Link);
            Assert.DoesNotContain("Prueba", n.Body);
        });
        Assert.False(await IsAnonymizedAsync(memberId));
    }

    // ---------- Supresión a pedido ----------

    [Fact]
    public async Task The_owner_can_anonymize_an_active_member_at_once_with_confirmation()
    {
        var w = await SeedAsync();
        var memberId = await AddMemberAsync(w, "Pedro", inactiveSince: null);

        using (var scope = AsOwner(w))
        {
            var handler = scope.ServiceProvider.GetRequiredService<AnonymizeMemberHandler>();
            await Assert.ThrowsAsync<ValidationDomainException>(
                () => handler.HandleAsync(memberId, new AnonymizeMemberRequest(false), default));
        }

        Assert.False(await IsAnonymizedAsync(memberId));

        using (var scope = AsOwner(w))
        {
            var detail = await scope.ServiceProvider.GetRequiredService<AnonymizeMemberHandler>()
                .HandleAsync(memberId, new AnonymizeMemberRequest(true), default);
            Assert.True(detail.IsAnonymized);
            Assert.Equal("Inactive", detail.Status);
            Assert.Equal("Miembro anonimizado", detail.FirstName);
        }

        Assert.True(await IsAnonymizedAsync(memberId));
    }

    [Fact]
    public async Task The_gym_period_is_validated_and_defaults_to_24_months()
    {
        var w = await SeedAsync();

        using var scope = AsOwner(w);
        var get = scope.ServiceProvider.GetRequiredService<GetMemberSettingsHandler>();
        var update = scope.ServiceProvider.GetRequiredService<UpdateMemberSettingsHandler>();

        Assert.Equal(24, (await get.HandleAsync(default)).AnonymizationMonths);
        await Assert.ThrowsAsync<ValidationDomainException>(
            () => update.HandleAsync(new UpdateMemberSettingsRequest(5), default));
        Assert.Equal(12, (await update.HandleAsync(new UpdateMemberSettingsRequest(12), default)).AnonymizationMonths);
        Assert.Equal(12, (await get.HandleAsync(default)).AnonymizationMonths);
    }

    // ---------- helpers ----------

    private async Task RunJobOnAsync(DateOnly day)
    {
        using var scope = System();
        var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(15, 0)), TimeSpan.FromHours(-5));
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(at, day);
        await scope.ServiceProvider.GetRequiredService<RunAnonymizationJobHandler>().HandleAsync(default);
    }

    private async Task<bool> IsAnonymizedAsync(Guid memberId)
    {
        using var scope = System();
        return (await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Member>()
            .IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == memberId)).IsAnonymized;
    }

    private async Task<List<Notification>> NoticesAsync(Guid gymId)
    {
        using var scope = System();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Notification>()
            .IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.GymId == gymId && n.Type == NotificationType.MemberAnonymizationUpcoming)
            .ToListAsync();
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope AsOwner(World w)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), w.GymId, UserRole.Owner);
        return scope;
    }

    /// <param name="months">Nulo = sin fila de ajustes (valen los 24 por defecto).</param>
    private async Task<World> SeedAsync(int? months = null)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = Gym.Create("M6 Gym", $"m6-{Guid.NewGuid():N}"[..14]);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        if (months is { } value)
        {
            var settings = MemberSettings.Default(gym.Id);
            settings.Update(value);
            db.Add(settings);
        }

        await db.SaveChangesAsync();
        return new World(gym.Id);
    }

    private async Task<Guid> AddOwnerAsync(World w)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"m6-owner-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Dueño M6", IsActive = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        db.GymAccesses.Add(GymAccess.Grant(user.Id, w.GymId, UserRole.Owner));
        await db.SaveChangesAsync();
        return user.Id;
    }

    private Task<Guid> AddInactiveMemberAsync(World w, DateOnly deactivatedOn)
        => AddMemberAsync(w, "Prueba", deactivatedOn);

    private async Task<Guid> AddMemberAsync(World w, string firstName, DateOnly? inactiveSince, Guid? userId = null)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var member = Member.Create(
            w.GymId, firstName, "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Joined,
            phone: "+573001234567", email: $"m6-{Guid.NewGuid():N}@test.local", birthDate: new DateOnly(1990, 7, 15));
        if (userId is { } id)
        {
            member.LinkUser(id);
        }

        if (inactiveSince is { } day)
        {
            member.Deactivate(day);
        }

        db.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    /// <summary>Una ficha dada de baja con datos en cada módulo que debe limpiarla.</summary>
    private async Task<Seeded> SeedFullMemberAsync(World w)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var email = $"m6-member-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Miembro M6", IsActive = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        db.GymAccesses.Add(GymAccess.Grant(user.Id, w.GymId, UserRole.Member));

        var member = Member.Create(
            w.GymId, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Joined,
            phone: "+573001234567", email: email, birthDate: new DateOnly(1990, 7, 15));
        member.LinkUser(user.Id);
        member.UpdateHealth(new MemberHealthData("asma", "inhalador", "sin sentadilla", null), Joined);
        member.Deactivate(Baja);
        db.Add(member);

        db.Add(MemberNote.Add(w.GymId, member.Id, "Se queja de la rodilla", Guid.CreateVersion7()));
        db.Add(MemberHealthAccess.Record(
            w.GymId, member.Id, Guid.CreateVersion7(), "owner", DateTimeOffset.UtcNow, Joined));
        db.Add(MemberMeasurement.Add(w.GymId, member.Id, Joined, Joined, 70m, 170m, null, "staff"));
        db.Add(MemberChange.Record(
            w.GymId, member.Id, "phone", "+573000000000", "+573001234567", null,
            MemberChangeRole.Staff, MemberChangeSource.Panel, DateTimeOffset.UtcNow));

        // Routines: rutina con nota, y un registro con nota.
        var exerciseId = Guid.CreateVersion7();
        var routine = Routine.Assign(
            w.GymId, member.Id, Joined,
            new RoutineDraft(
                "Fuerza", "rodilla operada", Guid.CreateVersion7(), false,
                [new RoutineDayDraft(
                    "Día 1", false,
                    [new RoutineExerciseDraft(
                        exerciseId, [new SetTarget(1, SetType.Normal, 8, 10, 20m, null, null, null)], 60, "con cuidado")])]));
        db.Add(routine);
        await db.SaveChangesAsync();
        var line = routine.Days.Single().Exercises.Single();
        db.Add(RoutineExerciseLog.Record(
            w.GymId, member.Id, line.Id, exerciseId, Joined, DateTimeOffset.UtcNow, "me dolió la rodilla"));

        // Equipment: un reporte hecho por la cuenta del miembro.
        var item = EquipmentItem.Create(
            w.GymId, null, "Polea", EquipmentType.Cardio_machine, null, null, null, null, 1, null, null);
        db.Add(item);
        await db.SaveChangesAsync();
        var issue = EquipmentIssue.Report(
            w.GymId, item.Id, user.Id, IssueReporterRole.Member, EquipmentIssueReason.Other, "Se traba la polea");
        db.Add(issue);

        // Notifications: un aviso a la bandeja del miembro.
        var notification = Notification.CreateForMember(
            w.GymId, member.Id, NotificationType.Birthday, NotificationCategory.News, Guid.CreateVersion7(),
            "¡Feliz cumpleaños!", "Feliz cumpleaños, Ana", "/app", DateTimeOffset.UtcNow);
        db.Add(notification);

        await db.SaveChangesAsync();
        return new Seeded(member.Id, user.Id, issue.Id, notification.Id);
    }

    private sealed record World(Guid GymId);

    private sealed record Seeded(Guid MemberId, Guid UserId, Guid IssueId, Guid NotificationId);
}
