using Pressing.Client;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Dialogs;

/// <summary>Création ou modification d'un client.</summary>
public sealed class ClientEditDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly ClientDto? _existing;
    private readonly InputBox _first = new() { PlaceholderText = "Prénom" };
    private readonly InputBox _last = new() { PlaceholderText = "Nom" };
    private readonly InputBox _phone = new() { PlaceholderText = "+225 07 00 00 00 00", IconGlyph = Icons.Phone };
    private readonly InputBox _whatsApp = new() { PlaceholderText = "Identique au téléphone", IconGlyph = Icons.Phone };
    private readonly InputBox _email = new() { PlaceholderText = "client@exemple.com", IconGlyph = Icons.Mail };
    private readonly InputBox _password = new() { PlaceholderText = "Facultatif (min. 8 caractères)", PasswordChar = '•', IconGlyph = Icons.Key };
    private readonly InputBox _address = new() { PlaceholderText = "Adresse (livraison à domicile)" };
    private readonly InputBox _notes = new() { Multiline = true, PlaceholderText = "Préférences, remarques…" };
    private readonly ToggleBox _welcome = new() { Text = "Envoyer l'e-mail de bienvenue" };
    private readonly ToggleBox _active = new() { Text = "Client actif", Checked = true };
    private readonly SelectBox _preference = new();
    private bool _whatsAppTouched;

    public ClientEditDialog(PressingApiClient api, ClientDto? existing = null)
        : base(existing is null ? "Nouveau client" : "Modifier le client",
               existing is null ? "Les champs marqués d'une étoile sont obligatoires." : existing.FullName, 700, 700)
    {
        _api = api;
        _existing = existing;
        Primary.Text = existing is null ? "Créer le client" : "Enregistrer";
        Primary.IconGlyph = Icons.Save;
        _notes.Height = Theme.S(84);

        FormGrid grid = new();
        grid.AddField("Prénom *", _first);
        grid.AddField("Nom *", _last);
        grid.AddField("Téléphone *", _phone);
        grid.AddField("WhatsApp *", _whatsApp);
        grid.AddField("E-mail", _email);
        _preference.Format = value => value switch
        {
            ClientNotificationPreference.None => "Aucun rappel",
            ClientNotificationPreference.WhatsApp => "WhatsApp",
            ClientNotificationPreference.Email => "E-mail",
            _ => value.ToString() ?? string.Empty
        };
        _preference.SetItems(new object[]
        {
            ClientNotificationPreference.None,
            ClientNotificationPreference.WhatsApp,
            ClientNotificationPreference.Email
        });
        grid.AddField("Canal de rappel", _preference, 2);
        if (existing is null)
        {
            grid.AddField("Mot de passe web", _password, 1, "Donne accès à l'espace client du site.");
        }
        else
        {
            grid.Add(new Panel { Height = Theme.S(1) });
        }

        grid.AddField("Adresse", _address, 2);
        grid.AddField("Notes", _notes, 2);

        FlowLayoutPanel toggles = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = new Padding(0, Theme.S(4), 0, 0)
        };
        if (existing is null)
        {
            toggles.Controls.Add(_welcome);
        }
        else
        {
            toggles.Controls.Add(_active);
        }

        Body.Controls.Add(toggles);
        Body.Controls.Add(grid);

        if (existing is not null)
        {
            _first.Text = existing.FirstName;
            _last.Text = existing.LastName;
            _phone.Text = existing.Phone;
            _whatsApp.Text = existing.WhatsApp;
            _email.Text = existing.Email ?? string.Empty;
            _preference.SelectedItem = existing.NotificationPreference;
            _address.Text = existing.Address ?? string.Empty;
            _notes.Text = existing.Notes ?? string.Empty;
            _active.Checked = existing.IsActive;
            _whatsAppTouched = true;
        }

        _phone.TextChanged += (_, _) =>
        {
            if (!_whatsAppTouched)
            {
                _whatsApp.Text = _phone.Text;
            }
        };
        _whatsApp.InnerTextBox.KeyPress += (_, _) => _whatsAppTouched = true;
        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _first.Focus();
    }

    public ClientDto? Result { get; private set; }

    private async Task SubmitAsync()
    {
        string first = _first.Text.Trim();
        string last = _last.Text.Trim();
        string phone = _phone.Text.Trim();
        string whatsApp = _whatsApp.Text.Trim();
        string? email = string.IsNullOrWhiteSpace(_email.Text) ? null : _email.Text.Trim();

        (InputBox Box, string Message)? problem = null;
        if (first.Length == 0)
        {
            problem = (_first, "Le prénom est obligatoire.");
        }
        else if (last.Length == 0)
        {
            problem = (_last, "Le nom est obligatoire.");
        }
        else if (phone.Length == 0)
        {
            problem = (_phone, "Le téléphone est obligatoire.");
        }
        else if (whatsApp.Length == 0)
        {
            problem = (_whatsApp, "Le numéro WhatsApp est obligatoire.");
        }
        else if (email is not null && !email.Contains('@'))
        {
            problem = (_email, "L'adresse e-mail n'est pas valide.");
        }

        if (problem is { } p)
        {
            ShowError(p.Message);
            p.Box.Focus();
            return;
        }

        ShowError(null);
        string? address = string.IsNullOrWhiteSpace(_address.Text) ? null : _address.Text.Trim();
        string? notes = string.IsNullOrWhiteSpace(_notes.Text) ? null : _notes.Text.Trim();
        bool ok = await UiRun.RunAsync(this, async () =>
        {
            if (_existing is null)
            {
                string? password = string.IsNullOrEmpty(_password.Text) ? null : _password.Text;
                Result = await _api.CreateClientAsync(new CreateClientRequest(
                    first, last, phone, whatsApp, email, address, notes, password,
                    _welcome.Checked && email is not null && password is not null,
                    _preference.SelectedItem is ClientNotificationPreference createPreference ? createPreference : ClientNotificationPreference.None));
            }
            else
            {
                Result = await _api.UpdateClientAsync(_existing.Id, new UpdateClientRequest(
                    first, last, phone, whatsApp, email, address, notes, _active.Checked,
                    _preference.SelectedItem is ClientNotificationPreference updatePreference ? updatePreference : ClientNotificationPreference.None));
            }
        }, _existing is null ? "Création du client impossible" : "Modification impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }
}

/// <summary>Activation / réinitialisation de l'accès web d'un client.</summary>
public sealed class ClientAccessDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly ClientDto _client;
    private readonly InputBox _password = new() { PasswordChar = '•', PlaceholderText = "Min. 8 caractères", IconGlyph = Icons.Key };
    private readonly ToggleBox _welcome = new() { Text = "Envoyer l'e-mail de bienvenue (si une adresse e-mail existe)" };

    public ClientAccessDialog(PressingApiClient api, ClientDto client)
        : base(client.WebAccessEnabled ? "Réinitialiser l'accès web" : "Activer l'accès web", client.FullName, 560, 400)
    {
        _api = api;
        _client = client;
        Primary.Text = client.WebAccessEnabled ? "Réinitialiser" : "Activer l'accès";

        FormGrid grid = new(1);
        grid.AddField("Nouveau mot de passe", _password, 1, "Le client l'utilisera avec son téléphone ou son e-mail sur le site web.");
        grid.Add(_welcome);
        _welcome.Checked = !string.IsNullOrWhiteSpace(client.Email);
        _welcome.Enabled = !string.IsNullOrWhiteSpace(client.Email);
        Body.Controls.Add(grid);
        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _password.Focus();
    }

    public ClientDto? Result { get; private set; }

    private async Task SubmitAsync()
    {
        if (_password.Text.Length < 8)
        {
            ShowError("Le mot de passe doit contenir au moins 8 caractères.");
            _password.Focus();
            return;
        }

        ShowError(null);
        bool ok = await UiRun.RunAsync(this, async () =>
        {
            Result = await _api.SetClientPasswordAsync(_client.Id,
                new SetClientPasswordRequest(_password.Text, true, _welcome.Checked && _welcome.Enabled));
        }, "Accès web");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }
}

