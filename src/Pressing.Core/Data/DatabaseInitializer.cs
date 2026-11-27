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
    /// Recalcule PhoneNormalized avec la rÃ¨gle courante (bases crÃ©Ã©es avec une ancienne rÃ¨gle).
    /// Idempotent : ne touche que les clients dont la valeur diffÃ¨re. Si deux clients aboutissent au mÃªme
    /// numÃ©ro canonique (index unique), le second est laissÃ© tel quel pour ne pas bloquer le dÃ©marrage.
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
            // Mise Ã  jour en deux temps possible si des Ã©changes de valeurs existent : les collisions sont ignorÃ©es plus haut.
            await _db.SaveChangesAsync(ct);
        }
    }
}
