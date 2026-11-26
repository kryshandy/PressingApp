using Pressing.Core;
using Pressing.Desktop.Ui;
using Pressing.Desktop.Views;

namespace Pressing.Desktop.Services;

/// <summary>Une entrée de la barre latérale et la fabrique de son écran.</summary>
public sealed record NavItem(
    string Key,
    string Title,
    string Glyph,
    string Section,
    Func<IAppHost, BaseView> Create,
    Func<UserRole, bool> IsAllowed,
    string? Shortcut = null);

/// <summary>
/// <b>Registre de navigation</b> : pour ajouter un écran, créez un <see cref="BaseView"/> puis ajoutez une ligne
/// à <see cref="Items"/> (clé, titre, icône, section, fabrique, rôles autorisés). La barre latérale, le contrôle
/// d'accès par rôle et la navigation s'en servent automatiquement.
/// </summary>
public static class NavRegistry
{
    public const string Dashboard = "dashboard";
    public const string NewOrder = "new-order";
    public const string Orders = "orders";
    public const string Clients = "clients";
    public const string Pickups = "pickups";
    public const string Cash = "cash";
    public const string Unpaid = "unpaid";
    public const string Tariffs = "tariffs";
    public const string Promotions = "promotions";
    public const string Reports = "reports";
    public const string Staff = "staff";
    public const string Settings = "settings";
    public const string Account = "account";

    public static readonly Func<UserRole, bool> Everyone = _ => true;
    public static readonly Func<UserRole, bool> ManagerOrAdmin = r => r is UserRole.Admin or UserRole.Manager;
    public static readonly Func<UserRole, bool> AdminOnly = r => r == UserRole.Admin;

    /// <summary>
    /// Toutes les entrées, dans l'ordre d'affichage. 
    /// </summary>
    public static readonly IReadOnlyList<NavItem> Items = new List<NavItem>
    {
        new(Dashboard, "Tableau de bord", Icons.Dashboard, "Quotidien", h => new DashboardView(h), Everyone),
        new(NewOrder, "Nouveau dépôt", Icons.Add, "Quotidien", h => new NewOrderView(h), Everyone, "F2"),
        new(Orders, "Commandes", Icons.Orders, "Quotidien", h => new OrdersView(h), Everyone),
        new(Clients, "Clients", Icons.Clients, "Quotidien", h => new ClientsView(h), Everyone),
        new(Pickups, "Retraits", Icons.Pickup, "Quotidien", h => new PickupView(h), Everyone),
        new(Cash, "Caisse", Icons.Cash, "Finances", h => new CashView(h), Everyone),
        new(Unpaid, "Impayés", Icons.Unpaid, "Finances", h => new OutstandingView(h), Everyone),
        new(Tariffs, "Tarifs", Icons.Tariffs, "Finances", h => new TariffsView(h), Everyone),
        new(Promotions, "Promotions", Icons.Promotions, "Administration",
            h => new PromotionsView(h), ManagerOrAdmin),
        new(Reports, "Rapports", Icons.Reports, "Administration",
            h => new ReportsView(h), ManagerOrAdmin),
        new(Staff, "Personnel", Icons.Staff, "Administration",
            h => new StaffView(h), AdminOnly),
        new(Settings, "Paramètres", Icons.Settings, "Administration",
            h => new SettingsView(h), ManagerOrAdmin),
        new(Account, "Mon compte", Icons.Account, "Compte", h => new AccountView(h), Everyone)
    };

    public static NavItem? Find(string key) => Items.FirstOrDefault(i => i.Key == key);

    /// <summary>Clés dont l'écran doit rester actif dans le menu quand une vue de détail est ouverte.</summary>
    public static string MenuKeyFor(string key) => key;
}
