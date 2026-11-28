using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Security;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Dialogs;

/// <summary>Création / modification d'un membre du personnel.</summary>
public sealed class StaffDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly StaffUserDto? _existing;
    private readonly bool _isSelf;
    private readonly InputBox _fullName = new() { PlaceholderText = "ex. Awa Koné" };
    private readonly InputBox _username = new() { PlaceholderText = "ex. awa.kone" };
    private readonly InputBox _email = new() { PlaceholderText = "ex. awa@pressing-eclat.local" };
    private readonly SelectBox _role = new();
    private readonly ToggleBox _active = new() { Text = "Compte actif", Checked = true };
    private readonly InputBox _password = new() { PasswordChar = '•', IconGlyph = Icons.Key, PlaceholderText = "Min. 8 caractères" };
    private readonly InputBox _confirm = new() { PasswordChar = '•', IconGlyph = Icons.Key, PlaceholderText = "Retapez le mot de passe" };

    public StaffDialog(PressingApiClient api, StaffUserDto? existing, int currentUserId)
        : base(existing is null ? "Nouveau membre du personnel" : "Modifier le compte",
            existing is null ? "Créez l'accès d'un employé et choisissez son rôle." : existing.FullName, 700, existing is null ? 560 : 520)
    {
        _api = api;
        _existing = existing;
        _isSelf = existing is not null && existing.Id == currentUserId;
        Primary.Text = existing is null ? "Créer le compte" : "Enregistrer";
        Primary.IconGlyph = Icons.Save;

        _role.SetChoices(Enum.GetValues<UserRole>().Select(r => new Choice<UserRole>(r, r.Label())), existing?.Role ?? UserRole.Cashier);
        _fullName.Text = existing?.FullName ?? string.Empty;
        _username.Text = existing?.Username ?? string.Empty;
        _email.Text = existing?.Email ?? string.Empty;
        _active.Checked = existing?.IsActive ?? true;

        FormGrid grid = new(2);
        grid.AddField("Nom complet *", _fullName);
        grid.AddField("Identifiant de connexion *", _username);
        grid.AddField("Adresse e-mail", _email, 2);
        grid.AddField("Rôle", _role);
        if (existing is null)
        {
            grid.AddField("Mot de passe *", _password);
            grid.AddField("Confirmation *", _confirm);
        }

        FlowLayoutPanel flags = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, Theme.S(4), 0, 0),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        if (existing is not null)
        {
            flags.Controls.Add(_active);
        }

        if (_isSelf)
        {
            _role.Enabled = false;
            _active.Enabled = false;
            flags.Controls.Add(new Label
            {
                Text = "C'est votre propre compte : vous ne pouvez ni le désactiver ni retirer votre rôle d'administrateur.",
                AutoSize = true,
                MaximumSize = new Size(Theme.S(620), 0),
                Font = Theme.F(9.5f),
                ForeColor = Theme.Amber,
                Margin = new Padding(0, Theme.S(10), 0, 0)
            });
        }

        Body.Controls.Add(flags);
        Body.Controls.Add(grid);
        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _fullName.Focus();
    }

    public StaffUserDto? Result { get; private set; }

    private async Task SubmitAsync()
    {
        string fullName = _fullName.Text.Trim();
        string username = _username.Text.Trim();
        string email = _email.Text.Trim();
        if (fullName.Length == 0)
        {
            Fail("Le nom complet est obligatoire.", _fullName);
            return;
        }

        if (username.Length == 0)
        {
            Fail("L'identifiant de connexion est obligatoire.", _username);
            return;
        }

        if (username.Any(char.IsWhiteSpace))
        {
            Fail("L'identifiant ne doit pas contenir d'espace.", _username);
            return;
        }

        if (email.Length > 0 && !IsEmail(email))
        {
            Fail("L'adresse e-mail n'est pas valide.", _email);
            return;
        }

        if (_existing is null)
        {
            if (!PasswordPolicy.IsValid(_password.Text))
            {
                Fail(PasswordPolicy.Rule, _password);
                return;
            }

            if (_password.Text != _confirm.Text)
            {
                Fail("La confirmation ne correspond pas au mot de passe.", _confirm);
                return;
            }
        }

        ShowError(null);
        UserRole role = _role.SelectedValue<UserRole>();
        bool ok = await UiRun.RunAsync(this, async () =>
        {
            Result = _existing is null
                ? await _api.CreateStaffAsync(new CreateStaffRequest(fullName, username, email.Length == 0 ? null : email, role, _password.Text))
                : await _api.UpdateStaffAsync(_existing.Id, new UpdateStaffRequest(fullName, username, email.Length == 0 ? null : email, role, _active.Checked));
        }, "Enregistrement du compte impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }

    internal static bool IsEmail(string value)
    {
        int at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && value.IndexOf('.', at) > at + 1 && !value.EndsWith('.') && !value.Any(char.IsWhiteSpace);
    }

    private void Fail(string message, Control focus)
    {
        ShowError(message);
        focus.Focus();
    }
}

/// <summary>Réinitialisation du mot de passe d'un membre du personnel.</summary>
public sealed class StaffPasswordDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly StaffUserDto _user;
    private readonly InputBox _password = new() { PasswordChar = '•', IconGlyph = Icons.Key, PlaceholderText = "Min. 8 caractères" };
    private readonly InputBox _confirm = new() { PasswordChar = '•', IconGlyph = Icons.Key, PlaceholderText = "Retapez le mot de passe" };

    public StaffPasswordDialog(PressingApiClient api, StaffUserDto user)
        : base("Réinitialiser le mot de passe", $"{user.FullName} ({user.Username})", 560, 400)
    {
        _api = api;
        _user = user;
        Primary.Text = "Réinitialiser";
        Primary.IconGlyph = Icons.Key;
        FormGrid grid = new(1);
        grid.AddField("Nouveau mot de passe *", _password);
        grid.AddField("Confirmation *", _confirm);
        Body.Controls.Add(grid);
        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _password.Focus();
    }

    private async Task SubmitAsync()
    {
        if (!PasswordPolicy.IsValid(_password.Text))
        {
            ShowError(PasswordPolicy.Rule);
            _password.Focus();
            return;
        }

        if (_password.Text != _confirm.Text)
        {
            ShowError("La confirmation ne correspond pas au mot de passe.");
            _confirm.Focus();
            return;
        }

        ShowError(null);
        bool ok = await UiRun.RunAsync(this, () => _api.SetStaffPasswordAsync(_user.Id, _password.Text), "Réinitialisation impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }
}
