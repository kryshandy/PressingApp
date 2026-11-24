using System.ComponentModel.DataAnnotations;

namespace Pressing.Core.Entities;

public class EmailMessage
{
    public int Id { get; set; }

    [MaxLength(160)]
    public string ToEmail { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? ToName { get; set; }

    [MaxLength(250)]
    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public string? TextBody { get; set; }

    [MaxLength(160)]
    public string? AttachmentFileName { get; set; }

    [MaxLength(120)]
    public string? AttachmentContentType { get; set; }

    public byte[]? AttachmentData { get; set; }

    [MaxLength(40)]
    public string Template { get; set; } = "generic";

    public EmailStatus Status { get; set; } = EmailStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? SentAt { get; set; }

    [MaxLength(1000)]
    public string? Error { get; set; }

    public int Attempts { get; set; }

    public int? RelatedOrderId { get; set; }

    public int? RelatedClientId { get; set; }
}

public class PasswordResetToken
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    [MaxLength(200)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    [MaxLength(60)]
    public string? RequestedIp { get; set; }
}

public class StaffNotification
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    public NotificationKind Kind { get; set; }

    [MaxLength(200)]
    public string? Link { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class AuditLog
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Actor { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(80)]
    public string EntityType { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? EntityId { get; set; }

    [MaxLength(2000)]
    public string? Details { get; set; }

    [MaxLength(60)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class AppSetting
{
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
