using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Pressing.Core.Dtos;

namespace Pressing.Web.Infrastructure;

/// <summary>Revendications stockées dans le cookie d'authentification du client.</summary>
public static class AuthClaims
{
    public const string AccessToken = "access_token";
    public const string TokenExpires = "token_expires";
    public const string FirstName = "first_name";

    public static ClaimsPrincipal Build(ClientDto client, string token, DateTime expiresAt)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, client.Id.ToString()),
            new(ClaimTypes.Name, client.FullName),
            new(FirstName, client.FirstName),
            new(AccessToken, token),
            new(TokenExpires, expiresAt.ToUniversalTime().ToString("O"))
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static Task SignInAsync(HttpContext http, ClientDto client, string token, DateTime expiresAt, bool persistent) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Build(client, token, expiresAt),
            new AuthenticationProperties { IsPersistent = persistent, AllowRefresh = true });

    public static string? Token(this ClaimsPrincipal user) => user.FindFirst(AccessToken)?.Value;

    public static string DisplayFirstName(this ClaimsPrincipal user) => user.FindFirst(FirstName)?.Value ?? "";
}
