using Pressing.Client;
using Pressing.Core.Dtos;
using Pressing.Desktop.Ui;
using QRCoder;

namespace Pressing.Desktop.Dialogs;

/// <summary>
/// Activation de la double authentification : QR code (ou secret à saisir), vérification d'un premier code, puis
/// affichage unique des 8 codes de secours. Utilisé depuis « Mon compte » et, pour un manager, avant l'ouverture de la coque.
/// </summary>
public sealed class TwoFactorSetupDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly InputBox _code = new() { PlaceholderText = "000000", IconGlyph = Icons.Key, MaxLength = 12 };
    private IReadOnlyList<string> _recoveryCodes = Array.Empty<string>();

    public TwoFactorSetupDialog(PressingApiClient api, TwoFactorSetupDto setup, bool mandatory)
        : base("Activer la double authentification",
            mandatory ? "Obligatoire pour les comptes responsables : sans elle, la connexion s'arrête ici." : "Un code à usage unique s'ajoute à votre mot de passe.",
            560, 740)
    {
        _api = api;
        Primary.Text = "Activer";
        Primary.IconGlyph = Icons.Check;

        // Les blocs sont empilés de bas en haut : le dernier ajouté s'affiche en premier.
        Body.Controls.Add(new FieldBox("2. Code à 6 chiffres affiché par l'application", _code));
        Body.Controls.Add(new TextLabel
        {
            Text = "Saisie manuelle : " + string.Join(' ', Chunk(setup.Secret, 4)),
            Font = Theme.F(9.5f),
            ForeColor = Theme.TextMuted,
            Wrap = true
        });
        Body.Controls.Add(new QrHolder(RenderQr(setup.OtpAuthUri)));
        Body.Controls.Add(new TextLabel
        {
            Text = "1. Dans Google Authenticator ou Microsoft Authenticator, ajoutez un compte en scannant ce QR code.",
            Font = Theme.F(10f),
            ForeColor = Theme.Text,
            Wrap = true
        });

        Primary.Click += async (_, _) => await ActivateAsync();
        Shown += (_, _) => _code.Focus();
    }

    private async Task ActivateAsync()
    {
        if (_recoveryCodes.Count > 0)
        {
            DialogResult = DialogResult.OK;
            return;
        }

        string code = _code.Text.Trim();
        if (code.Length == 0)
        {
            ShowError("Saisissez le code à 6 chiffres affiché par l'application.");
            _code.Focus();
            return;
        }

        ShowError(null);
        TwoFactorEnabledDto? result = null;
        bool ok = await UiRun.RunAsync(this, async () => result = await _api.EnableTwoFactorAsync(code), "Activation impossible");
        if (ok && result is not null)
        {
            ShowRecoveryCodes(result.RecoveryCodes);
        }
        else if (!IsDisposed)
        {
            _code.SelectAll();
            _code.Focus();
        }
    }

    /// <summary>Second écran : les codes de secours, affichés une seule fois.</summary>
    private void ShowRecoveryCodes(IReadOnlyList<string> codes)
    {
        _recoveryCodes = codes;
        foreach (Control c in Body.Controls.Cast<Control>().ToList())
        {
            Body.Controls.Remove(c);
            c.Dispose();
        }

        Text = "Double authentification activée";
        Primary.Text = "J'ai noté mes codes";
        Secondary.Visible = false;
        FlatButton copy = AddExtraButton("Copier les codes", ButtonKind.Secondary);
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, _recoveryCodes));
                ShowError(null);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                ShowError("Copie impossible : recopiez les codes à la main.");
            }
        };

        string rows = string.Join(Environment.NewLine, Enumerable.Range(0, (codes.Count + 1) / 2)
            .Select(i => codes[i * 2] + (i * 2 + 1 < codes.Count ? "     " + codes[i * 2 + 1] : string.Empty)));
        Body.Controls.Add(new TextLabel
        {
            Text = "Chaque code ne sert qu'une fois, si vous perdez votre téléphone. Ils ne seront plus affichés : rangez-les en lieu sûr.",
            Font = Theme.F(9.5f),
            ForeColor = Theme.Amber,
            Wrap = true
        });
        Body.Controls.Add(new CodesPanel(rows));
        Body.Controls.Add(new TextLabel
        {
            Text = "La double authentification est activée. Vos 8 codes de secours :",
            Font = Theme.F(10f),
            ForeColor = Theme.Text,
            Wrap = true
        });
        FitToContent();
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (int i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }

    private static Image RenderQr(string uri)
    {
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        using PngByteQRCode png = new(data);
        using MemoryStream stream = new(png.GetGraphic(8));
        using Image raw = Image.FromStream(stream);
        return new Bitmap(raw);
    }

    /// <summary>QR code centré sur fond blanc (les applications le lisent mal sur fond sombre).</summary>
    private sealed class QrHolder : Control
    {
        private readonly Image _image;

        public QrHolder(Image image)
        {
            _image = image;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = Theme.S(200);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.BackOf(this));
            int side = Theme.S(192);
            Rectangle target = new((Width - side) / 2, (Height - side) / 2, side, side);
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            e.Graphics.FillRectangle(Brushes.White, target);
            e.Graphics.DrawImage(_image, target);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _image.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Les 8 codes de secours, en police à chasse fixe, dans un cadre.</summary>
    private sealed class CodesPanel : Control
    {
        private readonly string _text;

        public CodesPanel(string text)
        {
            _text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = Theme.S(150);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.BackOf(this));
            Rectangle box = new(0, 0, Width - 1, Height - 1);
            using System.Drawing.Drawing2D.GraphicsPath path = Theme.RoundedRect(box, Theme.S(10));
            using SolidBrush fill = new(Theme.SurfaceAlt);
            using Pen border = new(Theme.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            using Font mono = new("Consolas", Theme.F(12f).Size, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, _text, mono, Rectangle.Inflate(box, -Theme.S(20), -Theme.S(14)), Theme.Figure,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>Désactivation de la double authentification : mot de passe et code (application ou secours).</summary>
public sealed class TwoFactorDisableDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly InputBox _password = new() { PasswordChar = '•', IconGlyph = Icons.Key, PlaceholderText = "Votre mot de passe" };
    private readonly InputBox _code = new() { PlaceholderText = "Code à 6 chiffres ou code de secours", IconGlyph = Icons.Key };

    public TwoFactorDisableDialog(PressingApiClient api)
        : base("Désactiver la double authentification", "Confirmez avec votre mot de passe et un code.", 480, 380)
    {
        _api = api;
        Primary.Text = "Désactiver";
        Primary.Kind = ButtonKind.Danger;
        FormGrid grid = new(1);
        grid.AddField("Mot de passe *", _password);
        grid.AddField("Code *", _code);
        Body.Controls.Add(grid);
        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _password.Focus();
    }

    private async Task SubmitAsync()
    {
        if (_password.Text.Length == 0 || _code.Text.Trim().Length == 0)
        {
            ShowError("Saisissez votre mot de passe et un code.");
            return;
        }

        ShowError(null);
        bool ok = await UiRun.RunAsync(this, () => _api.DisableTwoFactorAsync(_password.Text, _code.Text.Trim()), "Désactivation impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }
}
