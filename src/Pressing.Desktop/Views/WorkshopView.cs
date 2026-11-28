using System.Drawing.Drawing2D;
using System.Media;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>
/// Atelier : un grand champ toujours actif reçoit la douchette (clavier + Entrée) ou une saisie manuelle ;
/// chaque code d'étiquette fait avancer l'article d'une étape (Déposé → En traitement → Prêt).
/// Le dernier article s'affiche en très grand, avec le statut en couleur, et un son confirme ou refuse le scan.
/// </summary>
public sealed class WorkshopView : BaseView
{
    private const int HistoryLimit = 40;

    /// <summary>Un scan terminé (accepté ou refusé), pour l'historique et l'annulation.</summary>
    private sealed class ScanEntry : IPillRow
    {
        public ScanEntry(string code, WorkshopScanResultDto? result, string? error)
        {
            Code = code;
            Result = result;
            Error = error;
            Time = DateTime.Now.ToString("HH:mm:ss");
            Ticket = result?.TicketNumber ?? string.Empty;
            Client = result?.ClientName ?? string.Empty;
            Article = result is null ? code : $"{result.ItemName} ({result.Rank}/{result.ItemCount})";
            Detail = result is null ? error ?? string.Empty : result.Message;
        }

        public string Code { get; }
        public WorkshopScanResultDto? Result { get; }
        public string? Error { get; }
        public string Time { get; }
        public string Ticket { get; }
        public string Client { get; }
        public string Article { get; }
        public string Detail { get; }

        public (string Text, Color Color)? GetPill(string columnName) => columnName != "status"
            ? null
            : Result is null
                ? ("Refusé", Theme.Red)
                : (Result.NewStatusLabel, Theme.StatusColor(Result.NewStatus));
    }

    /// <summary>Affichage grand format du dernier scan, lisible à plusieurs mètres.</summary>
    private sealed class ScanDisplay : Control
    {
        private ScanEntry? _entry;

        public ScanDisplay()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Height = Theme.S(184);
        }

        public ScanEntry? Entry
        {
            get => _entry;
            set { _entry = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new(0, 0, Width - 1, Height - 1);
            using GraphicsPath path = Theme.RoundedRect(rect, Theme.S(12));
            using (SolidBrush fill = new(Theme.Surface)) g.FillPath(fill, path);

            Color accent = _entry is null ? Theme.Slate : _entry.Result is null ? Theme.Red : Theme.StatusColor(_entry.Result.NewStatus);
            Region? previous = g.Clip;
            g.SetClip(path);
            using (SolidBrush band = new(accent)) g.FillRectangle(band, 0, 0, Theme.S(12), Height);
            g.Clip = previous;
            using (Pen border = new(Theme.Border)) g.DrawPath(border, path);

            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            int left = Theme.S(44);
            int right = Width - Theme.S(36);
            int width = right - left;

            if (_entry is null)
            {
                TextRenderer.DrawText(g, "En attente d'un scan…", Theme.F(26f, FontStyle.Bold),
                    new Rectangle(left, Theme.S(44), width, Theme.S(48)), Theme.Text, flags);
                TextRenderer.DrawText(g, "Passez la douchette sur l'étiquette d'un article, ou saisissez son code puis Entrée.",
                    Theme.F(12f), new Rectangle(left, Theme.S(98), width, Theme.S(28)), Theme.TextMuted, flags);
                return;
            }

            if (_entry.Result is null)
            {
                TextRenderer.DrawText(g, "SCAN REFUSÉ", Theme.F(10f, FontStyle.Bold),
                    new Rectangle(left, Theme.S(18), width, Theme.S(22)), Theme.Red, flags);
                TextRenderer.DrawText(g, _entry.Error ?? "Code refusé.", Theme.F(20f, FontStyle.Bold),
                    new Rectangle(left, Theme.S(46), width, Theme.S(80)), Theme.Figure,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, "Code lu : " + _entry.Code, Theme.F(12f),
                    new Rectangle(left, Theme.S(140), width, Theme.S(26)), Theme.TextMuted, flags);
                return;
            }

            WorkshopScanResultDto r = _entry.Result;
            int statusWidth = Math.Min(Theme.S(430), width / 2);
            int leftWidth = width - statusWidth - Theme.S(24);

            TextRenderer.DrawText(g, "DERNIER ARTICLE SCANNÉ", Theme.F(9f, FontStyle.Bold),
                new Rectangle(left, Theme.S(18), leftWidth, Theme.S(20)), Theme.TextMuted, flags);
            TextRenderer.DrawText(g, r.ClientName, Theme.F(18f, FontStyle.Bold),
                new Rectangle(left, Theme.S(42), leftWidth, Theme.S(34)), Theme.Text, flags);
            TextRenderer.DrawText(g, r.ItemName, Theme.F(32f, FontStyle.Bold),
                new Rectangle(left, Theme.S(78), leftWidth, Theme.S(56)), Theme.Figure, flags);
            TextRenderer.DrawText(g, $"{r.TicketNumber} · article {r.Rank}/{r.ItemCount} · {r.ServiceLabel}", Theme.F(12f),
                new Rectangle(left, Theme.S(140), leftWidth, Theme.S(26)), Theme.TextMuted, flags);

            int statusLeft = right - statusWidth;
            const TextFormatFlags rightFlags = flags | TextFormatFlags.Right;
            TextRenderer.DrawText(g, "NOUVEAU STATUT", Theme.F(9f, FontStyle.Bold),
                new Rectangle(statusLeft, Theme.S(18), statusWidth, Theme.S(20)), Theme.TextMuted, rightFlags);
            TextRenderer.DrawText(g, r.NewStatusLabel.ToUpperInvariant(), Theme.F(32f, FontStyle.Bold),
                new Rectangle(statusLeft, Theme.S(62), statusWidth, Theme.S(56)), accent, rightFlags);
            string footnote = r.OrderReady ? "Commande complète : client prévenu" : "était : " + r.PreviousStatus.Label();
            TextRenderer.DrawText(g, footnote, Theme.F(12f, r.OrderReady ? FontStyle.Bold : FontStyle.Regular),
                new Rectangle(statusLeft, Theme.S(140), statusWidth, Theme.S(26)),
                r.OrderReady ? Theme.Green : Theme.TextMuted, rightFlags);
        }
    }

