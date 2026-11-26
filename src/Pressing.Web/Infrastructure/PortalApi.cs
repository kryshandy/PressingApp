using Pressing.Client;

namespace Pressing.Web.Infrastructure;

/// <summary>
/// Donne un <see cref="PressingApiClient"/> dont le jeton est celui du client connecté (revendication du cookie).
/// Instance par requête : le jeton n'est jamais partagé entre visiteurs.
/// </summary>
public sealed class PortalApi
{
    public PortalApi(PressingApiClient api, IHttpContextAccessor accessor)
    {
        Api = api;
        Api.Token = accessor.HttpContext?.User.Token();
        // IP du visiteur : l'API s'en sert pour sa limitation de débit (sinon tous les visiteurs partageraient celle du site).
        Api.ForwardedFor = accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    }

    public PressingApiClient Api { get; }
}
