using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Options;
using Pressing.Core.Security;

namespace Pressing.Core.Data;

public partial class DatabaseInitializer
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
    await MigrateOrAdoptAsync(ct);
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
}
