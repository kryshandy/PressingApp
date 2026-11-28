using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Security;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Mon compte : informations de l'utilisateur et changement de mot de passe.</summary>
public sealed class AccountView : BaseView
{
    private readonly InputBox _current = new() { PasswordChar = '•', IconGlyph = Icons.Key, PlaceholderText = "Mot de passe actuel" };
    private readonly InputBox _new = new() { PasswordChar = '•', IconGlyph = Icons.Key, PlaceholderText = "Min. 8 caractères" };
    private readonly InputBox _confirm = new() { PasswordChar = '•', IconGlyph = Icons.Key, PlaceholderText = "Retapez le nouveau mot de passe" };
    private readonly TextLabel _message = new() { Wrap = true, Font = Theme.F(9.5f) };
    private readonly FlatButton _save = new() { Text = "Changer le mot de passe", Kind = ButtonKind.Primary, IconGlyph = Icons.Save };
    private readonly TextLabel _twoFactorState = new() { Wrap = true, Height = Theme.S(48), Font = Theme.F(10f), ForeColor = Theme.Text };
    private readonly FlatButton _enableTwoFactor = new() { Text = "Activer la double authentification", Kind = ButtonKind.Secondary, IconGlyph = Icons.Key };
    private readonly FlatButton _disableTwoFactor = new() { Text = "Désactiver", Kind = ButtonKind.Secondary, Visible = false };
    private TwoFactorStatusDto? _twoFactor;

    public AccountView(IAppHost host) : base(host)
    {
        PageHeader header = new("Mon compte", "Vos informations et la sécurité de votre accès");

        InfoList info = new();
        info.Add("Nom", Session.Staff.FullName, null, true);
        info.Add("Identifiant", Session.Staff.Username);
        info.Add("Rôle", Session.Staff.Role.Label(), Theme.Accent, true);
        info.Add("E-mail", string.IsNullOrWhiteSpace(Session.Staff.Email) ? "—" : Session.Staff.Email);
        info.Add("Dernière connexion", Session.Staff.LastLoginAt.HasValue ? Fmt.DateTime(Session.Staff.LastLoginAt.Value) : "—");
        info.Add("Serveur", Api.BaseUrl);
        TitledCard infoCard = new("Profil", info, null, false, true);

        FormGrid form = new(1);
        form.AddField("Mot de passe actuel", _current);
        form.AddField("Nouveau mot de passe", _new);
        form.AddField("Confirmation", _confirm);
        form.Add(_message);
        ActionBar actions = new(alignRight: false);
        actions.Add(_save);
        form.Add(actions);
        TitledCard passwordCard = new("Changer mon mot de passe", form, "Choisissez un mot de passe d'au moins 8 caractères.", false, true);

        // 6 + 6 : profil à gauche, mot de passe à droite (même hauteur).
        VStack page = new VStack().Add(new GridRow { StackBelow = 900 }.Add(infoCard, 6).Add(passwordCard, 6));
        if (Session.Role == UserRole.Manager)
        {
            // Double authentification (TOTP) : réservée aux managers ; l'administrateur n'en a pas.
            FormGrid twoFactor = new(1);
            twoFactor.Add(_twoFactorState);
            ActionBar twoFactorActions = new(alignRight: false);
            twoFactorActions.Add(_enableTwoFactor);
            twoFactorActions.Add(_disableTwoFactor);
            twoFactor.Add(twoFactorActions);
            page.Add(new TitledCard("Double authentification", twoFactor,
                "Un code à usage unique de votre téléphone s'ajoute au mot de passe.", false, true));
            _enableTwoFactor.Click += async (_, _) => await EnableTwoFactorAsync();
            _disableTwoFactor.Click += async (_, _) => await DisableTwoFactorAsync();
        }

        SetPage(header, page, scroll: true);

        _save.Click += async (_, _) => await SaveAsync();
        _confirm.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SaveAsync();
            }
        };
    }

    public override string Title => "Mon compte";

    public override async Task LoadAsync()
    {
        if (Session.Role != UserRole.Manager)
        {
            return;
        }

        _twoFactor = await Api.GetTwoFactorStatusAsync();
        if (Alive)
        {
            ShowTwoFactorStatus();
        }
    }

    private void ShowTwoFactorStatus()
    {
        if (_twoFactor is null)
        {
            return;
        }

        if (_twoFactor.Enabled)
        {
            _twoFactorState.Text = $"Activée. Il vous reste {_twoFactor.RecoveryCodesLeft} code(s) de secours." +
                (_twoFactor.Required ? " L'administrateur l'exige : seul lui peut la désactiver." : string.Empty);
            _twoFactorState.ForeColor = Theme.Green;
            _enableTwoFactor.Visible = false;
            _disableTwoFactor.Visible = !_twoFactor.Required;
        }
        else
        {
            _twoFactorState.Text = _twoFactor.Required
                ? "Non activée. Elle est exigée pour votre compte : activez-la pour continuer à vous connecter."
                : "Non activée. Elle n'est pas exigée actuellement, mais vous pouvez la mettre en place.";
            _twoFactorState.ForeColor = _twoFactor.Required ? Theme.Amber : Theme.TextMuted;
            _enableTwoFactor.Visible = true;
            _disableTwoFactor.Visible = false;
        }

        this.RequestPageLayout();
    }

    private async Task EnableTwoFactorAsync()
    {
        TwoFactorSetupDto? setup = null;
        if (!await RunAsync(async () => setup = await Api.BeginTwoFactorSetupAsync(), "Activation impossible") || setup is null)
        {
            return;
        }

        using TwoFactorSetupDialog dialog = new(Api, setup, mandatory: false);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Double authentification activée.");
        }

        await RunAsync(LoadAsync);
    }

    private async Task DisableTwoFactorAsync()
    {
        using TwoFactorDisableDialog dialog = new(Api);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Double authentification désactivée.");
            await RunAsync(LoadAsync);
        }
    }

    private void ShowMessage(string text, Color color)
    {
        _message.Text = text;
        _message.ForeColor = color;
    }

    private async Task SaveAsync()
    {
        if (_current.Text.Length == 0)
        {
            ShowMessage("Saisissez votre mot de passe actuel.", Theme.Red);
            _current.Focus();
            return;
        }

        if (!PasswordPolicy.IsValid(_new.Text))
        {
            ShowMessage(PasswordPolicy.Rule, Theme.Red);
            _new.Focus();
            return;
        }

        if (_new.Text != _confirm.Text)
        {
            ShowMessage("La confirmation ne correspond pas au nouveau mot de passe.", Theme.Red);
            _confirm.Focus();
            return;
        }

        ShowMessage(string.Empty, Theme.Red);
        bool ok = await RunAsync(async () => await Api.ChangeStaffPasswordAsync(_current.Text, _new.Text), "Changement de mot de passe impossible");
        if (ok)
        {
            _current.Text = _new.Text = _confirm.Text = string.Empty;
            ShowMessage("Mot de passe modifié avec succès.", Theme.Green);
            Host.Toast("Mot de passe modifié.");
        }
    }
}
