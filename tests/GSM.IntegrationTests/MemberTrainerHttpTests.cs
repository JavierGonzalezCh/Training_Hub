using GSM.API.Auth;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Members.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// M1b: entrenador asignado al miembro, de punta a punta con identidad real (JWT): asignar, cambiar, quitar,
/// validar el permiso <c>Trainer</c> contra Identity, el filtro "mis miembros" y la limpieza al perder el rol.
/// </summary>
public sealed class MemberTrainerHttpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    [Fact]
    public async Task Owner_and_staff_with_Trainer_can_be_assigned_and_the_detail_shows_the_name()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await SetProfileAsync(owner, w.CoachId, "trainer");
        var memberId = await CreateMemberAsync(owner, "1001");

        var byCoach = await AssignAsync(owner, memberId, w.CoachId);
        Assert.Equal(w.CoachId, byCoach.TrainerUserId);
        Assert.Equal("Coach Trainer", byCoach.TrainerName);

        var byOwner = await AssignAsync(owner, memberId, w.OwnerId);
        Assert.Equal(w.OwnerId, byOwner.TrainerUserId);

        var detail = await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{memberId}");
        Assert.Equal(w.OwnerId, detail!.TrainerUserId);
        Assert.Equal("Trainer Owner", detail.TrainerName);
    }

    [Fact]
    public async Task Staff_without_the_Trainer_permission_cannot_be_assigned_it_is_a_422()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await SetProfileAsync(owner, w.ReceptionId, "reception");
        var memberId = await CreateMemberAsync(owner, "1002");

        var response = await owner.PutAsJsonAsync(
            $"/api/members/{memberId}/trainer", new AssignTrainerRequest(w.ReceptionId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var detail = await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{memberId}");
        Assert.Null(detail!.TrainerUserId);
    }

    [Fact]
    public async Task Someone_from_another_gym_cannot_be_assigned()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var memberId = await CreateMemberAsync(owner, "1003");

        var response = await owner.PutAsJsonAsync(
            $"/api/members/{memberId}/trainer", new AssignTrainerRequest(w.OtherOwnerId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Changing_and_removing_the_trainer_leaves_history_with_names()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await SetProfileAsync(owner, w.CoachId, "trainer");
        var memberId = await CreateMemberAsync(owner, "1004");

        await AssignAsync(owner, memberId, w.CoachId);
        await AssignAsync(owner, memberId, w.OwnerId);
        var removed = await AssignAsync(owner, memberId, null);
        Assert.Null(removed.TrainerUserId);
        Assert.Null(removed.TrainerName);

        var changes = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>(
            $"/api/members/{memberId}/changes?pageSize=50"))!.Items
            .Where(c => c.Field == "trainer")
            .OrderBy(c => c.ChangedAt)
            .ToList();
        Assert.Equal(3, changes.Count);
        Assert.Equal((null, "Coach Trainer"), (changes[0].OldValue, changes[0].NewValue));
        Assert.Equal(("Coach Trainer", "Trainer Owner"), (changes[1].OldValue, changes[1].NewValue));
        Assert.Equal(("Trainer Owner", null), (changes[2].OldValue, changes[2].NewValue));
    }

    [Fact]
    public async Task Assigning_the_same_trainer_again_adds_no_history()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var memberId = await CreateMemberAsync(owner, "1005");

        await AssignAsync(owner, memberId, w.OwnerId);
        await AssignAsync(owner, memberId, w.OwnerId);

        var changes = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>(
            $"/api/members/{memberId}/changes?pageSize=50"))!.Items;
        Assert.Single(changes, c => c.Field == "trainer");
    }

    [Fact]
    public async Task Editing_the_profile_does_not_clear_the_trainer()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var memberId = await CreateMemberAsync(owner, "1006");
        await AssignAsync(owner, memberId, w.OwnerId);

        var detail = (await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{memberId}"))!;
        var update = new UpdateMemberRequest(
            FirstName: "Editado", LastName: detail.LastName, DocumentType: detail.DocumentType,
            DocumentNumber: detail.DocumentNumber, BirthDate: detail.BirthDate, Gender: null, PhotoUrl: null,
            Phone: detail.Phone, Email: detail.Email, Address: null, City: null, EmergencyContactName: null,
            EmergencyContactRelationship: null, EmergencyContactPhone: null, Source: null, ReferredByMemberId: null,
            Goal: null, Tags: null, GuardianName: null, GuardianDocumentType: null, GuardianDocumentNumber: null,
            GuardianPhone: null, GuardianRelationship: null, JoinedAt: null);
        (await owner.PutAsJsonAsync($"/api/members/{memberId}", update)).EnsureSuccessStatusCode();

        var after = await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{memberId}");
        Assert.Equal(w.OwnerId, after!.TrainerUserId);
    }

    [Fact]
    public async Task The_trainer_list_has_the_owner_and_trainers_only_and_any_staff_can_read_it()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await SetProfileAsync(owner, w.CoachId, "trainer");
        await SetProfileAsync(owner, w.ReceptionId, "reception");

        var asReception = await LoggedInAsync(host, w.ReceptionEmail);
        var trainers = (await asReception.GetFromJsonAsync<List<TrainerOption>>("/api/members/trainers"))!;

        Assert.Equal(
            new[] { w.CoachId, w.OwnerId }.Order().ToArray(),
            trainers.Select(t => t.UserId).Order().ToArray());
        Assert.True(trainers.Single(t => t.UserId == w.OwnerId).IsOwner);
    }

    [Fact]
    public async Task A_member_cannot_assign_a_trainer_nor_read_the_trainer_list()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var memberId = await CreateMemberAsync(owner, "1007");
        var memberClient = await LoggedInAsync(host, w.MemberEmail);

        var put = await memberClient.PutAsJsonAsync(
            $"/api/members/{memberId}/trainer", new AssignTrainerRequest(w.OwnerId));
        var list = await memberClient.GetAsync("/api/members/trainers");

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task The_list_filters_by_trainer_and_by_without_trainer()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await SetProfileAsync(owner, w.CoachId, "trainer");
        var mine = await CreateMemberAsync(owner, "1008");
        var other = await CreateMemberAsync(owner, "1009");
        var none = await CreateMemberAsync(owner, "1010");
        await AssignAsync(owner, mine, w.CoachId);
        await AssignAsync(owner, other, w.OwnerId);

        var coachMembers = (await owner.GetFromJsonAsync<PagedResult<MemberListItem>>(
            $"/api/members?trainerUserId={w.CoachId}"))!.Items;
        Assert.Equal([mine], coachMembers.Select(m => m.Id).ToArray());
        Assert.Equal("Coach Trainer", coachMembers[0].TrainerName);

        var unassigned = (await owner.GetFromJsonAsync<PagedResult<MemberListItem>>(
            "/api/members?withoutTrainer=true"))!.Items;
        Assert.Equal([none], unassigned.Select(m => m.Id).ToArray());
    }

    [Fact]
    public async Task Losing_the_Trainer_permission_leaves_the_members_without_trainer()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await SetProfileAsync(owner, w.CoachId, "trainer");
        var memberId = await CreateMemberAsync(owner, "1011");
        await AssignAsync(owner, memberId, w.CoachId);

        await SetProfileAsync(owner, w.CoachId, "reception");

        var detail = await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{memberId}");
        Assert.Null(detail!.TrainerUserId);
        var changes = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>(
            $"/api/members/{memberId}/changes?pageSize=50"))!.Items;
        Assert.Contains(changes, c => c.Field == "trainer" && c.OldValue == "Coach Trainer" && c.NewValue is null);
    }

    [Fact]
    public async Task Deactivating_the_trainer_account_leaves_the_members_without_trainer_and_listed_for_reassigning()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await SetProfileAsync(owner, w.CoachId, "trainer");
        var memberId = await CreateMemberAsync(owner, "1012");
        await AssignAsync(owner, memberId, w.CoachId);

        (await owner.DeleteAsync($"/api/gym/staff/{w.CoachId}")).EnsureSuccessStatusCode();

        var detail = await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{memberId}");
        Assert.Null(detail!.TrainerUserId);
        var unassigned = (await owner.GetFromJsonAsync<PagedResult<MemberListItem>>(
            "/api/members?withoutTrainer=true"))!.Items;
        Assert.Contains(unassigned, m => m.Id == memberId);

        // Y una cuenta desactivada ya no puede ser entrenador.
        var again = await owner.PutAsJsonAsync($"/api/members/{memberId}/trainer", new AssignTrainerRequest(w.CoachId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
    }

    [Fact]
    public async Task A_member_sees_who_their_trainer_is_on_their_own_record()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await AssignAsync(owner, w.MemberRecordId, w.OwnerId);
        var memberClient = await LoggedInAsync(host, w.MemberEmail);

        var detail = await memberClient.GetFromJsonAsync<MemberDetail>($"/api/members/{w.MemberRecordId}");

        Assert.Equal("Trainer Owner", detail!.TrainerName);
    }

    // --- Ayudas ---

    private static async Task<MemberDetail> AssignAsync(HttpClient client, Guid memberId, Guid? trainerId)
    {
        var response = await client.PutAsJsonAsync($"/api/members/{memberId}/trainer", new AssignTrainerRequest(trainerId));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MemberDetail>())!;
    }

    private static async Task SetProfileAsync(HttpClient owner, Guid staffId, string profile)
    {
        var permissions = profile == "trainer" ? new[] { "trainer", "view_member_health" } : [];
        var response = await owner.PutAsJsonAsync(
            $"/api/gym/staff/{staffId}/permissions", new UpdateStaffPermissionsRequest(profile, permissions));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreateMemberAsync(HttpClient client, string document)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var response = await client.PostAsJsonAsync("/api/members", new CreateMemberRequest(
            FirstName: "Ana", LastName: $"Prueba{suffix}", DocumentType: "cc", DocumentNumber: $"{document}{suffix}",
            BirthDate: new DateOnly(1990, 1, 1), Gender: null, PhotoUrl: null, Phone: null, Email: null,
            Address: null, City: null, EmergencyContactName: null, EmergencyContactRelationship: null,
            EmergencyContactPhone: null, Source: null, ReferredByMemberId: null, Goal: null, Tags: null,
            GuardianName: null, GuardianDocumentType: null, GuardianDocumentNumber: null, GuardianPhone: null,
            GuardianRelationship: null, JoinedAt: null));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateMemberResponse>())!.Id;
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email)
    {
        var login = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Tokens!;
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<GSM.Application.Identity.Public.IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Trainer Gym", $"trn-{suffix}");
        var other = Gym.Create("Trainer Gym B", $"trnb-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), other, GymFeatureFlags.Default(other.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var coachEmail = $"coach-{suffix}@test.local";
        var receptionEmail = $"rec-{suffix}@test.local";
        var memberEmail = $"member-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Trainer Owner", Password, UserRole.Owner, gym.Id);
        var coachId = await provisioning.CreateGymUserAsync(coachEmail, "Coach Trainer", Password, UserRole.Staff, gym.Id);
        var receptionId = await provisioning.CreateGymUserAsync(receptionEmail, "Rita Reception", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);

        var otherOwnerId = await provisioning.CreateGymUserAsync(
            $"ownerb-{suffix}@test.local", "Other Owner", Password, UserRole.Owner, other.Id);
        other.AssignOwner(otherOwnerId);

        var memberUserId = await provisioning.CreateGymUserAsync(memberEmail, "Mia Member", Password, UserRole.Member, gym.Id);
        var member = GSM.Domain.Members.Member.Create(
            gym.Id, "Mia", "Member", $"9{suffix}", new DateOnly(2026, 1, 1), email: memberEmail);
        member.LinkUser(memberUserId);
        db.Add(member);

        await db.SaveChangesAsync();
        return new World(
            gym.Id, ownerId, ownerEmail, coachId, receptionId, receptionEmail, otherOwnerId, memberEmail, member.Id);
    }

    private sealed record World(
        Guid GymId, Guid OwnerId, string OwnerEmail, Guid CoachId, Guid ReceptionId, string ReceptionEmail,
        Guid OtherOwnerId, string MemberEmail, Guid MemberRecordId);
}
