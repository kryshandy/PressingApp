using System.Globalization;
using System.Net;
using System.Text;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using Pressing.Core.Services;

namespace Pressing.Core.Email;

public record EmailContent(string Subject, string Html, string Text);

public class EmailTemplateService
{
    private readonly SettingsService _settings;
    private readonly EmailOptions _emailOptions;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public EmailTemplateService(SettingsService settings, EmailOptions emailOptions)
    {
        _settings = settings;
        _emailOptions = emailOptions;
    }

    public async Task<EmailContent> OrderConfirmationAsync(Order order, CancellationToken ct = default)
    {
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        string items = BuildItemsTable(order, shop.Currency);
        string body = $"""
            <p style="margin:0 0 14px">Bonjour <strong>{E(order.Client.FirstName)}</strong>,</p>
            <p style="margin:0 0 18px">Nous avons bien reçu votre dépôt. Votre commande est enregistrée sous le numéro :</p>
            <div style="background:#eef2ff;border:1px solid #c7d2fe;border-radius:14px;padding:16px;text-align:center;margin:0 0 20px">
              <div style="font-size:12px;color:#6366f1;text-transform:uppercase;letter-spacing:1px">Votre ticket</div>
              <div style="font-size:24px;font-weight:800;color:#312e81;margin-top:4px">{E(order.TicketNumber)}</div>
              <div style="font-size:13px;color:#4f46e5;margin-top:6px">Retrait prévu le {order.DueDate:dddd dd MMMM yyyy}</div>
            </div>
            {items}
            <p style="margin:18px 0 0">Total : <strong>{Money(order.Total, shop.Currency)}</strong> — Acompte : <strong>{Money(order.PaidAmount, shop.Currency)}</strong></p>
            <p style="margin:6px 0 0">Solde à régler : <strong style="color:{((order.Balance > 0) ? "#b45309" : "#15803d")}">{Money(order.Balance, shop.Currency)}</strong></p>
            <p style="margin:20px 0 0;color:#64748b;font-size:13px">Conservez ce ticket. Vous pouvez suivre votre commande depuis votre espace client.</p>
            """;

        return new EmailContent(
            $"Dépôt confirmé — {order.TicketNumber}",
            Layout(shop, "Votre dépôt est enregistré", body, "Voir ma commande", $"{_emailOptions.AppBaseUrl}/compte/commandes/{order.Id}"),
            $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} est enregistrée. Retrait prévu le {order.DueDate:dd/MM/yyyy}. Total {Money(order.Total, shop.Currency)}, solde {Money(order.Balance, shop.Currency)}.");
    }

    public async Task<EmailContent> OrderReadyAsync(Order order, CancellationToken ct = default)
    {
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        string body = $"""
            <p style="margin:0 0 14px">Bonjour <strong>{E(order.Client.FirstName)}</strong>,</p>
            <p style="margin:0 0 18px">Bonne nouvelle : votre commande est prête à être retirée.</p>
            <div style="background:#ecfdf5;border:1px solid #a7f3d0;border-radius:14px;padding:18px;margin:0 0 20px">
              <div style="font-size:22px;font-weight:800;color:#065f46">{E(order.TicketNumber)}</div>
              <div style="font-size:13px;color:#047857;margin-top:6px">Solde à régler : {Money(order.Balance, shop.Currency)}</div>
            </div>
            <p style="margin:0 0 8px"><strong>Adresse :</strong> {E(shop.Address)}, {E(shop.City)}</p>
            <p style="margin:0 0 8px"><strong>Horaires :</strong> {E(shop.OpeningHours)}</p>
            <p style="margin:18px 0 0;color:#64748b;font-size:13px">Merci de présenter votre ticket ou votre numéro de téléphone lors du retrait.</p>
            """;

        return new EmailContent(
            $"Vos articles sont prêts — {order.TicketNumber}",
            Layout(shop, "Votre commande est prête", body, "Voir ma commande", $"{_emailOptions.AppBaseUrl}/compte/commandes/{order.Id}"),
            $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} est prête. Solde : {Money(order.Balance, shop.Currency)}.");
    }

