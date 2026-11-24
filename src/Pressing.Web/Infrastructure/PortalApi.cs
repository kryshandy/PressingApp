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
    }

    public PressingApiClient Api { get; }
}
