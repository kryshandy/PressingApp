namespace Pressing.Core.Options;

/// <summary>
/// Sauvegardes automatiques quotidiennes (variables <c>BACKUP_*</c> du <c>.env</c>).
/// Les valeurs invalides retombent sur le défaut : une faute de frappe ne doit jamais empêcher l'API de démarrer.
/// </summary>
public sealed class BackupScheduleOptions
{
    public const int DefaultHour = 22;
    public const int DefaultRetentionDays = 30;

    /// <summary><c>BACKUP_SCHEDULE_ENABLED</c> (défaut true).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary><c>BACKUP_HOUR</c> : heure locale (0 à 23) à partir de laquelle la sauvegarde du jour est faite (défaut 22).</summary>
    public int Hour { get; set; } = DefaultHour;

    /// <summary><c>BACKUP_RETENTION_DAYS</c> : les sauvegardes automatiques plus anciennes sont supprimées (défaut 30).</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary><c>BACKUP_COPY_PATH</c> : dossier de copie secondaire (autre disque, clé USB, dossier synchronisé). Vide = aucune copie.</summary>
    public string CopyPath { get; set; } = string.Empty;

    public static BackupScheduleOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static BackupScheduleOptions From(Func<string, string?> read)
    {
        var options = new BackupScheduleOptions();

        string? enabled = read("BACKUP_SCHEDULE_ENABLED");
        if (!string.IsNullOrWhiteSpace(enabled))
        {
            options.Enabled = !(enabled.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)
                || enabled.Trim() == "0"
                || enabled.Trim().Equals("no", StringComparison.OrdinalIgnoreCase)
                || enabled.Trim().Equals("non", StringComparison.OrdinalIgnoreCase));
        }

        if (int.TryParse(read("BACKUP_HOUR"), out int hour) && hour is >= 0 and <= 23)
        {
            options.Hour = hour;
        }

        if (int.TryParse(read("BACKUP_RETENTION_DAYS"), out int days) && days >= 1)
        {
            options.RetentionDays = days;
        }

        options.CopyPath = (read("BACKUP_COPY_PATH") ?? string.Empty).Trim();
        return options;
    }
}
