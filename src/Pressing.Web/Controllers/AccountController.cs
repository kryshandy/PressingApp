using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Web.Infrastructure;
using Pressing.Web.Models;

namespace Pressing.Web.Controllers;

[Authorize]
[Route("compte")]
public sealed class AccountController : Controller
{
    private readonly PortalApi _portal;

    public AccountController(PortalApi portal) => _portal = portal;

    private PressingApiClient Api => _portal.Api;

    /// <summary>Erreur « métier » affichable dans le formulaire (hors session expirée et service indisponible).</summary>
    private static bool IsFormError(ApiException ex) =>
        !ex.IsUnauthorized && ex.StatusCode != HttpStatusCode.ServiceUnavailable;

    // ---------------------------------------------------------------- Authentification

    [AllowAnonymous]
    [HttpGet("connexion")]
    public IActionResult Login(string? returnUrl, string? expiree)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturn(returnUrl));
        if (expiree == "1") ViewData["Info"] = "Votre session a expiré. Reconnectez-vous pour continuer.";
        return View(RetryForm.Restore(TempData, new LoginForm { ReturnUrl = returnUrl }));
    }

    [AllowAnonymous]
    [HttpPost("connexion")]
    public async Task<IActionResult> Login(LoginForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var auth = await Api.LoginClientAsync(form.Identifier.Trim(), form.Password, ct);
                if (auth?.Client is null) throw new ApiException(HttpStatusCode.Unauthorized, "Identifiants incorrects.");
                await AuthClaims.SignInAsync(HttpContext, auth.Client, auth.Token, auth.ExpiresAt, form.Remember);
                return LocalRedirect(SafeReturn(form.ReturnUrl));
            }
            catch (ApiException ex) when (IsFormError(ex) || ex.IsUnauthorized)
            {
                ModelState.AddModelError("", ex.IsUnauthorized && string.IsNullOrWhiteSpace(ex.Message)
                    ? "Identifiants incorrects." : ex.Message);
            }
        }
        form.Password = "";
        return View(form);
    }

    [HttpPost("deconnexion")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Success"] = "Vous êtes déconnecté. À très bientôt !";
        return Redirect("/");
    }

    [AllowAnonymous]
    [HttpGet("mot-de-passe-oublie")]
    public IActionResult Forgot() => View(RetryForm.Restore(TempData, new ForgotForm()));

    [AllowAnonymous]
    [HttpPost("mot-de-passe-oublie")]
    public async Task<IActionResult> Forgot(ForgotForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var r = await Api.ForgotPasswordAsync(form.Identifier.Trim(), ct);
                ViewData["Sent"] = r?.Message
                    ?? "Si un compte correspond à cet identifiant, un e-mail de réinitialisation vient d'être envoyé.";
            }
            catch (ApiException ex) when (IsFormError(ex))
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        return View(form);
    }
    [AllowAnonymous]
    [HttpGet("reinitialiser")]
    public IActionResult Reset(string? token) => View(RetryForm.Restore(TempData, new ResetForm { Token = token ?? "" }));

    [AllowAnonymous]
    [HttpPost("reinitialiser")]
    public async Task<IActionResult> Reset(ResetForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var r = await Api.ResetPasswordAsync(form.Token, form.NewPassword, ct);
                TempData["Success"] = r?.Message ?? "Votre mot de passe a été modifié. Vous pouvez vous connecter.";
                return RedirectToAction(nameof(Login));
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                TempData["Error"] = "Le service est temporairement indisponible. Réessayez dans quelques instants.";
                return RedirectToAction(nameof(Reset), new { token = form.Token });
            }
            catch (ApiException ex) when (IsFormError(ex))
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        form.NewPassword = form.ConfirmPassword = "";
        return View(form);
    }


    // ---------------------------------------------------------------- Tableau de bord, commandes

    [HttpGet("")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var data = await Api.GetPortalDashboardAsync(ct) ?? throw EmptyResponse();
        return View(new DashboardViewModel(data));
    }

    [HttpGet("commandes")]
    public async Task<IActionResult> Orders(string? filtre, CancellationToken ct)
    {
        var all = await Api.GetPortalOrdersAsync(ct) ?? [];
        var active = all.Where(o => Ui.IsActive(o.Status)).ToList();
        var done = all.Where(o => !Ui.IsActive(o.Status)).ToList();

        var filter = filtre switch { "en-cours" => "en-cours", "terminees" => "terminees", _ => "toutes" };
        IEnumerable<OrderSummaryDto> items = filter switch
        {
            "en-cours" => active,
            "terminees" => done,
            _ => all
        };
        return View(new OrdersViewModel(filter, items.OrderByDescending(o => o.CreatedAt).ToList(),
            all.Count, active.Count, done.Count));
    }

    [HttpGet("commandes/{id:int}")]
    public async Task<IActionResult> Order(int id, CancellationToken ct)
    {
        var order = await Api.GetPortalOrderAsync(id, ct);
        return order is null ? NotFound() : View(new OrderDetailViewModel(order));
    }

    [HttpGet("commandes/{id:int}/recu")]
    public async Task<IActionResult> Receipt(int id, CancellationToken ct)
    {
        var order = await Api.GetPortalOrderAsync(id, ct);
        if (order is null) return NotFound();
        var pdf = await Api.GetPortalReceiptPdfAsync(id, ct);
        return File(pdf, "application/pdf", $"recu-{order.TicketNumber}.pdf");
    }

    // ---------------------------------------------------------------- Profil, mot de passe, fidélité

    [HttpGet("profil")]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var c = await Api.GetProfileAsync(ct) ?? throw EmptyResponse();
        return View(RetryForm.Restore(TempData, new ProfileForm
        {
            FirstName = c.FirstName, LastName = c.LastName, Phone = c.Phone,
            WhatsApp = c.WhatsApp, Email = c.Email, Address = c.Address,
            NotificationPreference = c.NotificationPreference,
            Consent = c.NotificationPreference != ClientNotificationPreference.None &&
                      c.ConsentGivenAt.HasValue && !c.ConsentRevokedAt.HasValue
        }));
    }

    [HttpPost("profil")]
    public async Task<IActionResult> Profile(ProfileForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var updated = await Api.UpdateProfileAsync(new UpdateProfileRequest(
                    form.FirstName.Trim(), form.LastName.Trim(), form.Phone.Trim(), form.WhatsApp.Trim(),
                    Blank(form.Email), Blank(form.Address), form.NotificationPreference), ct) ?? throw EmptyResponse();

                // Le nom affiché vient du cookie : on le réémet avec le même jeton.
                var expires = DateTime.TryParse(User.FindFirst(AuthClaims.TokenExpires)?.Value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var e) ? e : DateTime.UtcNow.AddHours(1);
                var persistent = (await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme))
                    .Properties?.IsPersistent ?? true;
                await AuthClaims.SignInAsync(HttpContext, updated, User.Token() ?? "", expires, persistent);

                TempData["Success"] = "Vos informations ont bien été enregistrées.";
                return RedirectToAction(nameof(Profile));
            }
            catch (ApiException ex) when (IsFormError(ex))
            {
                ModelState.AddModelError("", ex.Message);
            }
        }
        return View(form);
    }

    [HttpGet("mot-de-passe")]
    public IActionResult Password() => View(RetryForm.Restore(TempData, new ChangePasswordForm()));

    [HttpPost("mot-de-passe")]
    public async Task<IActionResult> Password(ChangePasswordForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var r = await Api.ChangeClientPasswordAsync(form.CurrentPassword, form.NewPassword, ct);
                TempData["Success"] = r?.Message ?? "Votre mot de passe a été modifié.";
                return RedirectToAction(nameof(Password));
            }
            catch (ApiException ex) when (IsFormError(ex))
            {
                ModelState.AddModelError("", ex.Message);
            }
        }
        return View(new ChangePasswordForm());
    }

    [HttpGet("fidelite")]
    public async Task<IActionResult> Loyalty(CancellationToken ct)
    {
        var client = await Api.GetProfileAsync(ct) ?? throw EmptyResponse();
        var tx = await Api.GetPortalLoyaltyAsync(ct) ?? [];
        return View(new LoyaltyViewModel(client, tx.OrderByDescending(t => t.CreatedAt).ToList()));
    }

    // ---------------------------------------------------------------- Utilitaires

    private string SafeReturn(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/compte";

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ApiException EmptyResponse() =>
        new(HttpStatusCode.BadGateway, "Le serveur a renvoyé une réponse vide. Réessayez dans un instant.");
}
