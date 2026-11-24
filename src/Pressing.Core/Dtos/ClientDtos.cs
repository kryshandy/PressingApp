namespace Pressing.Core.Dtos;

public record ClientDto(
    int Id,
    string FirstName,
    string LastName,
    string FullName,
    string Phone,
    string WhatsApp,
    string? Email,
    string? Address,
    string? Notes,
    bool IsActive,
    bool WebAccessEnabled,
    int LoyaltyPoints,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int OrderCount,
    decimal Balance,
    decimal TotalSpent,
    DateTime? LastOrderAt);

public record CreateClientRequest(
    string FirstName,
    string LastName,
    string Phone,
    string WhatsApp,
    string? Email,
    string? Address,
    string? Notes,
    string? Password,
    bool SendWelcomeEmail);

public record UpdateClientRequest(
    string FirstName,
    string LastName,
    string Phone,
    string WhatsApp,
    string? Email,
    string? Address,
    string? Notes,
    bool IsActive);

public record SetClientPasswordRequest(
    string? Password,
    bool EnableWebAccess,
    bool SendWelcomeEmail);

public record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string Phone,
    string WhatsApp,
    string? Email,
    string? Address);

public record PortalDashboardDto(
    ClientDto Client,
    IReadOnlyList<OrderSummaryDto> ActiveOrders,
    IReadOnlyList<OrderSummaryDto> RecentOrders,
    IReadOnlyList<LoyaltyTransactionDto> LoyaltyTransactions,
    int ReadyItems,
    decimal Balance);

public record LoyaltyTransactionDto(
    int Id,
    int Points,
    LoyaltyTransactionType Type,
    string TypeLabel,
    string Note,
    DateTime CreatedAt,
    int? OrderId,
    string? TicketNumber);

public record ClientDetailDto(
    ClientDto Client,
    IReadOnlyList<OrderSummaryDto> Orders,
    IReadOnlyList<PaymentDto> Payments,
    IReadOnlyList<LoyaltyTransactionDto> LoyaltyTransactions);
