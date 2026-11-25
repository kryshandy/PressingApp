using Pressing.Client;
using Pressing.Core.Dtos;
using Pressing.Desktop.Ui;
using Pressing.Desktop.Views;

namespace Pressing.Desktop.Services;

/// <summary>Services offerts par la coque principale aux vues (navigation, session, notifications).</summary>
public interface IAppHost
{
    AppSession Session { get; }

    PressingApiClient Api { get; }

    /// <summary>Fenêtre principale (propriétaire des boîtes de dialogue).</summary>
    Form Window { get; }

    /// <summary>Affiche la vue enregistrée sous cette clé (<see cref="NavRegistry"/>) ; <paramref name="parameter"/> est transmis à <see cref="BaseView.ApplyParameter"/>.</summary>
    void Navigate(string key, object? parameter = null);

    void OpenOrder(int orderId);

    void OpenClient(int clientId);

    /// <summary>Affiche une vue secondaire (détail…) en conservant l'historique pour « Retour ».</summary>
    void OpenView(string navKey, Func<BaseView> factory);

    void GoBack();

    /// <summary>Ouvre le dépôt, éventuellement avec un client déjà choisi.</summary>
    void StartNewOrder(ClientDto? client = null);

    /// <summary>Recharge le compteur de notifications.</summary>
    Task RefreshBadgesAsync();

    /// <summary>Message discret en bas à droite de la fenêtre.</summary>
    void Toast(string message, MessageKind kind = MessageKind.Success);
}
