using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pressing.Core.Options;
using Pressing.Core.Services;
using Pressing.Tests.Support;

namespace Pressing.Tests.Unit;

/// <summary>
/// Sauvegardes planifiées : déclenchement à l'heure, une seule fois par jour, rotation des seules sauvegardes
/// automatiques, copie secondaire dont l'échec ne bloque pas.
/// </summary>
public sealed class ScheduledBackupTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly string _copyRoot = Path.Combine(Path.GetTempPath(), "pressing-copie-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 12, 1, 21, 59, 0, TimeSpan.Zero);

    private string BackupDir => _t.DbOptions.BackupPath;

    private sealed class MovableTime(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private ScheduledBackupService Service(BackupScheduleOptions? options = null, ILogger<ScheduledBackupService>? logger = null) =>
        new(_t.Settings, _t.DbOptions, options ?? new BackupScheduleOptions(), new MovableTime(() => _now),
            logger ?? NullLogger<ScheduledBackupService>.Instance);

    private string[] AutoFiles(string? dir = null) =>
        Directory.Exists(dir ?? BackupDir)
            ? Directory.GetFiles(dir ?? BackupDir, "pressing-auto-*.db").Select(f => Path.GetFileName(f)).Order().ToArray()
            : [];

    private static string Touch(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, "faux fichier de sauvegarde");
        return path;
    }

    [Fact]
    public async Task Avant_l_heure_rien_n_est_sauvegarde()
    {
        var result = await Service().RunIfDueAsync();

        Assert.Equal(ScheduledBackupStatus.NotDue, result.Status);
        Assert.Empty(AutoFiles());
    }

    [Fact]
    public async Task A_l_heure_une_sauvegarde_verifiee_est_creee()
    {
        _now = new DateTimeOffset(2026, 12, 1, 22, 0, 0, TimeSpan.Zero);

        var result = await Service().RunIfDueAsync();

        Assert.Equal(ScheduledBackupStatus.Created, result.Status);
        Assert.True(result.Backup!.IntegrityOk);
        Assert.False(result.CopyFailed);
        Assert.Equal("pressing-auto-20261201-220000.db", result.Backup.FileName);
        Assert.Equal(["pressing-auto-20261201-220000.db"], AutoFiles());
    }

    [Fact]
    public async Task Heure_parametrable()
    {
        var options = new BackupScheduleOptions { Hour = 3 };
        _now = new DateTimeOffset(2026, 12, 1, 2, 59, 0, TimeSpan.Zero);
        Assert.Equal(ScheduledBackupStatus.NotDue, (await Service(options).RunIfDueAsync()).Status);

        _now = _now.AddMinutes(1);
        Assert.Equal(ScheduledBackupStatus.Created, (await Service(options).RunIfDueAsync()).Status);
    }

    [Fact]
    public async Task Une_seule_sauvegarde_par_jour_meme_apres_un_redemarrage()
    {
        _now = new DateTimeOffset(2026, 12, 1, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(ScheduledBackupStatus.Created, (await Service().RunIfDueAsync()).Status);

        _now = _now.AddMinutes(5);
        Assert.Equal(ScheduledBackupStatus.AlreadyDone, (await Service().RunIfDueAsync()).Status);
        _now = _now.AddHours(1);
        Assert.Equal(ScheduledBackupStatus.AlreadyDone, (await Service().RunIfDueAsync()).Status);

        Assert.Single(AutoFiles());
    }

    [Fact]
    public async Task Le_lendemain_une_nouvelle_sauvegarde_est_creee()
    {
        _now = new DateTimeOffset(2026, 12, 1, 22, 0, 0, TimeSpan.Zero);
        await Service().RunIfDueAsync();

        _now = new DateTimeOffset(2026, 12, 2, 22, 5, 0, TimeSpan.Zero);
        var result = await Service().RunIfDueAsync();

        Assert.Equal(ScheduledBackupStatus.Created, result.Status);
        Assert.Equal(2, AutoFiles().Length);
    }

    [Fact]
    public async Task Interrupteur_coupe_aucune_sauvegarde()
    {
        _now = new DateTimeOffset(2026, 12, 1, 23, 0, 0, TimeSpan.Zero);

        var result = await Service(new BackupScheduleOptions { Enabled = false }).RunIfDueAsync();

        Assert.Equal(ScheduledBackupStatus.Disabled, result.Status);
        Assert.Empty(AutoFiles());
    }

    [Fact]
    public async Task Rotation_supprime_seulement_les_sauvegardes_automatiques_expirees()
    {
        _now = new DateTimeOffset(2026, 12, 1, 22, 0, 0, TimeSpan.Zero);
        // Conservation 30 jours : le 1er décembre, la limite est le 1er novembre (inclus).
        Touch(BackupDir, "pressing-auto-20261031-220000.db");   // 31 jours : supprimée
        Touch(BackupDir, "pressing-auto-20260101-220000.db");   // très ancienne : supprimée
        Touch(BackupDir, "pressing-auto-20261101-220000.db");   // 30 jours : conservée
        Touch(BackupDir, "pressing-auto-20261130-220000.db");   // hier : conservée
        // Jamais supprimées, même très anciennes :
        Touch(BackupDir, "pressing-20260101-100000.db");                    // manuelle
        Touch(BackupDir, "pressing-avant-migration-20260101-100000.db");   // avant migration
        Touch(BackupDir, "pressing-avant-adoption-20260101-100000.db");    // avant adoption
        Touch(BackupDir, "pressing-auto-notes.db");                         // nom inattendu : ignoré

        var result = await Service().RunIfDueAsync();

        Assert.Equal(ScheduledBackupStatus.Created, result.Status);
        Assert.Equal(2, result.Deleted);
        Assert.Equal(
            ["pressing-auto-20261101-220000.db", "pressing-auto-20261130-220000.db",
             "pressing-auto-20261201-220000.db", "pressing-auto-notes.db"],
            AutoFiles());
        Assert.True(File.Exists(Path.Combine(BackupDir, "pressing-20260101-100000.db")));
        Assert.True(File.Exists(Path.Combine(BackupDir, "pressing-avant-migration-20260101-100000.db")));
        Assert.True(File.Exists(Path.Combine(BackupDir, "pressing-avant-adoption-20260101-100000.db")));
    }

    [Fact]
    public async Task Duree_de_conservation_parametrable()
    {
        _now = new DateTimeOffset(2026, 12, 10, 22, 0, 0, TimeSpan.Zero);
        Touch(BackupDir, "pressing-auto-20261202-220000.db");   // 8 jours
        Touch(BackupDir, "pressing-auto-20261208-220000.db");   // 2 jours

        await Service(new BackupScheduleOptions { RetentionDays = 7 }).RunIfDueAsync();

        Assert.DoesNotContain("pressing-auto-20261202-220000.db", AutoFiles());
        Assert.Contains("pressing-auto-20261208-220000.db", AutoFiles());
    }

    [Fact]
    public async Task Copie_secondaire_et_rotation_dans_le_dossier_de_copie()
    {
        _now = new DateTimeOffset(2026, 12, 1, 22, 0, 0, TimeSpan.Zero);
        Touch(_copyRoot, "pressing-auto-20261001-220000.db");   // expirée : supprimée aussi sur le disque de copie
        Touch(_copyRoot, "pressing-20260101-100000.db");        // manuelle : conservée
        var options = new BackupScheduleOptions { CopyPath = _copyRoot };

        var result = await Service(options).RunIfDueAsync();

        Assert.Equal(ScheduledBackupStatus.Created, result.Status);
        Assert.False(result.CopyFailed);
        string copy = Path.Combine(_copyRoot, "pressing-auto-20261201-220000.db");
        Assert.True(File.Exists(copy));
        Assert.Equal(new FileInfo(result.Backup!.Path).Length, new FileInfo(copy).Length);
        Assert.Equal(["pressing-auto-20261201-220000.db"], AutoFiles(_copyRoot));
        Assert.True(File.Exists(Path.Combine(_copyRoot, "pressing-20260101-100000.db")));
    }

    [Fact]
    public async Task Echec_de_copie_journalise_sans_bloquer_la_sauvegarde()
    {
        _now = new DateTimeOffset(2026, 12, 1, 22, 0, 0, TimeSpan.Zero);
        // Le « dossier » de copie est en réalité un fichier : impossible d'y créer ou d'y copier quoi que ce soit.
        string notAFolder = Touch(_copyRoot, "occupe");
        var logger = new CapturingLogger();
        var options = new BackupScheduleOptions { CopyPath = notAFolder };

        var result = await Service(options, logger).RunIfDueAsync();

        Assert.Equal(ScheduledBackupStatus.Created, result.Status);
        Assert.True(result.CopyFailed);
        Assert.True(File.Exists(result.Backup!.Path));
        Assert.True(result.Backup.IntegrityOk);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Copie de la sauvegarde"));

        // La sauvegarde locale compte comme faite : pas de seconde sauvegarde dans la journée.
        Assert.Equal(ScheduledBackupStatus.AlreadyDone, (await Service(options, logger).RunIfDueAsync()).Status);
    }

    [Fact]
    public void Variables_d_environnement_valeurs_par_defaut_et_valeurs_invalides()
    {
        var defaults = BackupScheduleOptions.From(_ => null);
        Assert.True(defaults.Enabled);
        Assert.Equal(22, defaults.Hour);
        Assert.Equal(30, defaults.RetentionDays);
        Assert.Equal(string.Empty, defaults.CopyPath);

        var custom = BackupScheduleOptions.From(name => name switch
        {
            "BACKUP_SCHEDULE_ENABLED" => "false",
            "BACKUP_HOUR" => "6",
            "BACKUP_RETENTION_DAYS" => "90",
            "BACKUP_COPY_PATH" => "  E:\\Sauvegardes  ",
            _ => null
        });
        Assert.False(custom.Enabled);
        Assert.Equal(6, custom.Hour);
        Assert.Equal(90, custom.RetentionDays);
        Assert.Equal("E:\\Sauvegardes", custom.CopyPath);

        var invalid = BackupScheduleOptions.From(name => name switch
        {
            "BACKUP_HOUR" => "25",
            "BACKUP_RETENTION_DAYS" => "0",
            _ => "n'importe quoi"
        });
        Assert.Equal(22, invalid.Hour);
        Assert.Equal(30, invalid.RetentionDays);
        Assert.True(invalid.Enabled);
    }

    public void Dispose()
    {
        _t.Dispose();
        try { Directory.Delete(_copyRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class CapturingLogger : ILogger<ScheduledBackupService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
