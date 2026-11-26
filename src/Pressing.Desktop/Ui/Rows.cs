using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;

namespace Pressing.Desktop.Ui;

/// <summary>Ligne d'une liste de commandes.</summary>
public sealed class OrderRow : IPillRow, ITintRow
{
    public OrderRow(OrderSummaryDto o)
    {
        Source = o;
        Ticket = o.TicketNumber;
        Client = o.ClientName;
        Created = Fmt.Date(o.CreatedAt);
        Due = Fmt.Date(o.DueDate);
        Items = o.ItemCount + (o.ItemCount > 1 ? " articles" : " article") +
                (o.ReadyCount > 0 ? $" · {o.ReadyCount} prêt(s)" : string.Empty);
        Total = Fmt.Money(o.Total);
        Balance = o.Balance > 0 ? Fmt.Money(o.Balance) : "Soldé";
    }

    public OrderSummaryDto Source { get; }
    public int Id => Source.Id;
    public string Ticket { get; }
    public string Client { get; }
    public string Created { get; }
    public string Due { get; }
    public string Items { get; }
    public string Total { get; }
    public string Balance { get; }

    private bool Overdue => Source.DueDate.Date < DateTime.Today &&
                            Source.Status is not (OrderStatus.Completed or OrderStatus.Cancelled);

    public (string Text, Color Color)? GetPill(string columnName) => columnName switch
    {
        "status" => (Source.StatusLabel, Theme.StatusColor(Source.Status)),
        "express" => Source.IsExpress ? ("EXPRESS", Theme.Amber) : null,
        _ => null
    };

    public Color? GetTint(string columnName) => columnName switch
    {
        "due" when Overdue => Theme.Red,
        "balance" when Source.Status == OrderStatus.Cancelled => Theme.TextFaint,
        "balance" when Source.Balance > 0 => Theme.Amber,
        "balance" => Theme.Green,
        "ticket" => Theme.Accent,
        _ => null
    };
}

/// <summary>Ligne d'une liste de paiements.</summary>
public sealed class PaymentRow : ITintRow
{
    public PaymentRow(PaymentDto p)
    {
        Source = p;
        Date = Fmt.DateTime(p.PaidAt);
        Ticket = p.TicketNumber;
        Type = p.TypeLabel;
        Method = p.MethodLabel;
        Reference = p.Reference ?? string.Empty;
        Staff = p.StaffName ?? string.Empty;
        Amount = (p.Type == PaymentType.Refund ? "− " : string.Empty) + Fmt.Money(p.Amount);
    }

    public PaymentDto Source { get; }
    public string Date { get; }
    public string Ticket { get; }
    public string Type { get; }
    public string Method { get; }
    public string Reference { get; }
    public string Staff { get; }
    public string Amount { get; }

    public Color? GetTint(string columnName) => columnName switch
    {
        "amount" => Source.Type == PaymentType.Refund ? Theme.Red : Theme.Green,
        "ticket" => Theme.Accent,
        "type" when Source.Type == PaymentType.Refund => Theme.Red,
        _ => null
    };
}

/// <summary>Ligne d'une liste de clients.</summary>
public sealed class ClientRow : IPillRow, ITintRow
{
    public ClientRow(ClientDto c)
    {
        Source = c;
        Name = c.FullName;
        Phone = c.Phone;
        Email = c.Email ?? string.Empty;
        Orders = c.OrderCount.ToString();
        Points = c.LoyaltyPoints.ToString();
        Balance = c.Balance > 0 ? Fmt.Money(c.Balance) : "—";
        Since = Fmt.Date(c.CreatedAt);
    }

    public ClientDto Source { get; }
    public int Id => Source.Id;
    public string Name { get; }
    public string Phone { get; }
    public string Email { get; }
    public string Orders { get; }
    public string Points { get; }
    public string Balance { get; }
    public string Since { get; }

    public (string Text, Color Color)? GetPill(string columnName) => columnName switch
    {
        "web" => Source.WebAccessEnabled ? ("Accès web", Theme.Blue) : null,
        "state" when !Source.IsActive => ("Inactif", Theme.Slate),
        "status" when !Source.IsActive => ("Inactif", Theme.Slate),
        "status" when Source.WebAccessEnabled => ("Accès web", Theme.Blue),
        "status" => ("Actif", Theme.Green),
        _ => null
    };

    public Color? GetTint(string columnName) => columnName switch
    {
        "balance" when Source.Balance > 0 => Theme.Amber,
        "name" when !Source.IsActive => Theme.TextFaint,
        _ => null
    };
}

/// <summary>Ligne d'article d'une commande (détail).</summary>
public sealed class ItemRow : IPillRow
{
    public ItemRow(OrderItemDto i)
    {
        Source = i;
        Article = i.CategoryName + (string.IsNullOrWhiteSpace(i.Description) ? string.Empty : " — " + i.Description);
        Service = i.ServiceLabel + (i.IsExpress ? " (express)" : string.Empty);
        Quantity = i.Quantity.ToString();
        UnitPrice = Fmt.Money(i.UnitPrice + (i.IsExpress ? i.ExpressSupplement : 0));
        Total = Fmt.Money(i.LineTotal);
        Due = i.DueDate.HasValue ? Fmt.Date(i.DueDate.Value) : "—";
        PickedUp = i.PickedUpQuantity > 0 ? $"{i.PickedUpQuantity} / {i.Quantity}" : "—";
    }

    public OrderItemDto Source { get; }
    public int Id => Source.Id;
    public string Article { get; }
    public string Service { get; }
    public string Quantity { get; }
    public string UnitPrice { get; }
    public string Total { get; }
    public string Due { get; }
    public string PickedUp { get; }

    public (string Text, Color Color)? GetPill(string columnName) => columnName == "status"
        ? (Source.StatusLabel, Theme.StatusColor(Source.Status))
        : null;
}
