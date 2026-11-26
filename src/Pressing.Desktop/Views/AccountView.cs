using Pressing.Client;
using Pressing.Core;
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
        SetPage(header, new VStack().Add(new GridRow { StackBelow = 900 }.Add(infoCard, 6).Add(passwordCard, 6)), scroll: true);

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

        if (_new.Text.Length < 8)
        {
            ShowMessage("Le nouveau mot de passe doit contenir au moins 8 caractères.", Theme.Red);
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
