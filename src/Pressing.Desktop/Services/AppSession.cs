using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;

namespace Pressing.Desktop.Services;

/// <summary>Session du personnel connecté : client API, utilisateur, rôle et catalogue en cache.</summary>
public sealed class AppSession
{
    private List<CategoryDto>? _catalog;

    public AppSession(PressingApiClient api, StaffUserDto staff)
    {
        Api = api;
        Staff = staff;
    }

    public PressingApiClient Api { get; }

    public StaffUserDto Staff { get; }

    public UserRole Role => Staff.Role;

    public bool IsAdmin => Role == UserRole.Admin;

    /// <summary>Manager ou Administrateur (politique « ManagerOrAdmin » de l'API).</summary>
    public bool IsManagerOrAdmin => Role is UserRole.Admin or UserRole.Manager;

    /// <summary>Catalogue des catégories actives (mis en cache ; <paramref name="force"/> pour recharger).</summary>
    public async Task<IReadOnlyList<CategoryDto>> GetCatalogAsync(bool force = false, bool includeInactive = false)
    {
        if (includeInactive)
        {
            return await Api.GetCatalogAsync(true) ?? new List<CategoryDto>();
        }

        if (force || _catalog is null)
        {
            _catalog = await Api.GetCatalogAsync() ?? new List<CategoryDto>();
        }

        return _catalog;
    }

    public void InvalidateCatalog() => _catalog = null;
}
