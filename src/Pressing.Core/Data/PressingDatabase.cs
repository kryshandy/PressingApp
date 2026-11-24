using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Options;

namespace Pressing.Core.Data;

public static class PressingDatabase
{
    public static PressingDatabaseOptions ResolveOptions(string? contentRoot = null)
    {
        string? envPath = Environment.GetEnvironmentVariable("PRESSING_DB_PATH");
        string defaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pressing");

        string databasePath = !string.IsNullOrWhiteSpace(envPath)
            ? envPath
            : Path.Combine(defaultRoot, "pressing.db");

        if (!Path.IsPathRooted(databasePath))
        {
            databasePath = Path.GetFullPath(Path.Combine(contentRoot ?? Directory.GetCurrentDirectory(), databasePath));
        }

        string? envBackup = Environment.GetEnvironmentVariable("PRESSING_BACKUP_PATH");
        string backupPath = !string.IsNullOrWhiteSpace(envBackup)
            ? envBackup
            : Path.Combine(Path.GetDirectoryName(databasePath) ?? defaultRoot, "Backups");

        if (!Path.IsPathRooted(backupPath))
        {
            backupPath = Path.GetFullPath(Path.Combine(contentRoot ?? Directory.GetCurrentDirectory(), backupPath));
        }

        return new PressingDatabaseOptions
        {
            Path = databasePath,
            BackupPath = backupPath,
            SeedDemoData = ReadBool("PRESSING_SEED_DEMO", true),
            AdminUsername = Environment.GetEnvironmentVariable("PRESSING_ADMIN_USERNAME") ?? "admin",
            AdminPassword = Environment.GetEnvironmentVariable("PRESSING_ADMIN_PASSWORD") ?? "Admin@2026!"
        };
    }

    public static string BuildConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 10
        };

        return builder.ToString();
    }

    public static DbContextOptions<PressingDbContext> CreateOptions(string databasePath)
    {
        EnsureDirectory(databasePath);
        return new DbContextOptionsBuilder<PressingDbContext>()
            .UseSqlite(BuildConnectionString(databasePath))
            .EnableSensitiveDataLogging(false)
            .Options;
    }

    public static void EnsureDirectory(string databasePath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static bool ReadBool(string name, bool fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
