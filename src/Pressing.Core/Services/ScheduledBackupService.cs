using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pressing.Core.Dtos;
using Pressing.Core.Options;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public enum ScheduledBackupStatus
{
    /// <summary>BACKUP_SCHEDULE_ENABLED=false.</summary>
    Disabled,

    /// <summary>L'heure de sauvegarde n'est pas encore atteinte aujourd'hui.</summary>
    NotDue,

    /// <summary>La sauvegarde automatique du jour existe déjà.</summary>
    AlreadyDone,

    /// <summary>Sauvegarde créée et vérifiée.</summary>
    Created,

    /// <summary>La sauvegarde a échoué ou n'a pas passé la vérification d'intégrité (nouvel essai au prochain passage).</summary>
    Failed
}

/// <param name="Status">Résultat du passage.</param>
/// <param name="Backup">Sauvegarde créée (<see cref="ScheduledBackupStatus.Created"/> uniquement).</param>
/// <param name="CopyFailed">La copie secondaire (BACKUP_COPY_PATH) a échoué ; la sauvegarde locale, elle, est bonne.</param>
/// <param name="Deleted">Nombre de sauvegardes automatiques expirées supprimées (dossier principal et copie).</param>
public sealed record ScheduledBackupResult(
    ScheduledBackupStatus Status,
    BackupResultDto? Backup = null,
    bool CopyFailed = false,
    int Deleted = 0);

/// <summary>
/// Sauvegarde automatique quotidienne : une fois par jour, à partir de l'heure <see cref="BackupScheduleOptions.Hour"/>,
/// puis rotation des sauvegardes <b>automatiques</b> de plus de <see cref="BackupScheduleOptions.RetentionDays"/> jours.
/// Seuls les fichiers <c>pressing-auto-AAAAMMJJ-HHmmss.db</c> sont supprimés : les sauvegardes manuelles
/// (<c>pressing-AAAAMMJJ-HHmmss.db</c>) et celles d'avant migration / adoption ne sont jamais touchées.
/// « Déjà fait aujourd'hui » se lit dans le dossier de sauvegarde (pas en mémoire) : un redémarrage de l'API
/// ne provoque donc ni doublon ni oubli. Appelée toutes les quelques minutes par la tâche de fond de l'API.
/// </summary>
public sealed partial class ScheduledBackupService
{
    public const string AutoKind = "auto";

    [GeneratedRegex(@"^pressing-auto-(?<date>\d{8})-\d{6}\.db$", RegexOptions.CultureInvariant)]
    private static partial Regex AutoBackupName();

    private readonly SettingsService _settings;
    private readonly PressingDatabaseOptions _databaseOptions;
    private readonly BackupScheduleOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ScheduledBackupService> _logger;

    public ScheduledBackupService(
        SettingsService settings,
        PressingDatabaseOptions databaseOptions,
        BackupScheduleOptions options,
        TimeProvider time,
        ILogger<ScheduledBackupService> logger)
    {
        _settings = settings;
        _databaseOptions = databaseOptions;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public async Task<ScheduledBackupResult> RunIfDueAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return new ScheduledBackupResult(ScheduledBackupStatus.Disabled);
        }

        DateTime now = _time.GetLocalNow().DateTime;
        if (now.Hour < _options.Hour)
        {
            return new ScheduledBackupResult(ScheduledBackupStatus.NotDue);
        }

        string folder = _databaseOptions.BackupPath;
        string todayPrefix = $"pressing-{AutoKind}-{now:yyyyMMdd}-";
        if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, todayPrefix + "*.db").Any())
        {
            return new ScheduledBackupResult(ScheduledBackupStatus.AlreadyDone);
        }

        BackupResultDto backup;
        try
        {
            backup = await _settings.CreateBackupAsync(CurrentUser.System, AutoKind, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Sauvegarde automatique impossible (nouvel essai au prochain passage).");
            // Un fichier à moitié écrit ne doit pas passer pour « la sauvegarde du jour ».
            string partial = Path.Combine(folder, $"pressing-{AutoKind}-{now:yyyyMMdd-HHmmss}.db");
            if (File.Exists(partial))
            {
                TryDelete(partial);
            }

            return new ScheduledBackupResult(ScheduledBackupStatus.Failed);
        }

        if (!backup.IntegrityOk)
        {
            // Une sauvegarde douteuse ne doit ni bloquer la journée ni entraîner la suppression des bonnes.
            _logger.LogError("La sauvegarde automatique {File} a échoué à la vérification d'intégrité : supprimée.", backup.FileName);
            TryDelete(backup.Path);
            return new ScheduledBackupResult(ScheduledBackupStatus.Failed);
        }

        bool copyFailed = !TryCopy(backup);
        int deleted = DeleteExpired(folder, now);
        if (HasCopyFolder(out string copyFolder))
        {
            deleted += DeleteExpired(copyFolder, now);
        }

        return new ScheduledBackupResult(ScheduledBackupStatus.Created, backup, copyFailed, deleted);
    }

    private bool HasCopyFolder(out string copyFolder)
    {
        copyFolder = string.IsNullOrWhiteSpace(_options.CopyPath) ? string.Empty : _options.CopyPath;
        if (copyFolder.Length == 0)
        {
            return false;
        }

        // Dossier identique au dossier principal : rien à copier ni à faire tourner deux fois.
        return !string.Equals(
            Path.GetFullPath(copyFolder).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(_databaseOptions.BackupPath).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Copie secondaire. Un échec (disque absent, clé retirée...) est journalisé et ne bloque jamais.</summary>
    private bool TryCopy(BackupResultDto backup)
    {
        if (!HasCopyFolder(out string copyFolder))
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(copyFolder);
            File.Copy(backup.Path, Path.Combine(copyFolder, backup.FileName), overwrite: true);
            _logger.LogInformation("Sauvegarde {File} copiée vers {Folder}.", backup.FileName, copyFolder);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copie de la sauvegarde {File} vers {Folder} impossible : la sauvegarde locale est conservée.",
                backup.FileName, copyFolder);
            return false;
        }
    }

    /// <summary>Supprime les sauvegardes automatiques dont la date (lue dans le nom) dépasse la durée de conservation.</summary>
    private int DeleteExpired(string folder, DateTime now)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        DateTime limit = now.Date.AddDays(-_options.RetentionDays);
        int deleted = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, "pressing-auto-*.db"))
            {
                Match match = AutoBackupName().Match(Path.GetFileName(file));
                if (!match.Success
                    || !DateTime.TryParseExact(match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime date)
                    || date >= limit)
                {
                    continue;
                }

                if (TryDelete(file))
                {
                    deleted++;
                    _logger.LogInformation("Sauvegarde automatique expirée supprimée : {File}.", Path.GetFileName(file));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rotation des sauvegardes impossible dans {Folder}.", folder);
        }

        return deleted;
    }

    private bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Suppression impossible : {Path}.", path);
            return false;
        }
    }
}