    public async Task<EmailContent> ReceiptAsync(Order order, CancellationToken ct = default)
    {
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        string body = $"""
            <p style="margin:0 0 14px">Bonjour <strong>{E(order.Client.FirstName)}</strong>,</p>
            <p style="margin:0 0 18px">Voici le récapitulatif de votre commande <strong>{E(order.TicketNumber)}</strong>.</p>
            {BuildItemsTable(order, shop.Currency)}
            <p style="margin:18px 0 0">Total : <strong>{Money(order.Total, shop.Currency)}</strong> — Payé : <strong>{Money(order.PaidAmount, shop.Currency)}</strong> — Solde : <strong>{Money(order.Balance, shop.Currency)}</strong></p>
            """;

        return new EmailContent(
            $"Votre reçu — {order.TicketNumber}",
            Layout(shop, "Votre reçu", body, "Télécharger le reçu PDF", $"{_emailOptions.AppBaseUrl}/compte/commandes/{order.Id}/recu"),
            $"Reçu de la commande {order.TicketNumber}. Total {Money(order.Total, shop.Currency)}, solde {Money(order.Balance, shop.Currency)}.");
    }

    public async Task<EmailContent> WelcomeAsync(Client client, string? temporaryPassword, CancellationToken ct = default)
    {
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        string passwordBlock = string.IsNullOrWhiteSpace(temporaryPassword)
            ? string.Empty
            : $"""
              <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:14px;padding:16px;margin:0 0 20px">
                <div style="font-size:12px;color:#92400e;text-transform:uppercase;letter-spacing:1px">Mot de passe temporaire</div>
                <div style="font-size:20px;font-weight:800;color:#78350f;margin-top:4px">{E(temporaryPassword)}</div>
                <div style="font-size:12px;color:#92400e;margin-top:6px">Changez-le dès votre première connexion.</div>
              </div>
              """;

        string body = $"""
            <p style="margin:0 0 14px">Bonjour <strong>{E(client.FirstName)}</strong>,</p>
            <p style="margin:0 0 18px">Votre espace client Pressing est activé. Vous pouvez y consulter vos commandes, suivre leur avancement et télécharger vos reçus.</p>
            {passwordBlock}
            <p style="margin:0 0 8px"><strong>Identifiant :</strong> {E(client.Phone)} ou {E(client.Email ?? "votre e-mail")}</p>
            <p style="margin:18px 0 0;color:#64748b;font-size:13px">Pour votre sécurité, ne partagez jamais votre mot de passe.</p>
            """;

        return new EmailContent(
            "Votre espace client est activé",
            Layout(shop, "Bienvenue chez " + shop.Name, body, "Me connecter", $"{_emailOptions.AppBaseUrl}/compte/connexion"),
            $"Bonjour {client.FirstName}, votre espace client est activé. Connectez-vous sur {_emailOptions.AppBaseUrl}/compte/connexion.");
    }

    public async Task<EmailContent> PasswordResetAsync(Client client, string token, CancellationToken ct = default)
    {
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        string link = $"{_emailOptions.AppBaseUrl}/compte/reinitialiser?token={Uri.EscapeDataString(token)}";
        string body = $"""
            <p style="margin:0 0 14px">Bonjour <strong>{E(client.FirstName)}</strong>,</p>
            <p style="margin:0 0 18px">Vous avez demandé la réinitialisation de votre mot de passe. Ce lien est valable pendant 2 heures.</p>
            <p style="margin:0 0 18px;text-align:center">
              <a href="{E(link)}" style="display:inline-block;background:#4f46e5;color:#fff;text-decoration:none;padding:13px 26px;border-radius:10px;font-weight:700">Réinitialiser mon mot de passe</a>
            </p>
            <p style="margin:0 0 8px;color:#64748b;font-size:13px">Si vous n'êtes pas à l'origine de cette demande, ignorez simplement cet e-mail.</p>
            """;

        return new EmailContent(
            "Réinitialisation de votre mot de passe",
            Layout(shop, "Mot de passe oublié", body, null, null),
            $"Réinitialisez votre mot de passe : {link}");
    }

