using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

/// <summary>
/// Charge prévisionnelle de l'atelier, calculée à partir des commandes existantes (aucune table dédiée).
/// <list type="bullet">
/// <item>Un article « attendu » un jour J : ligne non retirée ni annulée, d'une commande non annulée dont la date
/// prévue (<c>Order.DueDate</c>) tombe ce jour ; on compte les pièces restantes (quantité − déjà retirées).</item>
/// <item>Un article « en retard » : date prévue dépassée et statut encore « Déposé » ou « En traitement ».</item>
/// <item>Capacité : réglage <c>Workshop.DailyCapacity</c> (défaut 60) ; un jour est en surcharge quand
/// ses articles dépassent strictement cette capacité.</item>
/// </list>
/// </summary>
public class WorkshopLoadService
{
    public const int DefaultDailyCapacity = 60;
    public const int DefaultDays = 14;
    public const int MaxDays = 60;

    /// <summary>Nombre de jours examinés au-delà du délai pour trouver une date libre.</summary>
    public const int SearchHorizonDays = 60;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly ServiceType[] Services =
    [
        ServiceType.Washing, ServiceType.WashAndIron, ServiceType.Ironing, ServiceType.DryCleaning, ServiceType.StainRemoval
    ];

    private readonly PressingDbContext _db;
    private readonly SettingsService _settings;

    public WorkshopLoadService(PressingDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    private sealed record LoadLine(int OrderId, DateTime Due, ServiceType Service, int Pieces, ItemStatus Status);

    /// <summary>Capacité journalière en vigueur (valeur invalide ou absente → 60).</summary>
    public async Task<int> GetCapacityAsync(CancellationToken ct = default)
    {
        int capacity = await _settings.GetIntAsync(SettingsKeys.WorkshopDailyCapacity, DefaultDailyCapacity, ct);
        return capacity is >= 1 and <= 10_000 ? capacity : DefaultDailyCapacity;
    }

    /// <summary>Change la capacité (1 à 10 000 articles/jour), avec la validation et le journal des paramètres.</summary>
    public async Task<int> SetCapacityAsync(int dailyCapacity, CurrentUser user, CancellationToken ct = default)
    {
        await _settings.UpdateAsync(new UpdateSettingsRequest(new Dictionary<string, string>
        {
            [SettingsKeys.WorkshopDailyCapacity] = dailyCapacity.ToString(CultureInfo.InvariantCulture)
        }), user, ct);
        return await GetCapacityAsync(ct);
    }

    /// <summary>Charge des <paramref name="days"/> prochains jours (aujourd'hui compris, 1 à 60).</summary>
    public async Task<WorkshopLoadDto> GetLoadAsync(int days = DefaultDays, DateTime? today = null, CancellationToken ct = default)
    {
        if (days is < 1 or > MaxDays)
        {
            throw new ArgumentException($"Le nombre de jours doit être compris entre 1 et {MaxDays}.");
        }

        DateTime start = (today ?? DateTime.Now).Date;
        DateTime end = start.AddDays(days);
        int capacity = await GetCapacityAsync(ct);
        List<LoadLine> lines = await LoadLinesAsync(end, ct);

        var daily = new List<WorkshopLoadDayDto>(days);
        for (int offset = 0; offset < days; offset++)
        {
            DateTime day = start.AddDays(offset);
            List<LoadLine> ofDay = lines.Where(l => l.Due == day).ToList();
            int items = ofDay.Sum(l => l.Pieces);
            daily.Add(new WorkshopLoadDayDto(
                day,
                DayLabel(day),
                items,
                ofDay.Select(l => l.OrderId).Distinct().Count(),
                capacity,
                Math.Round(items / (double)capacity, 4),
                items > capacity,
                offset == 0,
                ByService(ofDay)));
        }

        List<LoadLine> overdue = lines
            .Where(l => l.Due < start && l.Status is ItemStatus.Deposited or ItemStatus.InProgress)
            .ToList();
        List<LoadLine> inRange = lines.Where(l => l.Due >= start).ToList();

        return new WorkshopLoadDto(
            start,
            days,
            capacity,
            daily.Sum(d => d.Items),
            daily.Count(d => d.Overloaded),
            overdue.Sum(l => l.Pieces),
            overdue.Select(l => l.OrderId).Distinct().Count(),
            daily,
            ByService(inRange));
    }

    /// <summary>
    /// Propose la première date de retrait respectant le délai du catalogue du service le plus long ET où la charge
    /// reste sous la capacité. En express : délai express du réglage, capacité ignorée. Un jour sans aucun article est
    /// toujours accepté (un dépôt plus gros que la capacité ne repousse pas indéfiniment).
    /// </summary>
    public async Task<WorkshopDueDateSuggestionDto> SuggestDueDateAsync(
        IReadOnlyList<WorkshopSuggestLineRequest> items, bool express, DateTime? today = null, CancellationToken ct = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new InvalidOperationException("Ajoutez au moins un article pour obtenir une date conseillée.");
        }

        DateTime start = (today ?? DateTime.Now).Date;
        int defaultDays = await _settings.GetIntAsync(SettingsKeys.DefaultProcessingDays, 3, ct);
        int expressDays = await _settings.GetIntAsync(SettingsKeys.ExpressProcessingDays, 1, ct);

        int lead = 0;
        int newItems = 0;
        foreach (WorkshopSuggestLineRequest line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidOperationException("La quantité de chaque article doit être supérieure à zéro.");
            }

            CategoryPrice price = await _db.CategoryPrices.AsNoTracking()
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.CategoryId == line.CategoryId && p.ServiceType == line.ServiceType && p.IsActive, ct)
                ?? throw new InvalidOperationException(
                    $"Aucun tarif actif pour la catégorie {line.CategoryId} ({line.ServiceType.Label()}).");

