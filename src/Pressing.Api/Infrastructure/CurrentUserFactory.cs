using System.Security.Claims;
using Pressing.Core.Security;

namespace Pressing.Api.Infrastructure;

public static class CurrentUserFactory
{
    public static CurrentUser FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return CurrentUser.System;
        }

        string type = principal.FindFirstValue("token_type") ?? string.Empty;
        string name = principal.FindFirstValue(ClaimTypes.Name) ?? "Utilisateur";
        string role = principal.FindFirstValue(ClaimTypes.Role) ?? "User";
        string? idValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idValue, out int id))
        {
            return new CurrentUser { Name = name, Role = role };
        }

        return type switch
        {
            "staff" => new CurrentUser { StaffId = id, Name = name, Role = role },
            "client" => new CurrentUser { ClientId = id, Name = name, Role = "Client" },
            _ => new CurrentUser { Name = name, Role = role }
        };
    }
}
