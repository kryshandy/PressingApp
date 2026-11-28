using Microsoft.EntityFrameworkCore;
using Pressing.Core.Options;
using Pressing.Core.Security;

namespace Pressing.Core.Data;

public class DatabaseInitializer
{
    private readonly PressingDbContext _db;
    private readonly PressingDatabaseOptions _options;

    public DatabaseInitializer(PressingDbContext db, PressingDatabaseOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        PressingDatabase.EnsureDirectory(_options.Path);
        await _db.Database.EnsureCreatedAsync(ct);
        await EnsureClientConsentColumnsAsync(ct);

        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS ReminderLogs (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                OrderId INTEGER NOT NULL,
                ClientId INTEGER NOT NULL,
                ReminderType TEXT NOT NULL,
                Channel TEXT NOT NULL,
                SentAt TEXT NOT NULL,
                Success INTEGER NOT NULL,
                Error TEXT NULL
            );
            """, ct);

        await _db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ReminderLogs_OrderId_ReminderType ON ReminderLogs (OrderId, ReminderType);", ct);
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ReminderLogs_SentAt ON ReminderLogs (SentAt);", ct);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", ct);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=10000;", ct);

        await DbSeeder.SeedSettingsAsync(_db, ct);
        await DbSeeder.SeedStaffAsync(_db, _options.AdminUsername, _options.AdminPassword, ct);
        await DbSeeder.SeedCatalogAsync(_db, ct);
        await DbSeeder.SeedPromotionsAsync(_db, ct);

        if (_options.SeedDemoData)
        {
            await DbSeeder.SeedDemoAsync(_db, ct);
        }

        await RenormalizePhonesAsync(ct);
    }

    /// <summary>
    /// Recalcule PhoneNormalized avec la règle courante (bases créées avec une ancienne règle).
    /// Idempotent : ne touche que les clients dont la valeur diffère. Si deux clients aboutissent au même
    /// numéro canonique (index unique), le second est laissé tel quel pour ne pas bloquer le démarrage.
    /// </summary>
    private async Task RenormalizePhonesAsync(CancellationToken ct)
    {
        var clients = await _db.Clients.ToListAsync(ct);
        var taken = new HashSet<string>(clients.Select(c => c.PhoneNormalized));
        bool changed = false;

        foreach (var client in clients)
        {
            string expected = PhoneNormalizer.Normalize(client.Phone);
            if (expected == client.PhoneNormalized)
            {
                continue;
            }

            if (taken.Contains(expected))
            {
                continue;
            }

            taken.Remove(client.PhoneNormalized);
            taken.Add(expected);
            client.PhoneNormalized = expected;
            changed = true;
        }

        if (changed)
        {
            // Mise à jour en deux temps possible si des échanges de valeurs existent : les collisions sont ignorées plus haut.
            await _db.SaveChangesAsync(ct);
        }
    }
    private async Task EnsureClientConsentColumnsAsync(CancellationToken ct)
    {
        var columns = new HashSet<string>();

        await using (var command = _db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('Clients');";

            if (command.Connection!.State != System.Data.ConnectionState.Open)
                await command.Connection.OpenAsync(ct);

            await using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains("NotificationPreference"))
        {
            await _db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Clients ADD COLUMN NotificationPreference TEXT NOT NULL DEFAULT 'None';", ct);
        }

        if (!columns.Contains("ConsentGivenAt"))
        {
            await _db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Clients ADD COLUMN ConsentGivenAt TEXT NULL;", ct);
        }

        if (!columns.Contains("ConsentRevokedAt"))
        {
            await _db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Clients ADD COLUMN ConsentRevokedAt TEXT NULL;", ct);
        }
    }
}