            lead = Math.Max(lead, price.EstimatedDays > 0 ? price.EstimatedDays : defaultDays);
            newItems += line.Quantity;
        }

        if (express)
        {
            lead = expressDays;
        }

        DateTime earliest = start.AddDays(lead);
        if (express)
        {
            return new WorkshopDueDateSuggestionDto(
                earliest, earliest, lead, true, newItems, 0, true,
                $"{LongDay(earliest)} : délai express de {Plural(lead, "jour")}, charge de l'atelier ignorée.");
        }

        int capacity = await GetCapacityAsync(ct);
        DateTime last = earliest.AddDays(SearchHorizonDays);
        List<LoadLine> lines = await LoadLinesAsync(last.AddDays(1), ct);
        Dictionary<DateTime, int> load = lines
            .Where(l => l.Due >= earliest)
            .GroupBy(l => l.Due)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Pieces));

        var skipped = new List<DateTime>();
        for (DateTime day = earliest; day <= last; day = day.AddDays(1))
        {
            int current = load.GetValueOrDefault(day);
            if (current == 0 || current + newItems <= capacity)
            {
                return new WorkshopDueDateSuggestionDto(
                    day, earliest, lead, false, newItems, skipped.Count, true,
                    Explain(day, lead, skipped, capacity, current + newItems));
            }

            skipped.Add(day);
        }

        return new WorkshopDueDateSuggestionDto(
            earliest, earliest, lead, false, newItems, 0, false,
            $"{LongDay(earliest)} : délai de {Plural(lead, "jour")}, mais aucun jour sous la capacité "
            + $"de {capacity} articles dans les {SearchHorizonDays} jours suivants.");
    }

    private static string Explain(DateTime day, int lead, List<DateTime> skipped, int capacity, int loadAfter)
    {
        string text = $"{LongDay(day)} : délai de {Plural(lead, "jour")}";
        if (skipped.Count == 0)
        {
            return text + $", charge prévue {loadAfter}/{capacity} articles.";
        }

        if (skipped.Count > 5)
        {
            return text + $", {skipped.Count} jours complets ({LongDay(skipped[0])} au {LongDay(skipped[^1])}).";
        }

        List<string> names = skipped.Select(d => d.ToString("dddd", Fr)).ToList();
        string list = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " et " + names[^1];
        return text + ", " + list + (names.Count == 1 ? " complet." : " complets.");
    }

    private async Task<List<LoadLine>> LoadLinesAsync(DateTime beforeDate, CancellationToken ct)
    {
        var raw = await _db.OrderItems.AsNoTracking()
            .Where(i => (i.Status == ItemStatus.Deposited || i.Status == ItemStatus.InProgress || i.Status == ItemStatus.Ready)
                        && i.Order.Status != OrderStatus.Cancelled
                        && i.Order.DueDate < beforeDate)
            .Select(i => new { i.OrderId, i.Order.DueDate, i.ServiceType, i.Quantity, i.PickedUpQuantity, i.Status })
            .ToListAsync(ct);

        return raw
            .Select(i => new LoadLine(i.OrderId, i.DueDate.Date, i.ServiceType, Math.Max(0, i.Quantity - i.PickedUpQuantity), i.Status))
            .Where(l => l.Pieces > 0)
            .ToList();
    }

    private static List<WorkshopServiceCountDto> ByService(IEnumerable<LoadLine> lines)
    {
        List<LoadLine> list = lines.ToList();
        return Services
            .Select(s => new WorkshopServiceCountDto(s, s.Label(), list.Where(l => l.Service == s).Sum(l => l.Pieces)))
            .ToList();
    }

    private static string DayLabel(DateTime day) => day.ToString("ddd d", Fr).Replace(".", string.Empty);

    private static string LongDay(DateTime day) => day.ToString("dddd d MMM", Fr);

    private static string Plural(int count, string word) => count > 1 ? $"{count} {word}s" : $"{count} {word}";
}