/// <summary>Ajustement manuel des points de fidélité (Manager / Administrateur).</summary>
public sealed class LoyaltyAdjustDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly ClientDto _client;
    private readonly InputBox _points = new() { TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, Text = "0" };
    private readonly InputBox _note = new() { PlaceholderText = "Motif de l'ajustement (obligatoire)" };

    public LoyaltyAdjustDialog(PressingApiClient api, ClientDto client)
        : base("Ajuster les points de fidélité", $"{client.FullName} · solde actuel : {client.LoyaltyPoints} point(s)", 540, 420)
    {
        _api = api;
        _client = client;
        Primary.Text = "Appliquer";

        FormGrid grid = new(1);
        grid.AddField("Points à ajouter (ou à retirer avec un signe −)", _points);
        grid.AddField("Motif", _note);
        Body.Controls.Add(grid);
        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _points.Focus();
    }

    public ClientDto? Result { get; private set; }

    private async Task SubmitAsync()
    {
        if (!int.TryParse(_points.Text.Trim().Replace("−", "-"), out int points) || points == 0)
        {
            ShowError("Saisissez un nombre de points non nul.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_note.Text))
        {
            ShowError("Le motif est obligatoire.");
            _note.Focus();
            return;
        }

        ShowError(null);
        bool ok = await UiRun.RunAsync(this, async () => Result = await _api.AdjustLoyaltyAsync(_client.Id, points, _note.Text.Trim()), "Ajustement impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }
}
