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

    public HomeController(PortalApi portal, ShopDirectory shop)
    {
        _portal = portal;
        _shop = shop;
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
        if (string.IsNullOrEmpty(vm.Ticket) && string.IsNullOrEmpty(vm.Phone)) return View(vm);

        vm.Searched = true;
        if (string.IsNullOrEmpty(vm.Ticket) || string.IsNullOrEmpty(vm.Phone))
        {
            vm.Error = "Renseignez à la fois le numéro de ticket et le téléphone utilisé lors du dépôt.";
            return View(vm);
        }

        try
        {
            vm.Result = await _portal.Api.TrackOrderAsync(vm.Ticket.ToUpperInvariant(), vm.Phone, ct);
        }
        catch (ApiException ex) when (ex.StatusCode != HttpStatusCode.ServiceUnavailable && !ex.IsUnauthorized)
        {
            vm.Error = ex.Message;
        }
        return View(vm);
    }

    [HttpGet("contact")]
    public async Task<IActionResult> Contact(CancellationToken ct) =>
        View(new ContactViewModel { Shop = await _shop.GetShopAsync(ct) });

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
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                ModelState.AddModelError("", "Le service est temporairement indisponible. Votre message est conservé dans le formulaire ; vous pouvez réessayer.");
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
