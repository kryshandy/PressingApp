namespace Pressing.Core.Dtos;

/// <summary>Nombre d'articles attendus pour un service de l'atelier (lavage, repassage…).</summary>
public record WorkshopServiceCountDto(ServiceType Service, string Label, int Items);

/// <summary>Charge d'un jour : articles attendus (quantités comprises), répartition par service, taux de charge.</summary>
public record WorkshopLoadDayDto(
    DateTime Date,
    string Label,
    int Items,
    int Orders,
    int Capacity,
    double LoadRate,
    bool Overloaded,
    bool IsToday,
    IReadOnlyList<WorkshopServiceCountDto> ByService);

/// <summary>
/// Charge prévisionnelle de l'atelier : un élément par jour (aujourd'hui compris), les articles en retard
/// (date prévue dépassée, pas encore prêts) et la capacité journalière du réglage <c>Workshop.DailyCapacity</c>.
/// </summary>
public record WorkshopLoadDto(
    DateTime From,
    int DaysCount,
    int DailyCapacity,
    int TotalItems,
    int OverloadedDays,
    int OverdueItems,
    int OverdueOrders,
    IReadOnlyList<WorkshopLoadDayDto> Days,
    IReadOnlyList<WorkshopServiceCountDto> TotalsByService);

/// <summary>Ligne du dépôt en cours de saisie : catégorie, service et quantité.</summary>
public record WorkshopSuggestLineRequest(int CategoryId, ServiceType ServiceType, int Quantity);

/// <summary>Demande de date de retrait conseillée pour un dépôt (<see cref="IsExpress"/> : délai express du catalogue).</summary>
public record WorkshopSuggestDueDateRequest(IReadOnlyList<WorkshopSuggestLineRequest> Items, bool IsExpress);

/// <summary>
/// Date conseillée. <see cref="EarliestDate"/> = première date permise par le délai du catalogue ;
/// <see cref="SuggestedDate"/> = première date de ce jour ou après où la charge reste sous la capacité.
/// <see cref="CapacityRespected"/> est faux si aucun jour libre n'a été trouvé (la date conseillée est alors la plus tôt).
/// </summary>
public record WorkshopDueDateSuggestionDto(
    DateTime SuggestedDate,
    DateTime EarliestDate,
    int LeadDays,
    bool IsExpress,
    int NewItems,
    int SkippedDays,
    bool CapacityRespected,
    string Explanation);

/// <summary>Capacité journalière de l'atelier (articles par jour) : corps de la modification et réponse.</summary>
public record WorkshopCapacityDto(int DailyCapacity);
