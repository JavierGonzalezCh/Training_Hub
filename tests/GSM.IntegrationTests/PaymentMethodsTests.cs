using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Payments;
using GSM.Infrastructure.Common;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>P1: los 8 métodos de pago de Colombia y la migración de <c>transfer</c> a <c>bank_transfer</c>.</summary>
public sealed class PaymentMethodsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly (PaymentMethod Method, string Db, string Json)[] Expected =
    [
        (PaymentMethod.Cash, "cash", "Cash"),
        (PaymentMethod.Card, "card", "Card"),
        (PaymentMethod.Nequi, "nequi", "Nequi"),
        (PaymentMethod.Daviplata, "daviplata", "Daviplata"),
        (PaymentMethod.Breb, "breb", "Breb"),
        (PaymentMethod.BankTransfer, "bank_transfer", "BankTransfer"),
        (PaymentMethod.BankDeposit, "bank_deposit", "BankDeposit"),
        (PaymentMethod.Other, "other", "Other"),
    ];

    [Fact]
    public void Only_cash_goes_to_the_cash_drawer()
    {
        foreach (var (method, _, _) in Expected)
        {
            Assert.Equal(method == PaymentMethod.Cash, method.GoesToCashDrawer());
        }
    }

    [Fact]
    public async Task Every_method_is_recorded_stored_snake_case_and_read_back_and_the_close_counts_cash_only_as_cash()
    {
        var (gymId, staffId) = await SeedAsync();
        var day = new DateOnly(2026, 4, 14);

        using (var scope = Staff(gymId, staffId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
            var record = scope.ServiceProvider.GetRequiredService<RecordPaymentHandler>();
            var amount = 1_000m;
            foreach (var (method, _, json) in Expected)
            {
                var created = await record.HandleAsync(
                    new RecordPaymentRequest(amount, method, $"Cobro {json}", null, null, null), default);
                Assert.Equal(json, created.Method);
                amount += 1_000m;
            }
        }

        using (var scope = Staff(gymId, staffId))
        {
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);

            var close = await scope.ServiceProvider.GetRequiredService<GetDailyCashRegisterHandler>()
                .HandleAsync(null, default);

            Assert.Equal(8, close.TransactionCount);
            Assert.Equal(1_000m, close.TotalsByMethod["Cash"]);          // solo el cobro en efectivo
            Assert.Equal(8, close.TotalsByMethod.Count);
            Assert.Equal(6_000m, close.TotalsByMethod["BankTransfer"]);
            Assert.Equal(close.Total, close.TotalsByMethod.Values.Sum());
            foreach (var (_, _, json) in Expected)
            {
                Assert.Contains(json, close.Payments.Select(p => p.Method));
            }

            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var stored = await db.Database
                .SqlQueryRaw<string>("select method::text as \"Value\" from payments.payments where gym_id = {0}", gymId)
                .ToListAsync();
            Assert.Equal(Expected.Select(e => e.Db).Order(), stored.Order());
        }
    }

    [Fact]
    public async Task Migration_converts_an_existing_transfer_payment_to_bank_transfer()
    {
        var name = $"{ApiFactory.TestDatabaseName}_p1migr";
        var admin = $"Host=localhost;Port=5432;Database=postgres;Username=gsm;Password=gsm";
        var conn = $"Host=localhost;Port=5432;Database={name};Username=gsm;Password=gsm";

        await using (var c = new NpgsqlConnection(admin))
        {
            await c.OpenAsync();
            await Exec(c, $"drop database if exists \"{name}\" with (force)");
            await Exec(c, $"create database \"{name}\"");
        }

        try
        {
            var options = new DbContextOptionsBuilder<GsmDbContext>()
                .UseNpgsql(conn, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(GsmDbContext).Assembly.FullName);
                    PgEnumMappings.ConfigureProvider(npgsql);
                })
                .UseSnakeCaseNamingConvention()
                .Options;

            await using var db = new GsmDbContext(options, NullCurrentUser.Instance);
            var migrator = db.GetService<IMigrator>();
            var all = db.Database.GetMigrations().ToList();
            var p1 = all.Single(m => m.EndsWith("V2_P1_PaymentMethods"));
            var before = all[all.IndexOf(p1) - 1];

            await migrator.MigrateAsync(before);

            var id = Guid.CreateVersion7();
            await using (var c = new NpgsqlConnection(conn))
            {
                await c.OpenAsync();
                await Exec(c, $"""
                    insert into payments.payments (id, gym_id, amount, method, concept, registered_by, paid_at, created_at, updated_at)
                    values ('{id}', '{Guid.NewGuid()}', 50000, 'transfer', 'Mensualidad', '{Guid.NewGuid()}', now(), now(), now())
                    """);
            }

            await migrator.MigrateAsync();

            await using (var c = new NpgsqlConnection(conn))
            {
                await c.OpenAsync();
                await using var cmd = new NpgsqlCommand($"select method::text from payments.payments where id = '{id}'", c);
                Assert.Equal("bank_transfer", (string?)await cmd.ExecuteScalarAsync());

                await using var labels = new NpgsqlCommand(
                    "select string_agg(e.enumlabel, ',' order by e.enumsortorder) from pg_enum e join pg_type t on t.oid = e.enumtypid where t.typname = 'payment_method'", c);
                var list = ((string?)await labels.ExecuteScalarAsync())!.Split(',');
                Assert.Equal(
                    new[] { "bank_deposit", "bank_transfer", "breb", "card", "cash", "daviplata", "nequi", "other" },
                    list.Order());
                Assert.DoesNotContain("transfer", list);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var c = new NpgsqlConnection(admin);
            await c.OpenAsync();
            await Exec(c, $"drop database if exists \"{name}\" with (force)");
        }
    }

    private static async Task Exec(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private IServiceScope Staff(Guid gymId, Guid staffId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        return scope;
    }

    private async Task<(Guid GymId, Guid StaffId)> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships, payments.payments restart identity cascade;");

        var gym = Gym.Create("Methods Gym", $"meth-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@meth.local",
            Email = "staff@meth.local",
            NormalizedUserName = "STAFF@METH.LOCAL",
            NormalizedEmail = "STAFF@METH.LOCAL",
            EmailConfirmed = true,
            FullName = "Methods Staff",
            Role = UserRole.Staff,
            GymId = gym.Id,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        await db.SaveChangesAsync();

        return (gym.Id, staff.Id);
    }
}
