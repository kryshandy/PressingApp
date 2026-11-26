using System.Net;
using Microsoft.AspNetCore.Mvc;
using Pressing.Client;
using Pressing.Web.Infrastructure;
using Pressing.Web.Models;

namespace Pressing.Web.Controllers;

public sealed class HomeController : Controller
{
    private readonly PortalApi _portal;
    private readonly ShopDirectory _shop;
    private readonly ILogger<HomeController> _logger;

    public HomeController(PortalApi portal, ShopDirectory shop, ILogger<HomeController> logger)
    {
        _portal = portal;
        _shop = shop;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(new HomeViewModel(await _shop.GetAsync(ct)));

    [HttpGet("tarifs")]
    public async Task<IActionResult> Tarifs(CancellationToken ct) =>
        View(new TarifsViewModel(await _shop.GetAsync(ct)));

    [HttpGet("suivi")]
    public async Task<IActionResult> Suivi(string? ticket, string? phone, CancellationToken ct)
    {
        var vm = new SuiviViewModel { Ticket = (ticket ?? "").Trim(), Phone = (phone ?? "").Trim() };
        // Première ouverture : aucun paramètre dans l'URL. Une recherche avec des champs vides
        // envoie bien ?ticket=&phone= : ASP.NET lie alors ces valeurs vides à null, mais les clés existent.
        if (!Request.Query.ContainsKey("ticket") && !Request.Query.ContainsKey("phone")) return View(vm);

        if (string.IsNullOrEmpty(vm.Ticket) && string.IsNullOrEmpty(vm.Phone))
        {
            vm.Searched = true;
            vm.Error = SuiviViewModel.RequiredMessage;
            return View(vm);
        }

        vm.Searched = true;
        if (string.IsNullOrEmpty(vm.Ticket) || string.IsNullOrEmpty(vm.Phone))
        {
            vm.Error = SuiviViewModel.PartialMessage;
            return View(vm);
        }

        try
        {
            vm.Result = await _portal.Api.TrackOrderAsync(vm.Ticket.ToUpperInvariant(), vm.Phone, ct);
        }
        catch (ApiException ex) when (ex.StatusCode != HttpStatusCode.ServiceUnavailable && !ex.IsUnauthorized)
        {
            // Un 500 peut contenir un détail technique : on ne l'affiche pas, on le journalise seulement.
            vm.Error = (int)ex.StatusCode >= 500
                ? "Un incident imprévu s'est produit de notre côté. Réessayez dans un instant."
                : ex.Message;
            if ((int)ex.StatusCode >= 500)
                _logger.LogError(ex, "Erreur API sur le suivi {Ticket} : {Detail}", vm.Ticket, ex.Detail ?? ex.Message);
        }
        return View(vm);
    }

    [HttpGet("contact")]
    public async Task<IActionResult> Contact(CancellationToken ct) =>
        View(RetryForm.Restore(TempData, new ContactViewModel { Shop = await _shop.GetShopAsync(ct) }));

    [HttpPost("contact")]
    public async Task<IActionResult> Contact(ContactForm form, CancellationToken ct)
    {
        // Champ piège rempli : on fait semblant que tout va bien.
        if (!string.IsNullOrEmpty(form.Website))
        {
            TempData["Success"] = "Merci, votre message a bien été envoyé.";
            return RedirectToAction(nameof(Contact));
        }

        if (ModelState.IsValid)
        {
            try
            {
                var message = await _portal.Api.SendContactAsync(
                    form.Name.Trim(), form.Email.Trim(), string.IsNullOrWhiteSpace(form.Phone) ? null : form.Phone.Trim(),
                    form.Message.Trim(), ct);
                TempData["Success"] = string.IsNullOrWhiteSpace(message)
                    ? "Merci, votre message a bien été envoyé. Nous vous répondons très vite."
                    : message;
                return RedirectToAction(nameof(Contact));
            }
            catch (ApiException ex) when (ex.StatusCode != HttpStatusCode.ServiceUnavailable && !ex.IsUnauthorized)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        return View(new ContactViewModel { Form = form, Shop = await _shop.GetShopAsync(ct) });
    }

    [HttpGet("mentions-legales")]
    public async Task<IActionResult> MentionsLegales(CancellationToken ct) =>
        View(await _shop.GetShopAsync(ct));

    [HttpGet("erreur/{code:int}")]
    [HttpPost("erreur/{code:int}")]
    [IgnoreAntiforgeryToken]
    public IActionResult Erreur(int code)
    {
        Response.StatusCode = code is >= 400 and <= 599 ? code : 500;
        return code switch
        {
            404 => View("NotFound"),
            _ => View("Error", new ErrorViewModel(code, null))
        };
    }
}
