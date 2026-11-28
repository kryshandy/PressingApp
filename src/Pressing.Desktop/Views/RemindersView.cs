using Pressing.Client;
using Pressing.Core.Dtos;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>
/// Journal des rappels WhatsApp / e-mail (automatiques et manuels) et relance manuelle d'un client consentant
/// (Managers et Administrateurs).
/// </summary>
public sealed class RemindersView : BaseView
{
    private enum Period
    {
        All,
        Today,
        Week,
        Month
    }

    private sealed class ReminderRow : IPillRow, ITintRow
    {
        public ReminderRow(ReminderLogDto r)
        {
            Source = r;
            Date = Fmt.DateTime(r.SentAt);
            Ticket = r.TicketNumber;
            Client = r.ClientName;
            Type = r.TypeLabel;
            Channel = r.ChannelLabel;
        }

        public ReminderLogDto Source { get; }
        public string Date { get; }
        public string Ticket { get; }
        public string Client { get; }
        public string Type { get; }
        public string Channel { get; }

        public (string Text, Color Color)? GetPill(string columnName) => columnName == "result"
            ? Source.Success ? ("Envoyé", Theme.Green) : ("Échec", Theme.Red)
            : null;

        public Color? GetTint(string columnName) => columnName == "ticket" ? Theme.Accent : null;
    }

    private const int PageSize = 25;

    private readonly StatCard _total = new("Rappels journalisés", "—", Theme.Blue);
    private readonly StatCard _failed = new("Échecs (page affichée)", "—", Theme.Red);
    private readonly StatCard _last = new("Dernier rappel", "—", Theme.Green);
    private readonly SelectBox _type = new();
    private readonly SelectBox _channel = new();
    private readonly SelectBox _period = new();
    private readonly GridCard _card = new("Aucun rappel ne correspond à ces critères.");
    private readonly PagerBar _pager = new();
    private readonly FlatButton _resend = new()
    {
        Text = "Relancer le client", Kind = ButtonKind.Secondary, IconGlyph = Icons.Mail, Enabled = false
    };
    private int _page = 1;
    private int _version;

