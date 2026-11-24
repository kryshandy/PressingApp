using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Pressing.Client;
using Pressing.Core.Options;

namespace Pressing.Web.Infrastructure;

/// <summary>Informations publiques de la boutique (cache court) : évite un appel API par page.</summary>
public sealed class ShopDirectory
{
    private const string Key = "public-info";
    private const string LastGoodKey = "public-info-last-good";
    private const string DownKey = "public-info-down";

    public static readonly ShopInfo Fallback = new(
        "Pressing Éclat", "Pressing Éclat", "", "", "", "", "",
        "L'art du linge impeccable", "FCFA", "", "");

    private readonly PortalApi _portal;
    private readonly IMemoryCache _cache;

    public ShopDirectory(PortalApi portal, IMemoryCache cache)
    {
        _portal = portal;
        _cache = cache;
    }

    /// <summary>Lève <see cref="ApiException"/> si l'API est injoignable et qu'aucune copie n'existe.</summary>
    public async Task<PublicInfoDto> GetAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(Key, out PublicInfoDto? cached) && cached is not null) return cached;

        _cache.TryGetValue(LastGoodKey, out PublicInfoDto? lastGood);
        if (_cache.TryGetValue(DownKey, out _))
        {
            if (lastGood is not null) return lastGood;
            throw ApiException.Unreachable(_portal.Api.BaseUrl, new InvalidOperationException("API indisponible (cache négatif)."));
        }

        try
        {
            var info = await _portal.Api.GetPublicInfoAsync(ct)
                       ?? throw new ApiException(HttpStatusCode.BadGateway, "Réponse vide du serveur.");
            _cache.Set(Key, info, TimeSpan.FromMinutes(2));
            _cache.Set(LastGoodKey, info, TimeSpan.FromHours(6));
            return info;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            _cache.Set(DownKey, true, TimeSpan.FromSeconds(8));
            if (lastGood is not null) return lastGood;
            throw;
        }
    }

    /// <summary>Version tolérante (en-tête, pied de page) : ne lève jamais.</summary>
    public async Task<ShopInfo> GetShopAsync(CancellationToken ct = default)
    {
        try { return (await GetAsync(ct)).Shop; }
        catch (ApiException) { return Fallback; }
    }
}
