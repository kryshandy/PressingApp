using Pressing.Core.Options;
using Pressing.Core.Services;

namespace Pressing.Api.Infrastructure;

/// <summary>
/// Tâche de fond des sauvegardes quotidiennes : toutes les 5 minutes, demande à <see cref="ScheduledBackupService"/>
/// si la sauvegarde du jour est due (toute la logique, testée, est dans Core).
/// </summary>
public sealed class BackupBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupBackgroundService> _logger;

    public BackupBackgroundService(IServiceScopeFactory scopeFactory, ILogger<BackupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisse l'API finir son démarrage (migrations comprises) avant la première sauvegarde.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ScheduledBackupService>();
                ScheduledBackupResult result = await service.RunIfDueAsync(stoppingToken);
                if (result.Status == ScheduledBackupStatus.Created)
                {
                    _logger.LogInformation(
                        "Sauvegarde automatique {File} créée ({Deleted} ancienne(s) supprimée(s){Copy}).",
                        result.Backup!.FileName, result.Deleted, result.CopyFailed ? ", copie secondaire en échec" : string.Empty);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur pendant la sauvegarde automatique.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

public static class BackupServiceCollectionExtensions
{
    /// <summary>
    /// Sauvegardes automatiques quotidiennes. Variables : BACKUP_SCHEDULE_ENABLED, BACKUP_HOUR,
    /// BACKUP_RETENTION_DAYS, BACKUP_COPY_PATH (voir .env.example).
    /// </summary>
    public static IServiceCollection AddScheduledBackups(this IServiceCollection services)
    {
        BackupScheduleOptions options = BackupScheduleOptions.FromEnvironment();
        services.AddSingleton(options);
        services.AddScoped<ScheduledBackupService>();
        if (options.Enabled)
        {
            services.AddHostedService<BackupBackgroundService>();
        }

        return services;
    }
}
