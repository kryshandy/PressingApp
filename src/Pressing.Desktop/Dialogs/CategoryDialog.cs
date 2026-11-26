using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Dialogs;

/// <summary>Création / modification d'une catégorie et de sa grille tarifaire.</summary>
public sealed class CategoryDialog : DialogForm
{
    private sealed class PriceLine
    {
        public required ServiceType Service { get; init; }
        public required InputBox Price { get; init; }
        public required InputBox Express { get; init; }
        public required InputBox Days { get; init; }
        public required ToggleBox Active { get; init; }
    }

    private readonly PressingApiClient _api;
    private readonly CategoryDto? _existing;
    private readonly InputBox _name = new() { PlaceholderText = "ex. Chemise" };
    private readonly InputBox _order = new() { TextAlign = HorizontalAlignment.Right };
    private readonly ToggleBox _active = new() { Text = "Catégorie active", Checked = true };
    private readonly List<PriceLine> _lines = new();

    public CategoryDialog(PressingApiClient api, CategoryDto? existing)
        : base(existing is null ? "Nouvelle catégorie" : "Modifier la catégorie", existing?.Name ?? "Définissez le nom puis les tarifs par service.", 780, 640)
    {
        _api = api;
        _existing = existing;
        Primary.Text = existing is null ? "Créer" : "Enregistrer";
        Primary.IconGlyph = Icons.Save;

        FormGrid top = new(3);
        top.AddField("Nom de la catégorie *", _name, 2);
        top.AddField("Ordre d'affichage", _order);
        _name.Text = existing?.Name ?? string.Empty;
        _order.Text = (existing?.SortOrder ?? 0).ToString();

        TableLayoutPanel prices = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 5,
            Margin = Padding.Empty
        };
        prices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        prices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        prices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        prices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        prices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(70)));
        string[] headers = { "SERVICE", "PRIX", "SUPPL. EXPRESS", "DÉLAI (J)", "ACTIF" };
        for (int c = 0; c < headers.Length; c++)
        {
            // TextLabel : aucune marge interne GDI, donc bord gauche exact ; les colonnes numériques sont
            // alignées à droite comme les valeurs saisies en dessous.
            bool numeric = c is 1 or 2 or 3;
            prices.Controls.Add(new TextLabel
            {
                Text = headers[c],
                Font = Theme.FieldLabel,
                ForeColor = Theme.TextMuted,
                TextAlign = numeric ? ContentAlignment.BottomRight : ContentAlignment.BottomLeft,
                Dock = DockStyle.Fill,
                Height = Theme.S(20),
                Margin = new Padding(c == 4 ? Theme.S(8) : 0, 0, c == 4 ? 0 : Theme.S(8 + (numeric ? 12 : 0)), Theme.S(6))
            }, c, 0);
        }

        int row = 1;
        foreach (ServiceType service in Enum.GetValues<ServiceType>())
        {
            CategoryPriceDto? current = existing?.Prices.FirstOrDefault(p => p.ServiceType == service);
            PriceLine line = new()
            {
                Service = service,
                Price = new InputBox { Suffix = "F", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, AmountValue = current?.Price ?? 0, Margin = new Padding(0, 0, Theme.S(8), Theme.S(8)) },
                Express = new InputBox { Suffix = "F", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, AmountValue = current?.ExpressSupplement ?? 0, Margin = new Padding(0, 0, Theme.S(8), Theme.S(8)) },
                Days = new InputBox { TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, Text = (current?.EstimatedDays ?? 2).ToString(), Margin = new Padding(0, 0, Theme.S(8), Theme.S(8)) },
                Active = new ToggleBox { Checked = current?.IsActive ?? false, Margin = new Padding(Theme.S(8), Theme.S(6), 0, 0) }
            };
            line.Price.Dock = DockStyle.Fill;
            line.Express.Dock = DockStyle.Fill;
            line.Days.Dock = DockStyle.Fill;
            _lines.Add(line);
            Label name = new()
            {
                Text = service.Label(),
                AutoSize = true,
                Font = Theme.F(10f, FontStyle.Bold),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, Theme.S(8), Theme.S(8))
            };
            prices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            prices.Controls.Add(name, 0, row);
            prices.Controls.Add(line.Price, 1, row);
            prices.Controls.Add(line.Express, 2, row);
            prices.Controls.Add(line.Days, 3, row);
            prices.Controls.Add(line.Active, 4, row);
            row++;
        }

        FlowLayoutPanel flags = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, Theme.S(8), 0, 0) };
        if (existing is not null)
        {
            _active.Checked = existing.IsActive;
            flags.Controls.Add(_active);
        }

        Body.Controls.Add(flags);
        Body.Controls.Add(prices);
        Body.Controls.Add(top);
        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _name.Focus();
    }

    public CategoryDto? Result { get; private set; }

    private async Task SubmitAsync()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0)
        {
            ShowError("Le nom est obligatoire.");
            _name.Focus();
            return;
        }

        int sort = int.TryParse(_order.Text.Trim(), out int s) ? s : 0;
        List<UpdateCategoryPriceRequest> prices = new();
        foreach (PriceLine line in _lines)
        {
            if (!int.TryParse(line.Days.Text.Trim(), out int days) || days < 0)
            {
                ShowError($"Délai invalide pour « {line.Service.Label()} ».");
                return;
            }

            if (line.Price.AmountValue < 0 || line.Express.AmountValue < 0)
            {
                ShowError($"Les montants de « {line.Service.Label()} » doivent être positifs.");
                return;
            }

            prices.Add(new UpdateCategoryPriceRequest(line.Service, line.Price.AmountValue, line.Express.AmountValue, days, line.Active.Checked));
        }

        if (!prices.Any(p => p.IsActive && p.Price > 0))
        {
            ShowError("Activez au moins un service avec un prix.");
            return;
        }

        ShowError(null);
        bool ok = await UiRun.RunAsync(this, async () =>
        {
            Result = _existing is null
                ? await _api.CreateCategoryAsync(new CreateCategoryRequest(name, string.Empty, sort, prices))
                : await _api.UpdateCategoryAsync(_existing.Id, new UpdateCategoryRequest(name, _existing.Icon, sort, _active.Checked, prices));
        }, "Enregistrement des tarifs impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }
}

