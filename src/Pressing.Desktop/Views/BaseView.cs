using Pressing.Client;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>
/// Classe de base de tous les écrans (UserControl chargé dans la zone de contenu de <c>FrmMain</c>).
/// Une vue reçoit <see cref="IAppHost"/>, construit son interface dans son constructeur et charge ses données dans
/// <see cref="LoadAsync"/> (appelée par la coque après l'affichage).
/// </summary>
public abstract class BaseView : UserControl
{
    protected BaseView(IAppHost host)
    {
        Host = host;
        AutoScaleMode = AutoScaleMode.None;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Padding = Padding.Empty;
        DoubleBuffered = true;
    }

    protected IAppHost Host { get; }

    protected PressingApiClient Api => Host.Api;

    protected AppSession Session => Host.Session;

    /// <summary>Titre court (fenêtre / fil d'Ariane).</summary>
    public abstract string Title { get; }

    /// <summary>Si vrai, l'instance est conservée quand on quitte l'écran (ex. dépôt en cours de saisie).</summary>
    public virtual bool KeepAlive => false;

    /// <summary>Transmet un paramètre de navigation (filtre, texte de recherche…) avant le premier chargement.</summary>
    public virtual void ApplyParameter(object? parameter)
    {
    }

    /// <summary>Charge les données (appelée une fois après la création).</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>Recharge (F5, bouton Actualiser). Par défaut identique à <see cref="LoadAsync"/>.</summary>
    public virtual Task RefreshAsync() => LoadAsync();

    /// <summary>Appelée quand une vue conservée (<see cref="KeepAlive"/>) redevient visible.</summary>
    public virtual Task ReactivateAsync() => Task.CompletedTask;

    /// <summary>Raccourci F-clavier propre à la vue ; retourne vrai si traité.</summary>
    public virtual bool HandleShortcut(Keys keyData) => false;

    /// <summary>
    /// Installe le gabarit de page standard (DESIGN.md) : en-tête, barre de filtres facultative et corps
    /// (rempli ou défilant). Retourne le <see cref="PageLayout"/> créé.
    /// </summary>
    protected PageLayout SetPage(PageHeader? header, Control body, bool scroll = false, Control? toolbar = null)
    {
        PageLayout page = new(header, body, scroll, toolbar);
        Controls.Add(page);
        return page;
    }

    /// <summary>Exécute une opération réseau avec état occupé et gestion d'erreurs.</summary>
    protected Task<bool> RunAsync(Func<Task> action, string? context = null) => UiRun.RunAsync(this, action, context);

    /// <summary>Vrai tant que la vue est affichée (à tester après chaque <c>await</c>).</summary>
    protected bool Alive => !IsDisposed && !Disposing;
}