    private readonly InputBox _input = new()
    {
        IconGlyph = Icons.Search,
        PlaceholderText = "Scannez l'étiquette ou saisissez le code (ex. PRESS-2026-00012-01)",
        Height = Theme.S(56),
        Font = Theme.F(15f)
    };

    private readonly FlatButton _submit = new() { Text = "Valider", Kind = ButtonKind.Primary, IconGlyph = Icons.Check, AutoSize = false, Height = Theme.S(56) };
    private readonly ScanDisplay _display = new();
    private readonly GridCard _history = new("Aucun scan pour le moment.");
    private readonly FlatButton _undo;
    private readonly List<ScanEntry> _entries = new();
    private readonly Stack<ScanEntry> _undoable = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Windows.Forms.Timer _focusTimer = new() { Interval = 1500 };

    public WorkshopView(IAppHost host) : base(host)
    {
        PageHeader header = new("Atelier", "Scannez l'étiquette de chaque article pour le faire avancer d'une étape · la douchette USB saisit comme un clavier, le champ reste actif");
        _undo = header.AddAction("Annuler le dernier scan", ButtonKind.Secondary, Icons.Back, async (_, _) => await UndoLastAsync());
        _undo.Enabled = false;

        GridRow entry = new GridRow(56).Add(_input, 9).Add(_submit, 3);
        TitledCard scanCard = new("Scanner une étiquette", entry, null, autoHeight: true);

        ThemedGrid grid = _history.Grid;
        grid.AddText("time", "HEURE", nameof(ScanEntry.Time), fixedWidth: 96);
        grid.AddText("ticket", "TICKET", nameof(ScanEntry.Ticket), fixedWidth: 164);
        grid.AddText("client", "CLIENT", nameof(ScanEntry.Client), 25, minWidth: 130);
        grid.AddText("article", "ARTICLE", nameof(ScanEntry.Article), 22, minWidth: 140);
        grid.AddPill("status", "STATUT", fixedWidth: 148);
        grid.AddText("detail", "RÉSULTAT", nameof(ScanEntry.Detail), 53, minWidth: 200);
        grid.SetOptional("detail", "client", "ticket");
        TitledCard historyCard = new("Derniers scans", _history, $"Les {HistoryLimit} derniers, du plus récent au plus ancien", flushContent: true);

        VStack page = new VStack()
            .Add(scanCard)
            .Add(_display, StackSize.Fixed, 184)
            .Add(historyCard, StackSize.Fill);
        SetPage(header, page);

        _submit.Click += async (_, _) => await SubmitFromInputAsync();
        _input.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                await SubmitFromInputAsync();
            }
        };

        // Le champ garde le focus : une douchette « tape » au clavier, elle ne doit jamais tomber dans le vide.
        _focusTimer.Tick += (_, _) => KeepFocus();
        _focusTimer.Start();
        _history.Grid.Click += (_, _) => KeepFocus();
    }

    public override string Title => "Atelier";

    public override bool KeepAlive => true;

    public override Task LoadAsync()
    {
        _input.Focus();
        return Task.CompletedTask;
    }

    public override Task ReactivateAsync()
    {
        _input.Focus();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _focusTimer.Stop();
            _focusTimer.Dispose();
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }

    private void KeepFocus()
    {
        if (Alive && Visible && FindForm() is { ContainsFocus: true } && !_input.ContainsFocus && !AnyEditorFocused())
        {
            _input.Focus();
        }
    }

    /// <summary>Ne vole pas le focus à un champ de saisie d'une autre zone (recherche globale, par exemple).</summary>
    private bool AnyEditorFocused() => FindForm()?.ActiveControl is InputBox or TextBox;

    private async Task SubmitFromInputAsync()
    {
        string code = _input.Text.Trim();
        _input.Text = string.Empty;
        _input.Focus();
        if (code.Length == 0)
        {
            return;
        }

        await SubmitCodeAsync(code);
    }

    /// <summary>Envoie un code à l'API ; les scans rapprochés sont traités l'un après l'autre.</summary>
    public async Task SubmitCodeAsync(string code)
    {
        await _gate.WaitAsync();
        try
        {
            ScanEntry entry;
            try
            {
                WorkshopScanResultDto? result = await Api.WorkshopScanAsync(code);
                if (!Alive)
                {
                    return;
                }

                entry = new ScanEntry(code, result, result is null ? "Réponse vide du serveur." : null);
                if (result is not null)
                {
                    _undoable.Push(entry);
                }
            }
            catch (ApiException ex)
            {
                if (!Alive)
                {
                    return;
                }

                entry = new ScanEntry(code, null, ex.Message);
            }

            Show(entry);
            PlaySound(entry.Result is not null);
            if (entry.Result?.OrderReady == true)
            {
                Host.Toast($"Commande {entry.Result.TicketNumber} prête : le client est prévenu.");
                await Host.RefreshBadgesAsync();
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _gate.Release();
            }
        }
    }

    private async Task UndoLastAsync()
    {
        if (_undoable.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            ScanEntry last = _undoable.Peek();
            ScanEntry entry;
            try
            {
                WorkshopScanResultDto? result = await Api.WorkshopUndoAsync(last.Result!.Code, last.Result.NewStatus);
                if (!Alive)
                {
                    return;
                }

                _undoable.Pop();
                entry = new ScanEntry(last.Code, result, null);
                Host.Toast("Dernier scan annulé.", MessageKind.Info);
            }
            catch (ApiException ex)
            {
                if (!Alive)
                {
                    return;
                }

                _undoable.Clear();
                entry = new ScanEntry(last.Code, null, ex.Message);
            }

            Show(entry);
            PlaySound(entry.Result is not null);
        }
        finally
        {
            if (!IsDisposed)
            {
                _gate.Release();
            }
        }
    }

    private void Show(ScanEntry entry)
    {
        _display.Entry = entry;
        _entries.Insert(0, entry);
        if (_entries.Count > HistoryLimit)
        {
            _entries.RemoveRange(HistoryLimit, _entries.Count - HistoryLimit);
        }

        _history.Grid.SetRows(_entries.ToList());
        _history.UpdateEmpty();
        _undo.Enabled = _undoable.Count > 0;
        _input.Focus();
    }

    private static void PlaySound(bool success)
    {
        if (AppDialogs.Headless)
        {
            return;
        }

        try
        {
            (success ? SystemSounds.Asterisk : SystemSounds.Hand).Play();
        }
        catch (Exception)
        {
            // Pas de carte son : l'affichage couleur suffit.
        }
    }
}
