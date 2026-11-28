using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

/// <summary>Une réponse existe déjà pour cette commande (HTTP 409).</summary>
public sealed class FeedbackAlreadyAnsweredException : Exception
{
    public FeedbackAlreadyAnsweredException() : base("Vous avez déjà donné votre avis sur cette commande. Merci !")
    {
    }
}

/// <summary>Enquête de satisfaction : page publique (jeton signé « feedback ») et consultation par le personnel.</summary>
public class FeedbackService
{
    public const int MaxCommentLength = 1000;
    public const string InvalidLink = "Ce lien d'avis est invalide ou a expiré.";

    private readonly PressingDbContext _db;
    private readonly TokenService _tokens;
    private readonly SettingsService _settings;
    private readonly TimeProvider _time;

    public FeedbackService(PressingDbContext db, TokenService tokens, SettingsService settings, TimeProvider time)
    {
        _db = db;
        _tokens = tokens;
        _settings = settings;
        _time = time;
    }

    /// <summary>Contenu de la page /avis. Lève InvalidOperationException (400) si le lien est invalide ou expiré.</summary>
    public async Task<FeedbackFormDto> GetFormAsync(string? token, CancellationToken ct = default)
    {
        Order order = await FindOrderAsync(token, ct);
        Feedback? existing = await _db.Feedbacks.AsNoTracking().FirstOrDefaultAsync(f => f.OrderId == order.Id, ct);
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        return new FeedbackFormDto(order.TicketNumber, order.Client.FirstName, shop.Name, existing is not null, existing?.Note);
    }

    /// <summary>Enregistre l'avis : note 1 à 5 obligatoire, commentaire facultatif (1000 caractères), une seule réponse par commande.</summary>
    public async Task<MessageResponse> SubmitAsync(string? token, int note, string? comment, CancellationToken ct = default)
    {
        Order order = await FindOrderAsync(token, ct);

        if (note is < 1 or > 5)
        {
            throw new InvalidOperationException("La note doit être comprise entre 1 et 5.");
        }

        string? text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (text is not null && text.Length > MaxCommentLength)
        {
            throw new InvalidOperationException($"Le commentaire ne doit pas dépasser {MaxCommentLength} caractères.");
        }

        if (await _db.Feedbacks.AnyAsync(f => f.OrderId == order.Id, ct))
        {
            throw new FeedbackAlreadyAnsweredException();
        }

        _db.Feedbacks.Add(new Feedback
        {
            OrderId = order.Id,
            ClientId = order.ClientId,
            Note = note,
            Comment = text,
            CreatedAt = _time.GetLocalNow().DateTime
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Deux envois simultanés : l'index unique sur OrderId départage.
            _db.ChangeTracker.Clear();
            if (await _db.Feedbacks.AsNoTracking().AnyAsync(f => f.OrderId == order.Id, ct))
            {
                throw new FeedbackAlreadyAnsweredException();
            }

            throw;
        }

        return new MessageResponse("Merci pour votre avis, il nous aide à nous améliorer !");
    }

    /// <summary>Avis les plus récents d'abord ; moyenne et nombre sur les <paramref name="days"/> derniers jours (tout l'historique si null).</summary>
    public async Task<FeedbackPageDto> GetPageAsync(int page = 1, int pageSize = 25, int? days = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        IQueryable<Feedback> query = _db.Feedbacks.AsNoTracking();
        if (days is > 0)
        {
            DateTime since = _time.GetLocalNow().DateTime.AddDays(-days.Value);
            query = query.Where(f => f.CreatedAt >= since);
        }

        int total = await query.CountAsync(ct);
        double? average = total == 0 ? null : Math.Round(await query.AverageAsync(f => (double)f.Note, ct), 2);

        var rows = await query
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.Id, f.OrderId, f.Order.TicketNumber, f.ClientId,
                f.Client.FirstName, f.Client.LastName, f.Note, f.Comment, f.CreatedAt
            })
            .ToListAsync(ct);

        List<FeedbackDto> items = rows
            .Select(r => new FeedbackDto(r.Id, r.OrderId, r.TicketNumber, r.ClientId,
                $"{r.FirstName} {r.LastName}".Trim(), r.Note, r.Comment, r.CreatedAt))
            .ToList();

        return new FeedbackPageDto(items, total, page, pageSize, average, total);
    }

    /// <summary>Contrôle le jeton (type « feedback » uniquement) et retrouve la commande terminée du client désigné.</summary>
    private async Task<Order> FindOrderAsync(string? token, CancellationToken ct)
    {
        var principal = string.IsNullOrWhiteSpace(token) ? null : _tokens.Validate(token);
        if (principal?.FindFirst("token_type")?.Value != TokenService.FeedbackType ||
            !int.TryParse(principal.FindFirst("order_id")?.Value, out int orderId) ||
            !int.TryParse(principal.FindFirst("client_id")?.Value, out int clientId))
        {
            throw new InvalidOperationException(InvalidLink);
        }

        Order? order = await _db.Orders.Include(o => o.Client)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.ClientId == clientId && o.Status == OrderStatus.Completed, ct);
        return order ?? throw new InvalidOperationException(InvalidLink);
    }
}
