namespace Pressing.Core.Options;

public class PressingDatabaseOptions
{
    public string Path { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public bool SeedDemoData { get; set; }
    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = "Admin@2026!";
}

public class EmailOptions
{
    public string Provider { get; set; } = "auto";
    public string ResendApiKey { get; set; } = string.Empty;
    public string ResendFromEmail { get; set; } = "onboarding@resend.dev";
    public string ResendFromName { get; set; } = "Pressing Éclat";
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpAppPassword { get; set; } = string.Empty;
    public string ReplyTo { get; set; } = string.Empty;
    public string AppBaseUrl { get; set; } = "http://localhost:5181";
    public bool BackgroundEnabled { get; set; } = true;

    public bool HasResend => !string.IsNullOrWhiteSpace(ResendApiKey);
    public bool HasSmtp => !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(SmtpUser);

    public string EffectiveProvider
    {
        get
        {
            if (Provider.Equals("simulated", StringComparison.OrdinalIgnoreCase)) return "simulated";
            if (Provider.Equals("resend", StringComparison.OrdinalIgnoreCase)) return HasResend ? "resend" : "simulated";
            if (Provider.Equals("smtp", StringComparison.OrdinalIgnoreCase)) return HasSmtp ? "smtp" : "simulated";
            if (HasResend) return "resend";
            if (HasSmtp) return "smtp";
            return "simulated";
        }
    }

    public string FromEmail => HasResend ? ResendFromEmail : SmtpUser;
    public string FromName => string.IsNullOrWhiteSpace(ResendFromName) ? "Pressing Éclat" : ResendFromName;
}

public record ShopInfo(
    string Name,
    string LegalName,
    string Address,
    string City,
    string Phone,
    string WhatsApp,
    string Email,
    string Slogan,
    string Currency,
    string ReceiptFooter,
    string OpeningHours);
