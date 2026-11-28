using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pressing.Core;
using Pressing.Core.Data;
using Pressing.Core.Entities;
using Pressing.Core.Options;

namespace Pressing.Tests.Integration;

/// <summary>
/// Passage aux migrations sans perte : base neuve, base v2 créée par EnsureCreated, et base passée par la
/// version du 27/11 (schéma de consentement et de rappels ajouté en SQL brut).
/// </summary>
public sealed class MigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pressing-migrations-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public MigrationTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "pressing.db");
    }

    [Fact]
    public async Task Base_neuve_recoit_toutes_les_migrations()
    {
        await InitializeAsync();

        await using var db = NewContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.True(await db.Categories.AnyAsync(), "catalogue initial");
        Assert.Empty(await db.ReminderLogs.ToListAsync());
    }

    [Fact]
    public async Task Base_v2_sans_historique_est_adoptee_et_completee_sans_perte()
    {
        await CreateV2DatabaseAsync();
        await InsertClientAsync();

        await InitializeAsync();

        await using var db = NewContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Pressing.Core.Entities.Client client = await db.Clients.SingleAsync(c => c.LastName == "Ancienne");
        Assert.Equal(ClientNotificationPreference.None, client.NotificationPreference);
        Assert.Null(client.ConsentGivenAt);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_directory, "Backups"), "pressing-avant-adoption-*.db"));
    }

    [Fact]
    public async Task Base_du_27_11_modifiee_en_sql_brut_est_adoptee_sans_erreur()
    {
        await CreateV2DatabaseAsync();
        await ExecuteAsync("""
            ALTER TABLE Clients ADD COLUMN NotificationPreference TEXT NOT NULL DEFAULT 'None';
            ALTER TABLE Clients ADD COLUMN ConsentGivenAt TEXT NULL;
            ALTER TABLE Clients ADD COLUMN ConsentRevokedAt TEXT NULL;
            CREATE TABLE IF NOT EXISTS ReminderLogs (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, OrderId INTEGER NOT NULL, ClientId INTEGER NOT NULL,
                ReminderType TEXT NOT NULL, Channel TEXT NOT NULL, SentAt TEXT NOT NULL, Success INTEGER NOT NULL, Error TEXT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_ReminderLogs_OrderId_ReminderType ON ReminderLogs (OrderId, ReminderType);
            """);
        await InsertClientAsync();

        await InitializeAsync();

        await using var db = NewContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations().Count(), (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(ClientNotificationPreference.None,
            (await db.Clients.SingleAsync(c => c.LastName == "Ancienne")).NotificationPreference);
    }

    [Fact]
    public async Task Base_v2_passe_a_ClotureDeCaisse_sans_perte()
    {
        await CreateV2DatabaseAsync();
        await InsertClientAsync();

        await InitializeAsync();

        await using var db = NewContext();
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_ClotureDeCaisse", StringComparison.Ordinal));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal("Ancienne", (await db.Clients.SingleAsync()).LastName);
        Assert.Empty(await db.CashClosings.ToListAsync());
        await AssertCashClosingSchemaAsync();
    }

    [Fact]
    public async Task Base_a_la_migration_precedente_passe_a_ClotureDeCaisse_sans_perte()
    {
        await using (var previous = NewContext())
        {
            string target = previous.Database.GetMigrations().Single(m => m.EndsWith("_RappelsEtConsentement", StringComparison.Ordinal));
            await previous.GetService<IMigrator>().MigrateAsync(target);
        }

        await InsertClientAsync();
        await ExecuteAsync("INSERT INTO ReminderLogs (OrderId, ClientId, ReminderType, Channel, SentAt, Success) VALUES (1, 1, 'Ready', 'WhatsApp', '2026-11-02 09:00:00', 1);");

        await InitializeAsync();

        await using var db = NewContext();
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_ClotureDeCaisse", StringComparison.Ordinal));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal("Ancienne", (await db.Clients.SingleAsync()).LastName);
        Assert.Single(await db.ReminderLogs.ToListAsync());
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_directory, "Backups"), "pressing-avant-migration-*.db"));
        await AssertCashClosingSchemaAsync();
    }

    [Fact]
    public async Task Base_a_la_migration_precedente_passe_a_SecuritePersonnel_sans_perte()
    {
        await using (var previous = NewContext())
        {
            string target = previous.Database.GetMigrations().Single(m => m.EndsWith("_ClotureDeCaisse", StringComparison.Ordinal));
            await previous.GetService<IMigrator>().MigrateAsync(target);
        }

        await ExecuteAsync("INSERT INTO StaffUsers (FullName, Username, PasswordHash, Role, IsActive, CreatedAt, UpdatedAt) " +
                           "VALUES ('Awa Manager', 'awa', 'hash-existant', 'Manager', 1, '2026-11-01 08:00:00', '2026-11-01 08:00:00');");

        await InitializeAsync();

        await using var db = NewContext();
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_SecuritePersonnel", StringComparison.Ordinal));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        StaffUser staff = await db.StaffUsers.SingleAsync(s => s.Username == "awa");
        Assert.Equal("hash-existant", staff.PasswordHash);
        Assert.Equal(UserRole.Manager, staff.Role);
        Assert.Equal(0, staff.FailedLoginCount);
        Assert.Null(staff.LockedUntil);
        Assert.False(staff.TwoFactorEnabled);
        Assert.Null(staff.TwoFactorSecret);
        Assert.Equal(0L, staff.TwoFactorLastStep);
        Assert.Null(staff.RecoveryCodeHashes);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_directory, "Backups"), "pressing-avant-migration-*.db"));
    }

    [Fact]
    public async Task Base_a_SecuritePersonnel_passe_a_EnqueteSatisfaction_sans_perte()
    {
        await using (var previous = NewContext())
        {
            string target = previous.Database.GetMigrations().Single(m => m.EndsWith("_SecuritePersonnel", StringComparison.Ordinal));
            await previous.GetService<IMigrator>().MigrateAsync(target);
        }

        await InsertClientAsync();
        await ExecuteAsync("INSERT INTO ReminderLogs (OrderId, ClientId, ReminderType, Channel, SentAt, Success) VALUES (1, 1, 'ready', 'whatsapp', '2026-11-02 09:00:00', 1);");

        await InitializeAsync();

        await using var db = NewContext();
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_EnqueteSatisfaction", StringComparison.Ordinal));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal("Ancienne", (await db.Clients.SingleAsync()).LastName);
        Assert.Single(await db.ReminderLogs.ToListAsync());
        Assert.Empty(await db.Feedbacks.ToListAsync());
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_directory, "Backups"), "pressing-avant-migration-*.db"));

        // Index unique sur OrderId : une seule réponse par commande.
        int clientId = (await db.Clients.SingleAsync()).Id;
        var order = new Order { TicketNumber = "PRESS-2026-00001", ClientId = clientId, Status = OrderStatus.Completed, DueDate = DateTime.Today };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        db.Feedbacks.Add(new Feedback { OrderId = order.Id, ClientId = clientId, Note = 5 });
        await db.SaveChangesAsync();
        db.Feedbacks.Add(new Feedback { OrderId = order.Id, ClientId = clientId, Note = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>Une ligne insérée sans statut reçoit « Draft » (jamais ""), et l'index unique ne vise que les clôtures validées.</summary>
    private async Task AssertCashClosingSchemaAsync()
    {
        const string columns = "BusinessDate, OpeningFloat, ExpectedCash, ExpectedMobileMoney, ExpectedCard, ExpectedTransfer, ExpectedOther, CountedCash, Variance, CreatedByName, CreatedAt, UpdatedAt";
        const string values = "'2026-11-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 'Test', '2026-11-01 20:00:00', '2026-11-01 20:00:00'";
        await ExecuteAsync($"INSERT INTO CashClosings ({columns}) VALUES ({values});");
        await ExecuteAsync($"INSERT INTO CashClosings ({columns}) VALUES ({values});");

        await using var db = NewContext();
        List<CashClosing> rows = await db.CashClosings.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(CashClosingStatus.Draft, r.Status));

        await ExecuteAsync($"INSERT INTO CashClosings ({columns}, Status) VALUES ({values}, 'Validated');");
        await Assert.ThrowsAnyAsync<SqliteException>(() =>
            ExecuteAsync($"INSERT INTO CashClosings ({columns}, Status) VALUES ({values}, 'Validated');"));
    }

    [Fact]
    public async Task Deuxieme_demarrage_ne_refait_rien()
    {
        await InitializeAsync();
        await InitializeAsync();

        await using var db = NewContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    // ------------------------------------------------------------------ outils

    /// <summary>Schéma v2 : Baseline seule, sans table d'historique (comme EnsureCreated).</summary>
    private async Task CreateV2DatabaseAsync()
    {
        await using (var db = NewContext())
        {
            string baseline = db.Database.GetMigrations().First();
            await db.GetService<IMigrator>().MigrateAsync(baseline);
        }

        await ExecuteAsync("DROP TABLE \"__EFMigrationsHistory\";");
    }

    private Task InsertClientAsync() => ExecuteAsync("""
        INSERT INTO Clients (FirstName, LastName, Phone, PhoneNormalized, WhatsApp, IsActive, WebAccessEnabled,
                             LoyaltyPoints, CreatedAt, UpdatedAt)
        VALUES ('Cliente', 'Ancienne', '0700000000', '2250700000000', '0700000000', 1, 0, 0,
                '2026-11-01 10:00:00', '2026-11-01 10:00:00');
        """);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task InitializeAsync()
    {
        await using var db = NewContext();
        var options = new PressingDatabaseOptions { Path = _path, SeedDemoData = false };
        await new DatabaseInitializer(db, options).InitializeAsync();
    }

    private PressingDbContext NewContext() => new(PressingDatabase.CreateOptions(_path));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
