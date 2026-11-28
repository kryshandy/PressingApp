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
            "state" => !Source.IsActive ? ("Inactif", Theme.Slate)
                : Source.IsLocked ? ("Verrouillé", Theme.Amber)
                : ("Actif", Theme.Green),
            "twofactor" => Source.TwoFactorEnabled ? ("2FA active", Theme.Blue) : null,
            _ => null
        };

        public Color? GetTint(string columnName) => !Source.IsActive ? Theme.TextFaint : null;
    }

    private readonly GridCard _card = new("Aucun membre du personnel.");
    private readonly FlatButton _edit = new() { Text = "Modifier", Kind = ButtonKind.Secondary, IconGlyph = Icons.Edit, Enabled = false };
    private readonly FlatButton _password = new() { Text = "Mot de passe", Kind = ButtonKind.Secondary, IconGlyph = Icons.Key, Enabled = false };
    private readonly FlatButton _unlock = new() { Text = "Déverrouiller", Kind = ButtonKind.Secondary, IconGlyph = Icons.Refresh, Enabled = false };
    private readonly FlatButton _resetTwoFactor = new() { Text = "Réinitialiser la 2FA", Kind = ButtonKind.Secondary, Enabled = false };

    public StaffView(IAppHost host) : base(host)
    {
        PageHeader header = new("Personnel", "Comptes des employés, rôles d'accès et mots de passe");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));
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
        grid.AddText("name", "NOM", nameof(StaffRow.Name), 40, minWidth: 160);
        grid.AddText("username", "IDENTIFIANT", nameof(StaffRow.Username), 25, minWidth: 120);
        grid.AddText("email", "E-MAIL", nameof(StaffRow.Email), 45, minWidth: 180);
        grid.AddPill("role", "RÔLE", fixedWidth: 148);
        grid.AddPill("state", "ÉTAT", fixedWidth: 120);
        grid.AddPill("twofactor", "2FA", fixedWidth: 112);
        grid.AddText("last", "DERNIÈRE CONNEXION", nameof(StaffRow.LastLogin), fixedWidth: 180);
        grid.SetOptional("last", "email");
        grid.SelectionChanged += (_, _) =>
        {
            bool has = grid.SelectedItem<StaffRow>() is not null;
            _edit.Enabled = has;
            _password.Enabled = has;
            StaffRow? selected = grid.SelectedItem<StaffRow>();
            _unlock.Enabled = selected is { Source.IsLocked: true };
            _resetTwoFactor.Enabled = selected is { Source.TwoFactorEnabled: true };
        };
        grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].DataBoundItem is StaffRow)
            {
                await EditAsync();
            }
        };

        TitledCard list = new("Comptes", _card, hint.Text, flushContent: true);
        list.AddAction(_edit);
        list.AddAction(_password);
        list.AddAction(_unlock);
        list.AddAction(_resetTwoFactor);
        SetPage(header, list);
        _edit.Click += async (_, _) => await EditAsync();
        _password.Click += async (_, _) => await ResetPasswordAsync();
        _unlock.Click += async (_, _) => await UnlockAsync();
        _resetTwoFactor.Click += async (_, _) => await ResetTwoFactorAsync();
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

    private async Task UnlockAsync()
    {
        StaffRow? row = _card.Grid.SelectedItem<StaffRow>();
        if (row is null)
        {
            return;
        }

        MessageResponse? result = null;
        if (await RunAsync(async () => result = await Api.UnlockStaffAsync(row.Source.Id), "Déverrouillage impossible"))
        {
            Host.Toast(result?.Message ?? "Compte déverrouillé.");
            await RunAsync(LoadAsync);
        }
    }

    private async Task ResetTwoFactorAsync()
    {
        StaffRow? row = _card.Grid.SelectedItem<StaffRow>();
        if (row is null)
        {
            return;
        }

        if (!AppDialogs.Confirm(FindForm(), "Réinitialiser la double authentification",
                $"Effacer le secret et les codes de secours de {row.Source.FullName} ?" + Environment.NewLine +
                "Le membre devra la réactiver à sa prochaine connexion si le réglage « Exiger la double authentification » est actif.",
                "Réinitialiser", danger: true))
        {
            return;
        }

        MessageResponse? result = null;
        if (await RunAsync(async () => result = await Api.ResetStaffTwoFactorAsync(row.Source.Id), "Réinitialisation impossible"))
        {
            Host.Toast(result?.Message ?? "Double authentification réinitialisée.");
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