    public async Task<EmailContent> OrderCancelledAsync(Order order, string reason, CancellationToken ct = default)
    {
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        string body = $"""
            <p style="margin:0 0 14px">Bonjour <strong>{E(order.Client.FirstName)}</strong>,</p>
            <p style="margin:0 0 18px">Votre commande <strong>{E(order.TicketNumber)}</strong> a été annulée.</p>
            <p style="margin:0 0 8px"><strong>Motif :</strong> {E(reason)}</p>
            <p style="margin:18px 0 0;color:#64748b;font-size:13px">Pour toute question, contactez-nous au {E(shop.Phone)}.</p>
            """;

        return new EmailContent(
            $"Commande annulée — {order.TicketNumber}",
            Layout(shop, "Commande annulée", body, null, null),
            $"Votre commande {order.TicketNumber} a été annulée. Motif : {reason}.");
    }

    public async Task<EmailContent> GenericAsync(string subject, string message, CancellationToken ct = default)
    {
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        string body = $"<p style=\"margin:0;white-space:pre-wrap\">{E(message)}</p>";
        return new EmailContent(
            subject,
            Layout(shop, subject, body, null, null),
            message);
    }

    private static string Layout(ShopInfo shop, string title, string body, string? buttonLabel, string? buttonUrl)
    {
        string button = string.IsNullOrWhiteSpace(buttonLabel) || string.IsNullOrWhiteSpace(buttonUrl)
            ? string.Empty
            : $"""
              <div style="text-align:center;margin:24px 0 8px">
                <a href="{E(buttonUrl)}" style="display:inline-block;background:#4f46e5;color:#ffffff;text-decoration:none;padding:13px 26px;border-radius:10px;font-weight:700;font-size:14px">{E(buttonLabel)}</a>
              </div>
              """;

        return $"""
            <!doctype html>
            <html lang="fr">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f1f5f9;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:#1e293b">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f1f5f9;padding:28px 12px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:620px;background:#ffffff;border-radius:18px;overflow:hidden;box-shadow:0 18px 45px rgba(15,23,42,.08)">
                    <tr>
                      <td style="background:linear-gradient(135deg,#312e81,#4f46e5 55%,#0ea5e9);padding:30px 32px;color:#fff">
                        <div style="font-size:12px;letter-spacing:2px;text-transform:uppercase;opacity:.8">{E(shop.Name)}</div>
                        <div style="font-size:26px;font-weight:800;margin-top:6px;line-height:1.2">{E(title)}</div>
                        <div style="font-size:13px;opacity:.85;margin-top:8px">{E(shop.Slogan)}</div>
                      </td>
                    </tr>
                    <tr><td style="padding:30px 32px;font-size:14px;line-height:1.65">{body}{button}</td></tr>
                    <tr>
                      <td style="background:#0f172a;color:#cbd5e1;padding:22px 32px;font-size:12px;line-height:1.7">
                        <strong style="color:#fff">{E(shop.Name)}</strong><br>
                        {E(shop.Address)}, {E(shop.City)}<br>
                        {E(shop.Phone)} · {E(shop.Email)}<br>
                        <span style="color:#94a3b8">{E(shop.ReceiptFooter)}</span>
                      </td>
                    </tr>
                  </table>
                  <div style="max-width:620px;margin:14px auto 0;font-size:11px;color:#94a3b8;text-align:center">E-mail automatique — merci de ne pas répondre directement.</div>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string BuildItemsTable(Order order, string currency)
    {
        var rows = new StringBuilder();
        foreach (OrderItem item in order.Items)
        {
            rows.Append($"""
                <tr>
                  <td style="padding:10px 8px;border-bottom:1px solid #e2e8f0">{E(item.Category.Name)}<div style="font-size:11px;color:#94a3b8">{E(item.ServiceType.Label())}{ (item.IsExpress ? " · Express" : "") }</div></td>
                  <td style="padding:10px 8px;border-bottom:1px solid #e2e8f0;text-align:center">{item.Quantity}</td>
                  <td style="padding:10px 8px;border-bottom:1px solid #e2e8f0;text-align:right">{Money(item.LineTotal, currency)}</td>
                </tr>
                """);
        }

        return $"""
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="border-collapse:collapse;font-size:13px">
              <thead><tr style="background:#f8fafc;color:#64748b;font-size:11px;text-transform:uppercase;letter-spacing:.6px">
                <th align="left" style="padding:10px 8px">Article</th>
                <th align="center" style="padding:10px 8px">Qté</th>
                <th align="right" style="padding:10px 8px">Montant</th>
              </tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """;
    }

    private static string Money(decimal value, string currency) =>
        value.ToString("N0", French) + " " + currency;

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