    public RemindersView(IAppHost host) : base(host)
    {
        PageHeader header = new("Rappels", "Journal des rappels WhatsApp et e-mail, et relance manuelle des clients consentants");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));

        _type.SetChoices(new[]
        {
            new Choice<string?>(null, "Tous les types"),
            new Choice<string?>("ready", "Commande prête"),
            new Choice<string?>("uncollected-j3", "Non récupérée (3 jours)"),
            new Choice<string?>("uncollected-j7", "Non récupérée (7 jours)"),
            new Choice<string?>("uncollected-j14", "Dernier rappel (14 jours)"),
            new Choice<string?>("unpaid-balance", "Solde impayé"),
            new Choice<string?>("due-tomorrow", "Échéance demain"),
            new Choice<string?>("manual", "Relance manuelle")
        }, null);
        _channel.SetChoices(new[]
        {
            new Choice<string?>(null, "Tous les canaux"),
            new Choice<string?>("whatsapp", "WhatsApp"),
            new Choice<string?>("email", "E-mail")
        }, null);
        _period.SetChoices(new[]
        {
            new Choice<Period>(Period.All, "Toute période"),
            new Choice<Period>(Period.Today, "Aujourd'hui"),
            new Choice<Period>(Period.Week, "7 derniers jours"),
            new Choice<Period>(Period.Month, "30 derniers jours")
        }, Period.All);

        // Barre de filtres sur une ligne : type (6 colonnes), canal et période (3 + 3).
        GridRow filters = new GridRow().Add(_type, 6).Add(_channel, 3).Add(_period, 3);

        ThemedGrid grid = _card.Grid;
        grid.AddText("date", "ENVOYÉ LE", nameof(ReminderRow.Date), fixedWidth: 148);
        grid.AddText("ticket", "TICKET", nameof(ReminderRow.Ticket), fixedWidth: 164);
        grid.AddText("client", "CLIENT", nameof(ReminderRow.Client), 100, minWidth: 150);
        grid.AddText("type", "TYPE", nameof(ReminderRow.Type), 80, minWidth: 170);
        grid.AddText("channel", "CANAL", nameof(ReminderRow.Channel), fixedWidth: 148);
        grid.AddPill("result", "RÉSULTAT", fixedWidth: 120);
        grid.SetOptional("channel", "type");
        grid.SelectionChanged += (_, _) => _resend.Enabled = grid.SelectedItem<ReminderRow>() is not null;

        TitledCard list = new("Historique des envois", _card,
            "Sélectionnez une ligne puis « Relancer le client » : l'envoi exige le consentement du client", flushContent: true);
        list.AddAction(_resend);

        VStack page = new VStack(16)
            .Add(new GridRow().Add(_total, 4).Add(_failed, 4).Add(_last, 4), StackSize.Auto)
            .Add(list, StackSize.Fill)
            .Add(_pager);
        SetPage(header, page, toolbar: filters);

        _pager.PageRequested += async (_, p) =>
        {
            _page = p;
            await RunAsync(QueryAsync);
        };
        _type.SelectedIndexChanged += async (_, _) => await FilterChangedAsync();
        _channel.SelectedIndexChanged += async (_, _) => await FilterChangedAsync();
        _period.SelectedIndexChanged += async (_, _) => await FilterChangedAsync();
        _resend.Click += async (_, _) => await ResendAsync();
    }

    public override string Title => "Rappels";

    public override Task LoadAsync() => QueryAsync();

    public override Task ReactivateAsync() => QueryAsync();

    private async Task FilterChangedAsync()
    {
        if (!Alive)
        {
            return;
        }

        _page = 1;
        await RunAsync(QueryAsync);
    }

    private (DateTime? From, DateTime? To) Range() => _period.SelectedValue<Period>() switch
    {
        Period.Today => (DateTime.Today, DateTime.Today),
        Period.Week => (DateTime.Today.AddDays(-6), DateTime.Today),
        Period.Month => (DateTime.Today.AddDays(-29), DateTime.Today),
        _ => (null, null)
    };

    private async Task QueryAsync()
    {
        int version = ++_version;
        (DateTime? from, DateTime? to) = Range();
        PagedResult<ReminderLogDto>? result = await Api.GetRemindersAsync(
            _page, PageSize, _type.SelectedValue<string?>(), _channel.SelectedValue<string?>(), from, to);
        if (!Alive || version != _version || result is null)
        {
            return;
        }

        _card.Grid.SetRows(result.Items.Select(r => new ReminderRow(r)).ToList());
        _card.UpdateEmpty();
        _pager.Update(result.Page, result.TotalPages, result.Total, "rappel");

        _total.Set(result.Total.ToString(), "selon les filtres");
        int failures = result.Items.Count(r => !r.Success);
        _failed.Set(failures.ToString(), failures == 0 ? "aucun échec" : "à vérifier");
        if (result.Items.Count > 0)
        {
            ReminderLogDto latest = result.Items.OrderByDescending(r => r.SentAt).First();
            _last.Set(Fmt.DateTime(latest.SentAt), $"{latest.TicketNumber} · {latest.ChannelLabel}");
        }
        else
        {
            _last.Set("—", "aucun envoi");
        }
    }

    private async Task ResendAsync()
    {
        ReminderRow? row = _card.Grid.SelectedItem<ReminderRow>();
        if (row is null)
        {
            return;
        }

        if (!AppDialogs.Confirm(FindForm(), "Relancer le client",
                $"Envoyer une relance à {row.Client} pour la commande {row.Ticket} ?\n" +
                "Le message part sur le canal choisi par le client, qui doit avoir donné son consentement.",
                "Relancer"))
        {
            return;
        }

        ReminderLogDto? sent = null;
        bool ok = await RunAsync(async () => sent = await Api.SendReminderAsync(row.Source.OrderId), "Relance du client");
        if (ok && sent is not null && Alive)
        {
            Host.Toast($"Relance envoyée à {sent.ClientName} ({sent.ChannelLabel}).");
            _page = 1;
            await RunAsync(QueryAsync);
        }
    }
}
