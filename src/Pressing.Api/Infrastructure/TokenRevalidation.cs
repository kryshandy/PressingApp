using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Pressing.Core;
using Pressing.Core.Data;

namespace Pressing.Api.Infrastructure;

/// <summary>
/// Un jeton signé reste valide jusqu'à son expiration : on relit donc en base, à chaque requête,
/// que le compte existe toujours, est actif et (personnel) a toujours le même rôle ou (client) a toujours l'accès web.
/// </summary>
public static class TokenRevalidation
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        ClaimsPrincipal? principal = context.Principal;
        string? type = principal?.FindFirstValue("token_type");
        if (principal is null || !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out int id))
        {
            context.Fail("Jeton incomplet.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<PressingDbContext>();
        CancellationToken ct = context.HttpContext.RequestAborted;

        switch (type)
        {
            case "staff":
                var staff = await db.StaffUsers.AsNoTracking()
                    .Where(s => s.Id == id)
                    .Select(s => new { s.IsActive, s.Role })
                    .FirstOrDefaultAsync(ct);
                string? tokenRole = principal.FindFirstValue(ClaimTypes.Role);
                if (staff is null || !staff.IsActive || !string.Equals(tokenRole, staff.Role.ToString(), StringComparison.Ordinal))
                {
                    context.Fail("Compte du personnel désactivé, supprimé ou rôle modifié.");
                }

                break;

            case "client":
                var client = await db.Clients.AsNoTracking()
                    .Where(c => c.Id == id)
                    .Select(c => new { c.IsActive, c.WebAccessEnabled })
                    .FirstOrDefaultAsync(ct);
                if (client is null || !client.IsActive || !client.WebAccessEnabled)
                {
                    context.Fail("Compte client désactivé ou sans accès web.");
                }

                break;

            default:
                context.Fail("Type de jeton inconnu.");
                break;
        }
    }
}
