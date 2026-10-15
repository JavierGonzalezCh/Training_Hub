using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Equipment;
using GSM.Application.Equipment.Contracts;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Routines;
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
/// R5: alternativas de ejercicios (recomendadas + sugeridas), "Cambiar por…" que conserva el historial y "Hacer otro
/// ejercicio hoy". Los ejercicios sembrados del sistema solo se leen; cada test crea los suyos con el músculo
/// <c>Neck</c> (que el catálogo sembrado no usa) y los borra en un <c>finally</c>.
/// </summary>
public sealed class ExerciseAlternativesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private static readonly DateOnly Day = new(2026, 6, 10);

    // --- Siembra del sistema ---

    [Fact]
    public async Task Migration_seeds_system_recommendations_that_work_in_both_directions()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var squat = await db.Set<Exercise>().SingleAsync(e => e.GymId == null && e.Name == "Sentadilla con barra");
        var press = await db.Set<Exercise>().SingleAsync(e => e.GymId == null && e.Name == "Prensa de piernas");

        var fromSquat = await scope.ServiceProvider.GetRequiredService<GetExerciseAlternativesHandler>().HandleAsync(squat.Id, default);
        var fromPress = await scope.ServiceProvider.GetRequiredService<GetExerciseAlternativesHandler>().HandleAsync(press.Id, default);

        Assert.Contains(fromSquat, a => a.Id == press.Id && a.Source == "recommended");
        Assert.Contains(fromPress, a => a.Id == squat.Id && a.Source == "recommended");
        // Las recomendadas van antes que cualquier sugerida.
        var firstSuggested = fromSquat.ToList().FindIndex(a => a.Source == "suggested");
        Assert.True(firstSuggested < 0 || fromSquat.Take(firstSuggested).All(a => a.Source == "recommended"));
    }

    // --- Recomendadas primero, luego sugeridas ---

    [Fact]
    public async Task Recommended_come_first_then_suggested_ranked_and_without_inactive_or_itself()
    {
        var w = await SeedAsync();
        var ids = new List<Guid>();
        try
        {
            var source = await CreateAsync(w, "Fuente", Muscle.Neck, [Muscle.Traps]);
            var withSecondary = await CreateAsync(w, "Con secundario", Muscle.Neck, [Muscle.Traps]);
            var plain = await CreateAsync(w, "Sin secundario", Muscle.Neck, []);
            var inactive = await CreateAsync(w, "Inactivo", Muscle.Neck, [Muscle.Traps]);
            var recommended = await CreateAsync(w, "Recomendado", Muscle.Chest, []);
            ids.AddRange([source, withSecondary, plain, inactive, recommended]);

            using (var scope = Staff(w))
            {
                await scope.ServiceProvider.GetRequiredService<DeactivateExerciseHandler>().HandleAsync(inactive, default);
                await scope.ServiceProvider.GetRequiredService<AddExerciseAlternativeHandler>().HandleAsync(source, recommended, default);
            }

            var list = await AlternativesAsync(w, source);

            Assert.Equal([recommended, withSecondary, plain], list.Take(3).Select(a => a.Id));
            Assert.Equal(["recommended", "suggested", "suggested"], list.Take(3).Select(a => a.Source));
            Assert.DoesNotContain(list, a => a.Id == source || a.Id == inactive);
        }
        finally
        {
            await CleanupAsync(w, ids);
        }
    }

    [Fact]
    public async Task Same_tracking_ranks_before_a_different_one_and_the_list_is_capped_at_six()
    {
        var w = await SeedAsync();
        var ids = new List<Guid>();
        try
        {
            var source = await CreateAsync(w, "Fuente", Muscle.Neck, []);
            ids.Add(source);
            var timed = await CreateAsync(w, "A tiempo", Muscle.Neck, [], tracking: TrackingType.Time);
            ids.Add(timed);
            for (var i = 0; i < 7; i++)
            {
                ids.Add(await CreateAsync(w, $"Reps {i}", Muscle.Neck, []));
            }

            var list = await AlternativesAsync(w, source);

            Assert.Equal(ExerciseAlternativesResolver.MaxResults, list.Count);
            Assert.DoesNotContain(list, a => a.Id == timed); // el de otra medición queda al final y fuera del tope
            Assert.All(list, a => Assert.Equal("RepsWeight", a.TrackingType));
        }
        finally
        {
            await CleanupAsync(w, ids);
        }
    }

    [Fact]
    public async Task With_fewer_than_three_suggestions_it_completes_with_the_same_muscle_group_and_type()
    {
        var w = await SeedAsync();
        var ids = new List<Guid>();
        try
        {
            var source = await CreateAsync(w, "Fuente", Muscle.Neck, []);
            var sameMuscle = await CreateAsync(w, "Mismo músculo", Muscle.Neck, []);
            ids.AddRange([source, sameMuscle]);

            var list = await AlternativesAsync(w, source);

            Assert.Equal(sameMuscle, list[0].Id);
            Assert.Equal(3, list.Count);
            Assert.All(list, a => Assert.Equal("UpperBody", a.MuscleGroup));
            Assert.All(list, a => Assert.Equal("Strength", a.ExerciseType));
        }
        finally
        {
            await CleanupAsync(w, ids);
        }
    }

    [Fact]
    public async Task Suggestions_skip_what_the_gym_cannot_do_today_but_recommended_ones_stay_flagged()
    {
        var w = await SeedAsync();
        var ids = new List<Guid>();
        try
        {
            var source = await CreateAsync(w, "Fuente", Muscle.Neck, []);
            var withBarbell = await CreateAsync(w, "Con barra", Muscle.Neck, [], [EquipmentType.Barbell]);
            var withMachine = await CreateAsync(w, "Con máquina", Muscle.Neck, [], [EquipmentType.Machine]);
            var bodyweight = await CreateAsync(w, "Sin equipo", Muscle.Neck, []);
            var extra1 = await CreateAsync(w, "Extra 1", Muscle.Neck, [], [EquipmentType.Machine]);
            ids.AddRange([source, withBarbell, withMachine, bodyweight, extra1]);

            Guid barbellItem;
            using (var scope = Staff(w))
            {
                barbellItem = (await scope.ServiceProvider.GetRequiredService<CreateEquipmentHandler>().HandleAsync(
                    new SaveEquipmentRequest("Barra R5 " + w.Suffix, EquipmentType.Barbell, null, null, null, null, 1, null, null),
                    default)).Id;
            }

            // El gym solo tiene barra: lo que pide máquina no se puede hacer aquí.
            var list = await AlternativesAsync(w, source);
            Assert.Contains(list, a => a.Id == withBarbell);
            Assert.Contains(list, a => a.Id == bodyweight);
            Assert.DoesNotContain(list, a => a.Id == withMachine || a.Id == extra1);

            // La barra entra en mantenimiento: ya no hay con qué hacer el ejercicio de barra.
            using (var scope = Staff(w))
            {
                await scope.ServiceProvider.GetRequiredService<ChangeEquipmentStatusHandler>().HandleAsync(
                    barbellItem, new ChangeEquipmentStatusRequest(EquipmentStatus.Maintenance, "Cable roto", null), default);
                await scope.ServiceProvider.GetRequiredService<AddExerciseAlternativeHandler>().HandleAsync(source, withBarbell, default);
            }

            var afterBreak = await AlternativesAsync(w, source);
            Assert.DoesNotContain(afterBreak, a => a.Source == "suggested" && a.Id == withBarbell);
            var stillRecommended = Assert.Single(afterBreak, a => a.Id == withBarbell);
            Assert.Equal("recommended", stillRecommended.Source);
            Assert.False(stillRecommended.IsAvailableHere);
        }
        finally
        {
            await CleanupAsync(w, ids);
        }
    }

    // --- Recomendadas del gym ---

    [Fact]
    public async Task A_recommended_pair_is_stored_once_works_both_ways_and_stays_inside_its_gym()
    {
        var w = await SeedAsync();
        var ids = new List<Guid>();
        try
        {
            var a = await CreateAsync(w, "Uno", Muscle.Neck, []);
            var b = await CreateAsync(w, "Dos", Muscle.Chest, []);
            ids.AddRange([a, b]);

            using (var scope = Staff(w))
            {
                var add = scope.ServiceProvider.GetRequiredService<AddExerciseAlternativeHandler>();
                await add.HandleAsync(a, b, default);
                await add.HandleAsync(b, a, default); // idempotente, y el sentido da igual
            }

            Assert.Equal(1, await CountPairsAsync(a, b));
            Assert.Contains(await AlternativesAsync(w, a), x => x.Id == b && x.Source == "recommended");
            Assert.Contains(await AlternativesAsync(w, b), x => x.Id == a && x.Source == "recommended");

            // Otro gym no ve el ejercicio ni la relación: para él es un 404.
            using (var other = Staff(w.GymB, w.StaffB))
            {
                await Assert.ThrowsAsync<NotFoundException>(() =>
                    other.ServiceProvider.GetRequiredService<GetExerciseAlternativesHandler>().HandleAsync(a, default));
            }

            using (var scope = Staff(w))
            {
                await scope.ServiceProvider.GetRequiredService<RemoveExerciseAlternativeHandler>().HandleAsync(b, a, default);
            }

            Assert.Equal(0, await CountPairsAsync(a, b));
        }
        finally
        {
            await CleanupAsync(w, ids);
        }
    }

    [Fact]
    public async Task An_exercise_cannot_be_its_own_alternative_and_a_gym_cannot_remove_a_system_pair()
    {
        var w = await SeedAsync();
        var ids = new List<Guid>();
        try
        {
            var a = await CreateAsync(w, "Uno", Muscle.Neck, []);
            ids.Add(a);
            using (var scope = Staff(w))
            {
                await Assert.ThrowsAsync<ValidationDomainException>(() =>
                    scope.ServiceProvider.GetRequiredService<AddExerciseAlternativeHandler>().HandleAsync(a, a, default));
            }

            Guid squat;
            Guid press;
            using (var scope = System())
            {
                var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
                squat = (await db.Set<Exercise>().SingleAsync(e => e.GymId == null && e.Name == "Sentadilla con barra")).Id;
                press = (await db.Set<Exercise>().SingleAsync(e => e.GymId == null && e.Name == "Prensa de piernas")).Id;
            }

            using (var scope = Staff(w))
            {
                await Assert.ThrowsAsync<ValidationDomainException>(() =>
                    scope.ServiceProvider.GetRequiredService<RemoveExerciseAlternativeHandler>().HandleAsync(squat, press, default));
            }
        }
        finally
        {
            await CleanupAsync(w, ids);
        }
    }

    [Fact]
    public async Task A_superadmin_saves_system_pairs_between_system_exercises_only()
    {
        var w = await SeedAsync();
        Guid s1;
        Guid s2;
        Guid gymOwn = Guid.Empty;
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var one = Exercise.Create(null, $"R5 sistema uno {w.Suffix}", null, Muscle.Neck, null, null, null, null);
            var two = Exercise.Create(null, $"R5 sistema dos {w.Suffix}", null, Muscle.Chest, null, null, null, null);
            db.AddRange(one, two);
            await db.SaveChangesAsync();
            (s1, s2) = (one.Id, two.Id);
        }

        try
        {
            gymOwn = await CreateAsync(w, "Propio", Muscle.Neck, []);

            using (var scope = SuperAdmin())
            {
                await scope.ServiceProvider.GetRequiredService<AddExerciseAlternativeHandler>().HandleAsync(s1, s2, default);
                // Un ejercicio de un gym no lo ve el superadmin.
                await Assert.ThrowsAsync<NotFoundException>(() =>
                    scope.ServiceProvider.GetRequiredService<AddExerciseAlternativeHandler>().HandleAsync(s1, gymOwn, default));
            }

            // Un gym ve la recomendación del sistema, sin haberla definido.
            Assert.Contains(await AlternativesAsync(w, s1), a => a.Id == s2 && a.Source == "recommended");
        }
        finally
        {
            await CleanupAsync(w, gymOwn == Guid.Empty ? [] : [gymOwn]);
            using var scope = System();
            await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<Exercise>()
                .Where(e => e.GymId == null && e.Name.StartsWith("R5 sistema")).ExecuteDeleteAsync();
        }
    }

    // --- "Cambiar por…" en el editor ---

    [Fact]
    public async Task Changing_a_line_for_an_alternative_retires_the_old_line_and_keeps_its_history()
    {
        var w = await SeedAsync();
        var ids = new List<Guid>();
        try
        {
            var planned = await CreateAsync(w, "Planeado", Muscle.Neck, []);
            var alternative = await CreateAsync(w, "Alternativa", Muscle.Neck, []);
            ids.AddRange([planned, alternative]);
            var (routineId, lineId, dayId) = await AssignAsync(w, planned);
            await LogAsync(w, lineId, new LogRoutineExerciseRequest(null, [new LogSetRequest("normal", 10, 20m, null, null, null)]));

            // El editor manda la línea nueva (sin Id) en lugar de la anterior.
            RoutineResponse updated;
            using (var scope = Staff(w))
            {
                updated = await scope.ServiceProvider.GetRequiredService<UpdateRoutineHandler>().HandleAsync(
                    routineId,
                    new SaveRoutineRequest("Rutina R5", null,
                    [
                        new RoutineDayInput("D1", false,
                            [new RoutineExerciseInput(alternative, TestSets.Normal(3, 10), 60, null)], dayId),
                    ]),
                    default);
            }

            var line = Assert.Single(updated.Days.Single().Exercises);
            Assert.Equal(alternative, line.ExerciseId);
            Assert.NotEqual(lineId, line.Id);

            using var check = Staff(w);
            var db = check.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.NotNull((await db.Set<RoutineExercise>().SingleAsync(e => e.Id == lineId)).RetiredOn);
            var log = await db.Set<RoutineExerciseLog>().SingleAsync(l => l.RoutineExerciseId == lineId);
            Assert.Equal(planned, log.ExerciseId);

            var history = await HistoryAsync(w);
            var item = Assert.Single(history);
            Assert.Equal(planned, item.ExerciseId);
            Assert.Null(item.PlannedExerciseId);
        }
        finally
        {
            await CleanupAsync(w, ids);
        }
    }

    // --- "Hacer otro ejercicio hoy" ---

    [Fact]
    public async Task Doing_another_exercise_today_logs_the_replacement_without_changing_the_routine()
    {
        var w = await SeedAsync();
        var ids = new List<Guid>();
        try
        {
            var planned = await CreateAsync(w, "Planeado", Muscle.Neck, []);
            var other = await CreateAsync(w, "Otro", Muscle.Neck, []);
            ids.AddRange([planned, other]);
            var (_, lineId, _) = await AssignAsync(w, planned);

            var log = await LogAsync(w, lineId, new LogRoutineExerciseRequest(
                null, [new LogSetRequest("normal", 12, 30m, null, null, null)], other));
            Assert.Equal(other, log.ExerciseId);
            Assert.Equal(lineId, log.RoutineExerciseId);

            var item = Assert.Single(await HistoryAsync(w));
            Assert.Equal(other, item.ExerciseId);
            Assert.Equal("Otro " + w.Suffix, item.ExerciseName);
            Assert.Equal(planned, item.PlannedExerciseId);
            Assert.Equal("Planeado " + w.Suffix, item.PlannedExerciseName);
            Assert.Equal((12, 30m), (item.Sets![0].Reps, item.Sets![0].WeightKg));

            // La rutina sigue igual: misma línea, mismo ejercicio planeado, sin líneas nuevas ni retiradas.
            using (var scope = Staff(w))
            {
                var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
                var line = await db.Set<RoutineExercise>().SingleAsync(e => e.Id == lineId);
                Assert.Equal(planned, line.ExerciseId);
                Assert.Null(line.RetiredOn);
                Assert.Equal(1, await db.Set<RoutineExercise>().CountAsync(e => e.Id == lineId || e.ExerciseId == planned));
            }

            // Volver a registrar ese día sin reemplazo deja el ejercicio planeado y sigue siendo un solo registro.
            var again = await LogAsync(w, lineId, new LogRoutineExerciseRequest(null, [new LogSetRequest("normal", 8, 40m, null, null, null)]));
            Assert.Equal(planned, again.ExerciseId);
            var back = Assert.Single(await HistoryAsync(w));
            Assert.Equal(planned, back.ExerciseId);
            Assert.Null(back.PlannedExerciseId);
        }
        finally
        {
            await CleanupAsync(w, ids);
        }
    }

    [Fact]
    public async Task A_replacement_outside_the_alternatives_list_is_rejected_and_nothing_is_logged()
    {
        var w = await SeedAsync();
        var ids = new List<Guid>();
        try
        {
            var planned = await CreateAsync(w, "Planeado", Muscle.Neck, []);
            var stranger = await CreateAsync(w, "Ajeno", Muscle.Calves, []);
            ids.AddRange([planned, stranger]);
            var (_, lineId, _) = await AssignAsync(w, planned);

            using (var scope = AsMember(w))
            {
                await Assert.ThrowsAsync<ValidationDomainException>(() =>
                    scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>().HandleAsync(
                        lineId, new LogRoutineExerciseRequest(null, null, stranger), default));
            }

            Assert.Empty(await HistoryAsync(w));

            // Una alternativa recomendada, aunque sea de otro músculo, sí vale.
            using (var scope = Staff(w))
            {
                await scope.ServiceProvider.GetRequiredService<AddExerciseAlternativeHandler>().HandleAsync(planned, stranger, default);
            }

            var ok = await LogAsync(w, lineId, new LogRoutineExerciseRequest(null, null, stranger));
            Assert.Equal(stranger, ok.ExerciseId);
        }
        finally
        {
            await CleanupAsync(w, ids);
        }
    }

    // --- HTTP con la identidad real ---

    [Fact]
    public async Task Http_member_reads_alternatives_but_only_staff_manages_recommended_ones()
    {
        var w = await SeedHttpAsync();
        var seed = new World(w.GymId, w.StaffId, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, w.Suffix);
        var ids = new List<Guid>();
        try
        {
            var a = await CreateAsync(seed, "Http uno", Muscle.Neck, []);
            var b = await CreateAsync(seed, "Http dos", Muscle.Chest, []);
            ids.AddRange([a, b]);

            await using var host = RealIdentityHost();
            var staff = await LoggedInAsync(host, w.StaffEmail);
            var member = await LoggedInAsync(host, w.MemberEmail);

            Assert.Equal(HttpStatusCode.NoContent, (await staff.PutAsync($"/api/exercises/{a}/alternatives/{b}", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsync($"/api/exercises/{a}/alternatives/{b}", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync($"/api/exercises/{a}/alternatives/{b}")).StatusCode);

            var list = await member.GetFromJsonAsync<JsonElement>($"/api/exercises/{a}/alternatives");
            var first = list[0];
            Assert.Equal(b, first.GetProperty("id").GetGuid());
            Assert.Equal("recommended", first.GetProperty("source").GetString());
            Assert.True(first.GetProperty("isAvailableHere").GetBoolean());

            Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/exercises/{Guid.NewGuid()}/alternatives")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/exercises/{a}/alternatives/{b}")).StatusCode);
        }
        finally
        {
            await CleanupAsync(seed, ids);
        }
    }

    // --- Ayudas ---

    private async Task<Guid> CreateAsync(
        World w, string name, Muscle muscle, IReadOnlyList<Muscle> secondary,
        IReadOnlyList<EquipmentType>? equipment = null, TrackingType? tracking = null)
    {
        using var scope = Staff(w);
        return (await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>().HandleAsync(
            new CreateExerciseRequest(
                $"{name} {w.Suffix}", null, muscle, secondary, equipment ?? [], null, null,
                ExerciseType.Strength, tracking ?? TrackingType.RepsWeight, false, Difficulty.Intermediate),
            default)).Id;
    }

    private async Task<IReadOnlyList<ExerciseAlternativeResponse>> AlternativesAsync(World w, Guid exerciseId)
    {
        using var scope = Staff(w);
        return await scope.ServiceProvider.GetRequiredService<GetExerciseAlternativesHandler>().HandleAsync(exerciseId, default);
    }

    private async Task<int> CountPairsAsync(Guid a, Guid b)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await db.Set<ExerciseAlternative>().CountAsync(p =>
            (p.ExerciseId == a && p.AlternativeId == b) || (p.ExerciseId == b && p.AlternativeId == a));
    }

    private async Task<(Guid RoutineId, Guid LineId, Guid DayId)> AssignAsync(World w, Guid exerciseId)
    {
        using var scope = Staff(w);
        var routine = await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>().HandleAsync(
            w.MemberId,
            new SaveRoutineRequest("Rutina R5", null,
                [new RoutineDayInput("D1", false, [new RoutineExerciseInput(exerciseId, TestSets.Normal(3, 10), 60, null)])]),
            default);
        var day = routine.Days.Single();
        return (routine.Id, day.Exercises.Single().Id, day.Id);
    }

    private async Task<RoutineLogResponse> LogAsync(World w, Guid lineId, LogRoutineExerciseRequest request)
    {
        using var scope = AsMember(w);
        return await scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>().HandleAsync(lineId, request, default);
    }

    private async Task<IReadOnlyList<RoutineHistoryItem>> HistoryAsync(World w)
    {
        using var scope = AsMember(w);
        return await scope.ServiceProvider.GetRequiredService<GetMyRoutineHistoryHandler>().HandleAsync(Day, Day, default);
    }

    /// <summary>Borra lo desechable: registros, líneas y ejercicios del gym (las alternativas caen en cascada).</summary>
    private async Task CleanupAsync(World w, IEnumerable<Guid> exerciseIds)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var ids = exerciseIds.ToArray();

        await db.Database.ExecuteSqlRawAsync(
            "delete from routines.routine_exercise_logs where exercise_id = any({0}) or member_id = {1}", ids, w.MemberId);
        await db.Database.ExecuteSqlRawAsync("delete from routines.routine_exercises where exercise_id = any({0})", ids);
        await db.Database.ExecuteSqlRawAsync(
            "delete from routines.routines where member_id = {0}", w.MemberId);
        await db.Set<Exercise>().Where(e => ids.Contains(e.Id) && e.GymId != null).ExecuteDeleteAsync();
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Staff(World w) => Staff(w.GymA, w.StaffA);

    private IServiceScope Staff(Guid gymId, Guid staffId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Day);
        return scope;
    }

    private IServiceScope SuperAdmin()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), null, UserRole.SuperAdmin);
        return scope;
    }

    private IServiceScope AsMember(World w)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserId, w.GymA, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Day);
        return scope;
    }

    private sealed record World(
        Guid GymA, Guid StaffA, Guid MemberId, Guid MemberUserId, Guid GymB, Guid StaffB, string Suffix);

    private async Task<World> SeedAsync()
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gymA = Gym.Create("R5 A", $"r5-a-{suffix}");
        var gymB = Gym.Create("R5 B", $"r5-b-{suffix}");
        var memberUser = MakeUser($"r5-m-{suffix}");
        var member = Member.Create(gymA.Id, "Miembro", "R5", $"doc-r5-{suffix}", new DateOnly(2026, 1, 1));
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
        var gym = Gym.Create("R5 Http", $"r5-http-{suffix}");
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var ownerEmail = $"r5-owner-{suffix}@test.local";
        var staffEmail = $"r5-staff-{suffix}@test.local";
        var memberEmail = $"r5-member-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "R5 Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "R5 Staff", Password, UserRole.Staff, gym.Id);
        await provisioning.CreateMemberUserAsync(memberEmail, "R5 Member", Password, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new HttpWorld(gym.Id, staffId, staffEmail, memberEmail, suffix);
    }
}