/// <summary>Liste des notifications du personnel.</summary>
public sealed class NotificationsDialog : DialogForm
{
    private sealed class NotifRow : IPillRow, ITintRow
    {
        public NotifRow(NotificationDto n)
        {
            Source = n;
            Title = n.Title;
            Message = n.Message;
            When = Fmt.Relative(n.CreatedAt);
        }

        public NotificationDto Source { get; }
        public string Title { get; }
        public string Message { get; }
        public string When { get; }

        public (string Text, Color Color)? GetPill(string columnName) => columnName == "kind"
            ? (Source.Kind switch
            {
                NotificationKind.Success => "Succès",
                NotificationKind.Warning => "Attention",
                NotificationKind.Danger => "Alerte",
                _ => "Info"
            }, Source.Kind switch
            {
                NotificationKind.Success => Theme.Green,
                NotificationKind.Warning => Theme.Amber,
                NotificationKind.Danger => Theme.Red,
                _ => Theme.Blue
            })
            : null;

        public Color? GetTint(string columnName) => Source.IsRead ? Theme.TextMuted : null;
    }

    private readonly PressingApiClient _api;
    private readonly GridCard _card = new("Aucune notification.");

    public NotificationsDialog(PressingApiClient api, IReadOnlyList<NotificationDto> items)
        : base("Notifications", "Double-cliquez sur une notification pour ouvrir la commande concernée.", 820, 560)
    {
        _api = api;
        // Secondary (à gauche) : action ; Primary (à droite) : fermer.
        Secondary.Text = "Tout marquer comme lu";
        Secondary.DialogResult = DialogResult.None;
        Primary.Text = "Fermer";
        Primary.DialogResult = DialogResult.Cancel;

        ThemedGrid grid = _card.Grid;
        grid.AddPill("kind", "TYPE", fixedWidth: 124);
        grid.AddText("title", "TITRE", nameof(NotifRow.Title), 40, minWidth: 140);
        grid.AddText("message", "MESSAGE", nameof(NotifRow.Message), 60, minWidth: 160);
        grid.AddText("when", "QUAND", nameof(NotifRow.When), fixedWidth: 120);
        grid.SetRows(items.Select(n => new NotifRow(n)).ToList());
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].DataBoundItem is NotifRow row)
            {
                string? link = row.Source.Link;
                if (link is not null && link.StartsWith("/orders/", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(link["/orders/".Length..], out int id))
                {
                    OpenOrderId = id;
                    DialogResult = DialogResult.OK;
                }
            }
        };
        Body.Controls.Add(_card);
        Secondary.Click += async (_, _) =>
        {
            bool ok = await UiRun.RunAsync(this, () => _api.MarkAllNotificationsReadAsync(), "Notifications");
            if (ok)
            {
                grid.SetRows(items.Select(n => new NotifRow(n with { IsRead = true })).ToList());
            }
        };
        AcceptButton = Primary;
        CancelButton = Primary;
    }

    public int? OpenOrderId { get; private set; }
}
