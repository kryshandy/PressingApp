namespace Pressing.Core.Dtos;

/// <summary>Ce que la page publique /avis affiche avant la réponse du client.</summary>
public record FeedbackFormDto(string TicketNumber, string FirstName, string ShopName, bool AlreadyAnswered, int? Note);

public record SubmitFeedbackRequest(string Token, int Note, string? Comment);

public record FeedbackDto(
    int Id,
    int OrderId,
    string TicketNumber,
    int ClientId,
    string ClientName,
    int Note,
    string? Comment,
    DateTime CreatedAt);

/// <summary>Liste paginée des avis ; <paramref name="Average"/> et <paramref name="Count"/> portent sur la période demandée.</summary>
public record FeedbackPageDto(
    IReadOnlyList<FeedbackDto> Items,
    int Total,
    int Page,
    int PageSize,
    double? Average,
    int Count)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}
