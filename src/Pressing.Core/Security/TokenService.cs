using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Pressing.Core.Entities;

namespace Pressing.Core.Security;

public class TokenOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "pressing-eclat";
    public string Audience { get; set; } = "pressing-clients";
    public int StaffHours { get; set; } = 12;
    public int ClientDays { get; set; } = 7;
}

public class TokenService
{
    private readonly TokenOptions _options;
    private readonly JwtSecurityTokenHandler _handler = new();

    public TokenService(TokenOptions options)
    {
        _options = options;
    }

    public (string Token, DateTime ExpiresAt) CreateStaffToken(StaffUser user)
    {
        DateTime expires = DateTime.UtcNow.AddHours(_options.StaffHours);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("username", user.Username),
            new("token_type", "staff")
        };

        return (Write(claims, expires), expires);
    }

    public (string Token, DateTime ExpiresAt) CreateClientToken(Client client)
    {
        DateTime expires = DateTime.UtcNow.AddDays(_options.ClientDays);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, client.Id.ToString()),
            new(ClaimTypes.NameIdentifier, client.Id.ToString()),
            new(ClaimTypes.Name, client.FullName),
            new(ClaimTypes.Role, "Client"),
            new("token_type", "client"),
            new("email", client.Email ?? string.Empty)
        };

        return (Write(claims, expires), expires);
    }

    public ClaimsPrincipal? Validate(string token)
    {
        try
        {
            return _handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);
        }
        catch
        {
            return null;
        }
    }

    public string CreateUnsubscribeToken(int clientId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientId.ToString()),
            new("client_id", clientId.ToString()),
            new("token_type", "unsubscribe")
        };

        return Write(claims, DateTime.UtcNow.AddDays(180));
    }
    private string Write(IEnumerable<Claim> claims, DateTime expires)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: expires,
            signingCredentials: credentials);

        return _handler.WriteToken(token);
    }
}
