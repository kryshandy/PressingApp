namespace Pressing.Core.Dtos;

public record DashboardDto(
    int OrdersToday,
    decimal RevenueToday,
    decimal RevenueMonth,
    int ActiveOrders,
    int ReadyOrders,
    int OverdueOrders,
    decimal OutstandingTotal,
    int NewClientsMonth,
    int PendingEmails,
    IReadOnlyList<OrderSummaryDto> RecentOrders,
    IReadOnlyList<OrderSummaryDto> ReadyOrdersList,
    IReadOnlyList<RevenuePointDto> Last7Days,
    IReadOnlyList<StatusCountDto> StatusBreakdown,
    IReadOnlyList<CategoryStatDto> TopCategories,
    IReadOnlyList<NotificationDto> Notifications);

public record ReportDto(
    DateTime From,
    DateTime To,
    decimal Revenue,
    decimal Refunds,
    decimal NetRevenue,
    decimal Outstanding,
    int Orders,
    int CompletedOrders,
    int NewClients,
    int ItemsProcessed,
    IReadOnlyList<RevenuePointDto> Daily,
    IReadOnlyList<CategoryStatDto> TopCategories,
    IReadOnlyList<PaymentMethodStatDto> PaymentMethods,
    IReadOnlyList<StaffStatDto> Staff,
    IReadOnlyList<OrderSummaryDto> RecentOrders);
