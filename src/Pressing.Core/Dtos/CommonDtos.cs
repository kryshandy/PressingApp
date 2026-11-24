namespace Pressing.Core.Dtos;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public record HealthInfoDto(
    string Status,
    string Version,
    string ShopName,
    DateTime ServerTime,
    bool EmailConfigured,
    string EmailProvider);

public record RevenuePointDto(string Label, DateTime Date, decimal Revenue, int Orders);

public record StatusCountDto(string Status, string Label, int Count, string Color);

public record CategoryStatDto(string Category, int Quantity, decimal Revenue);

public record PaymentMethodStatDto(string Method, string Label, decimal Amount, int Count);

public record StaffStatDto(string StaffName, int Orders, decimal Payments);

public record NotificationDto(
    int Id,
    string Title,
    string Message,
    NotificationKind Kind,
    string KindLabel,
    string? Link,
    bool IsRead,
    DateTime CreatedAt);

public record AuditLogDto(
    int Id,
    string Actor,
    string Action,
    string EntityType,
    string? EntityId,
    string? Details,
    string? IpAddress,
    DateTime CreatedAt);

public record EmailMessageDto(
    int Id,
    string ToEmail,
    string? ToName,
    string Subject,
    string Template,
    EmailStatus Status,
    string StatusLabel,
    DateTime CreatedAt,
    DateTime? SentAt,
    string? Error,
    int Attempts,
    int? RelatedOrderId,
    int? RelatedClientId);

public record BackupResultDto(
    string FileName,
    string Path,
    long Size,
    DateTime CreatedAt,
    bool IntegrityOk);

public record EmailConfigStatusDto(
    bool ResendConfigured,
    bool SmtpConfigured,
    string Provider,
    string FromAddress,
    string FromName,
    string? Warning);

public record SettingsDto(
    Dictionary<string, string> Values,
    EmailConfigStatusDto Email);

public record UpdateSettingsRequest(Dictionary<string, string> Values);

public record TestEmailRequest(string To);

public record SendEmailRequest(string To, string Subject, string Body);
