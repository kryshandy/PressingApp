using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pressing.Core.Data;
using Pressing.Core.Email;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using Pressing.Core.Reports;
using Pressing.Core.Security;
using ClientEntity = Pressing.Core.Entities.Client;
using Pressing.Core.Services;

namespace Pressing.Tests.Support;

/// <summary>
/// Base SQLite temporaire (un fichier unique dans le dossier temporaire) avec les services métier de Core câblés
/// comme dans l'API. Tout est supprimé à la libération.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly string _directory;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public TestDb()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pressing-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Path_ = Path.Combine(_directory, "unit.db");

        var dbOptions = new PressingDatabaseOptions
        {
            Path = Path_,
            BackupPath = Path.Combine(_directory, "Backups"),
            SeedDemoData = false
        };
        var emailOptions = new EmailOptions { Provider = "simulated", BackgroundEnabled = false };

        var services = new ServiceCollection();
        services.AddSingleton(dbOptions);
        services.AddSingleton(emailOptions);
        services.AddSingleton<IEmailTransport, SimulatedEmailTransport>();
        services.AddScoped(_ => new PressingDbContext(PressingDatabase.CreateOptions(Path_)));
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<AuditService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ClientService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<PromotionService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<EmailTemplateService>();
        services.AddScoped<EmailService>();
        services.AddScoped<OrderService>();
        services.AddScoped<DashboardService>();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        _scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }

    public string Path_ { get; }

    public PressingDbContext Db => _scope.ServiceProvider.GetRequiredService<PressingDbContext>();
    public OrderService Orders => _scope.ServiceProvider.GetRequiredService<OrderService>();
    public PaymentService Payments => _scope.ServiceProvider.GetRequiredService<PaymentService>();
    public SettingsService Settings => _scope.ServiceProvider.GetRequiredService<SettingsService>();
    public DashboardService Dashboard => _scope.ServiceProvider.GetRequiredService<DashboardService>();
    public ReceiptService Receipts => _scope.ServiceProvider.GetRequiredService<ReceiptService>();
    public PressingDatabaseOptions DbOptions => _scope.ServiceProvider.GetRequiredService<PressingDatabaseOptions>();
    public ClientService Clients => _scope.ServiceProvider.GetRequiredService<ClientService>();

    /// <summary>Service enregistré dans la portée de test (ex. EmailService).</summary>
    public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    public CurrentUser Cashier { get; } = new() { StaffId = 1, Name = "Test", Role = "Cashier" };

    public async Task<ClientEntity> AddClientAsync(string first = "Test", string last = "Client", string phone = "+225 07 00 11 22 33")
    {
        var client = new ClientEntity
        {
            FirstName = first,
            LastName = last,
            Phone = phone,
            PhoneNormalized = PhoneNormalizer.Normalize(phone),
            WhatsApp = phone,
            Email = "test@example.com"
        };
        Db.Clients.Add(client);
        await Db.SaveChangesAsync();
        return client;
    }

    /// <summary>Catégorie du catalogue seed par nom.</summary>
    public async Task<Category> CategoryAsync(string name) =>
        await Db.Categories.Include(c => c.Prices).FirstAsync(c => c.Name == name);

    public async Task SetSettingAsync(string key, string value)
    {
        var setting = await Db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting is null) Db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else setting.Value = value;
        await Db.SaveChangesAsync();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
