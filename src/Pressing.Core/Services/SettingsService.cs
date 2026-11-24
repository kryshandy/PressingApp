using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public class SettingsService
{
    private readonly PressingDbContext _db;
    private readonly PressingDatabaseOptions _databaseOptions;
    private readonly EmailOptions _emailOptions;
    private readonly AuditService _audit;

    public SettingsService(
        PressingDbContext db,
        PressingDatabaseOptions databaseOptions,
        EmailOptions emailOptions,
        AuditService audit)
    {
        _db = db;
        _databaseOptions = databaseOptions;
        _emailOptions = emailOptions;
        _audit = audit;
    }

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        var stored = await _db.AppSettings.AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var result = new Dictionary<string, string>(SettingsKeys.Defaults);
        foreach ((string key, string value) in stored)
        {
            result[key] = value;
        }

        return result;
    }

    public async Task<string> GetAsync(string key, string fallback = "", CancellationToken ct = default)
    {
        AppSetting? setting = await _db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);
        return setting?.Value ?? (SettingsKeys.Defaults.TryGetValue(key, out string? value) ? value : fallback);
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct = default)
    {
        string value = await GetAsync(key, fallback.ToString(CultureInfo.InvariantCulture), ct);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : fallback;
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default)
    {
        string value = await GetAsync(key, fallback.ToString(CultureInfo.InvariantCulture), ct);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct = default)
    {
        string value = await GetAsync(key, fallback.ToString(), ct);
        return bool.TryParse(value, out bool parsed) ? parsed : fallback;
    }

    public async Task<ShopInfo> GetShopInfoAsync(CancellationToken ct = default)
    {
        Dictionary<string, string> values = await GetAllAsync(ct);
        return new ShopInfo(
            values[SettingsKeys.ShopName],
            values[SettingsKeys.ShopLegalName],
            values[SettingsKeys.ShopAddress],
            values[SettingsKeys.ShopCity],
            values[SettingsKeys.ShopPhone],
            values[SettingsKeys.ShopWhatsApp],
            values[SettingsKeys.ShopEmail],
            values[SettingsKeys.ShopSlogan],
            values[SettingsKeys.Currency],
            values[SettingsKeys.ReceiptFooter],
            values[SettingsKeys.OpeningHours]);
    }

    public async Task<SettingsDto> GetDtoAsync(CancellationToken ct = default)
    {
        Dictionary<string, string> values = await GetAllAsync(ct);
        return new SettingsDto(values, GetEmailConfigStatus(values));
    }

    public EmailConfigStatusDto GetEmailConfigStatus(Dictionary<string, string>? settings = null)
    {
        string provider = _emailOptions.EffectiveProvider;
        string from = string.IsNullOrWhiteSpace(_emailOptions.FromEmail)
            ? "non configuré"
            : _emailOptions.FromEmail;
        string? warning = provider == "simulated"
            ? "Aucun fournisseur e-mail n'est configuré. Les messages sont enregistrés en mode simulé."
            : null;

        return new EmailConfigStatusDto(
            _emailOptions.HasResend,
            _emailOptions.HasSmtp,
            provider,
            from,
            _emailOptions.FromName,
            warning);
    }

    public async Task<SettingsDto> UpdateAsync(
        UpdateSettingsRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        var existing = await _db.AppSettings.ToDictionaryAsync(s => s.Key, s => s, ct);
        var allowed = new HashSet<string>(SettingsKeys.Defaults.Keys, StringComparer.OrdinalIgnoreCase);

        foreach ((string key, string rawValue) in request.Values)
        {
            if (!allowed.Contains(key))
            {
                continue;
            }

            string value = (rawValue ?? string.Empty).Trim();
            ValidateSetting(key, value);

            if (existing.TryGetValue(key, out AppSetting? setting))
            {
                setting.Value = value;
                setting.UpdatedAt = DateTime.Now;
            }
            else
            {
                _db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTime.Now });
            }
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "update", "Settings", null, "Mise à jour des paramètres", ct: ct);
        return await GetDtoAsync(ct);
    }

    public async Task<BackupResultDto> CreateBackupAsync(CurrentUser user, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_databaseOptions.BackupPath);
        string fileName = $"pressing-{DateTime.Now:yyyyMMdd-HHmmss}.db";
        string target = Path.Combine(_databaseOptions.BackupPath, fileName);

        var sourceBuilder = new SqliteConnectionStringBuilder(PressingDatabase.BuildConnectionString(_databaseOptions.Path));
        var targetBuilder = new SqliteConnectionStringBuilder { DataSource = target };

        await using (var source = new SqliteConnection(sourceBuilder.ToString()))
        await using (var destination = new SqliteConnection(targetBuilder.ToString()))
        {
            await source.OpenAsync(ct);
            await destination.OpenAsync(ct);
            source.BackupDatabase(destination);
        }

        bool integrityOk = await CheckIntegrityAsync(target, ct);
        long size = new FileInfo(target).Length;
        await _audit.LogAsync(
            user, "backup", "Database", fileName, $"Sauvegarde vérifiée ({size} octets)", ct: ct);

        return new BackupResultDto(fileName, target, size, DateTime.Now, integrityOk);
    }

    public Task<IReadOnlyList<BackupResultDto>> GetBackupsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_databaseOptions.BackupPath))
        {
            return Task.FromResult<IReadOnlyList<BackupResultDto>>(Array.Empty<BackupResultDto>());
        }

        var files = new DirectoryInfo(_databaseOptions.BackupPath)
            .GetFiles("pressing-*.db")
            .OrderByDescending(f => f.CreationTime)
            .Select(f => new BackupResultDto(f.Name, f.FullName, f.Length, f.CreationTime, true))
            .ToList();

        return Task.FromResult<IReadOnlyList<BackupResultDto>>(files);
    }

    private static async Task<bool> CheckIntegrityAsync(string path, CancellationToken ct)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(ct);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            object? result = await command.ExecuteScalarAsync(ct);
            return string.Equals(result?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateSetting(string key, string value)
    {
        if (key is SettingsKeys.DefaultProcessingDays or SettingsKeys.ExpressProcessingDays
            or SettingsKeys.MaxDiscountPercent or SettingsKeys.LoyaltyPointsPerAmount)
        {
            if (!int.TryParse(value, out int number) || number < 0 || number > 100_000_000)
            {
                throw new InvalidOperationException($"La valeur de « {key} » doit être un nombre entier positif.");
            }
        }

        if (key is SettingsKeys.LoyaltyPointValue or SettingsKeys.DefaultProcessingDays)
        {
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number) || number < 0)
            {
                throw new InvalidOperationException($"La valeur de « {key} » doit être un nombre positif.");
            }
        }

        if (key is SettingsKeys.AllowPickupWithBalance or SettingsKeys.LoyaltyEnabled
            or SettingsKeys.EmailNotifications or SettingsKeys.EmailOrderConfirmation
            or SettingsKeys.EmailOrderReady or SettingsKeys.EmailReceipt
            or SettingsKeys.ReceiptShowQr or SettingsKeys.ReceiptShowBarcode)
        {
            if (!bool.TryParse(value, out _))
            {
                throw new InvalidOperationException($"La valeur de « {key} » doit être true ou false.");
            }
        }
    }
}
