using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Gestion du personnel : comptes, rôles et mots de passe (Administrateurs).</summary>
public sealed class StaffView : BaseView
{
    private sealed class StaffRow : IPillRow, ITintRow
    {
        public StaffRow(StaffUserDto s, bool self)
        {
            Source = s;
            Name = self ? s.FullName + "  (vous)" : s.FullName;
            Username = s.Username;
            Email = string.IsNullOrWhiteSpace(s.Email) ? "—" : s.Email;
            LastLogin = s.LastLoginAt.HasValue ? Fmt.DateTime(s.LastLoginAt.Value) : "Jamais";
        }

        public StaffUserDto Source { get; }
        public string Name { get; }
        public string Username { get; }
        public string Email { get; }
        public string LastLogin { get; }

        public (string Text, Color Color)? GetPill(string columnName) => columnName switch
        {
            "role" => (Source.Role.Label(), Source.Role switch
            {
                UserRole.Admin => Theme.Violet,
                UserRole.Manager => Theme.Blue,
                _ => Theme.Slate
            }),
            "state" => Source.IsActive ? ("Actif", Theme.Green) : ("Inactif", Theme.Slate),
            _ => null
        };

        public Color? GetTint(string columnName) => !Source.IsActive ? Theme.TextFaint : null;
    }

    private readonly GridCard _card = new("Aucun membre du personnel.");
    private readonly FlatButton _edit = new() { Text = "Modifier", Kind = ButtonKind.Secondary, IconGlyph = Icons.Edit, Enabled = false };
    private readonly FlatButton _password = new() { Text = "Mot de passe", Kind = ButtonKind.Secondary, IconGlyph = Icons.Key, Enabled = false };

    public StaffView(IAppHost host) : base(host)
    {
        PageHeader header = new("Personnel", "Comptes des employés, rôles d'accès et mots de passe");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));
        header.AddControl(_edit);
        header.AddControl(_password);
        header.AddAction("Nouveau membre", ButtonKind.Primary, Icons.Add, async (_, _) => await CreateAsync());

        Label hint = new()
        {
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            Font = Theme.F(9.5f),
            Margin = new Padding(0, Theme.S(8), 0, 0),
            Text = "Double-cliquez sur une ligne pour modifier le compte. Votre propre compte ne peut être ni désactivé ni rétrogradé."
        };

        ThemedGrid grid = _card.Grid;
        grid.AddText("name", "NOM", nameof(StaffRow.Name), 24, minWidth: 150);
        grid.AddText("username", "IDENTIFIANT", nameof(StaffRow.Username), 16, minWidth: 110);
        grid.AddText("email", "E-MAIL", nameof(StaffRow.Email), 26, minWidth: 160);
        grid.AddPill("role", "RÔLE", 14, 140);
        grid.AddPill("state", "ÉTAT", 10, 100);
        grid.AddText("last", "DERNIÈRE CONNEXION", nameof(StaffRow.LastLogin), 18, minWidth: 140);
        grid.SelectionChanged += (_, _) =>
        {
            bool has = grid.SelectedItem<StaffRow>() is not null;
            _edit.Enabled = has;
            _password.Enabled = has;
        };
        grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].DataBoundItem is StaffRow)
            {
                await EditAsync();
            }
        };

        Controls.Add(UiKit.Rows((header, false), (_card, true), (hint, false)));
        _edit.Click += async (_, _) => await EditAsync();
        _password.Click += async (_, _) => await ResetPasswordAsync();
    }

    public override string Title => "Personnel";

    public override async Task LoadAsync()
    {
        List<StaffUserDto>? staff = await Api.GetStaffAsync();
        if (!Alive)
        {
            return;
        }

        _card.Grid.SetRows((staff ?? new List<StaffUserDto>())
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.Role).ThenBy(s => s.FullName)
            .Select(s => new StaffRow(s, s.Id == Session.Staff.Id)).ToList());
        _card.UpdateEmpty();
    }

    private async Task CreateAsync()
    {
        using StaffDialog dialog = new(Api, null, Session.Staff.Id);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Compte créé.");
            await RunAsync(LoadAsync);
        }
    }

    private async Task EditAsync()
    {
        StaffRow? row = _card.Grid.SelectedItem<StaffRow>();
        if (row is null)
        {
            return;
        }

        using StaffDialog dialog = new(Api, row.Source, Session.Staff.Id);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Compte mis à jour.");
            await RunAsync(LoadAsync);
        }
    }

    private async Task ResetPasswordAsync()
    {
        StaffRow? row = _card.Grid.SelectedItem<StaffRow>();
        if (row is null)
        {
            return;
        }

        using StaffPasswordDialog dialog = new(Api, row.Source);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Mot de passe réinitialisé.");
        }
    }
}
