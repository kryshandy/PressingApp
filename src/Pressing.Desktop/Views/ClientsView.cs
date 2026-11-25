using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Liste paginée des clients avec recherche.</summary>
public sealed class ClientsView : BaseView
{
    private const int PageSize = 25;

    private readonly InputBox _search = new() { IconGlyph = Icons.Search, PlaceholderText = "Nom, téléphone ou e-mail…" };
    private readonly ToggleBox _inactive = new() { Text = "Inclure les clients inactifs" };
    private readonly GridCard _card = new("Aucun client ne correspond à cette recherche.");
    private readonly PagerBar _pager = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 400 };
    private int _page = 1;
    private int _version;
    private bool _suspend;

    public ClientsView(IAppHost host) : base(host)
    {
        PageHeader header = new("Clients", "Fichier client, historique et accès web");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));
        header.AddAction("Nouveau client", ButtonKind.Primary, Icons.Add, async (_, _) => await CreateAsync());

        TableLayoutPanel filters = new()
        {
            AutoSize = true,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, Theme.S(14))
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 0, Theme.S(20), 0);
        _inactive.Anchor = AnchorStyles.Right;
        filters.Controls.Add(_search, 0, 0);
        filters.Controls.Add(_inactive, 1, 0);

        ThemedGrid grid = _card.Grid;
        grid.AddText("name", "CLIENT", nameof(ClientRow.Name), 24, minWidth: 140);
        grid.AddText("phone", "TÉLÉPHONE", nameof(ClientRow.Phone), 16, minWidth: 120);
        grid.AddText("email", "E-MAIL", nameof(ClientRow.Email), 24, minWidth: 140);
        grid.AddText("orders", "COMMANDES", nameof(ClientRow.Orders), 11, true, 90);
        grid.AddText("points", "POINTS", nameof(ClientRow.Points), 9, true, 70);
        grid.AddText("balance", "SOLDE DÛ", nameof(ClientRow.Balance), 13, true, 100);
        grid.AddPill("web", "WEB", 11, 100);
        grid.AddPill("state", "", 9, 80);
        grid.CellDoubleClick += (_, e) => OpenRow(e.RowIndex);
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OpenRow(grid.CurrentRow?.Index ?? -1);
                e.Handled = true;
            }
        };

        Controls.Add(UiKit.Rows((header, false), (filters, false), (_card, true), (_pager, false)));

        _pager.PageRequested += async (_, page) =>
        {
            _page = page;
            await RunAsync(QueryAsync);
        };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            _page = 1;
            await RunAsync(QueryAsync);
        };
        _search.TextChanged += (_, _) =>
        {
            if (!_suspend)
            {
                _debounce.Stop();
                _debounce.Start();
            }
        };
        _inactive.CheckedChanged += async (_, _) =>
        {
            _page = 1;
            await RunAsync(QueryAsync);
        };
    }

    public override string Title => "Clients";

    public override void ApplyParameter(object? parameter)
    {
        if (parameter is string text)
        {
            _suspend = true;
            _search.Text = text;
            _suspend = false;
        }
    }

    public override Task LoadAsync() => QueryAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OpenRow(int rowIndex)
    {
        ThemedGrid grid = _card.Grid;
        if (rowIndex >= 0 && rowIndex < grid.Rows.Count && grid.Rows[rowIndex].DataBoundItem is ClientRow row)
        {
            Host.OpenClient(row.Id);
        }
    }

    private async Task CreateAsync()
    {
        using ClientEditDialog dialog = new(Api);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.Result is { } created)
        {
            Host.Toast($"Client « {created.FullName} » créé.");
            Host.OpenClient(created.Id);
        }

        await Task.CompletedTask;
    }

    private async Task QueryAsync()
    {
        int version = ++_version;
        PagedResult<ClientDto>? result = await Api.SearchClientsAsync(
            string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text.Trim(), _page, PageSize, _inactive.Checked);
        if (!Alive || version != _version || result is null)
        {
            return;
        }

        _card.Grid.SetRows(result.Items.Select(c => new ClientRow(c)).ToList());
        _card.UpdateEmpty();
        _pager.Update(result.Page, result.TotalPages, result.Total, "client");
    }
}
