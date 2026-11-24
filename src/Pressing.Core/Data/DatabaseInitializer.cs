using Microsoft.EntityFrameworkCore;
using Pressing.Core.Options;

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
    }
}
