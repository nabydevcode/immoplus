namespace ImmoPlus.Api.Services;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ImmoPlus.Api.Models;

public class TokenServices
{
    private readonly IConfiguration _configuration;

    public TokenServices(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenererToken(Utilisateur utilisateur)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, utilisateur.Id.ToString()),
            new(ClaimTypes.Name, utilisateur.Nom),
            new(ClaimTypes.Email, utilisateur.Email),
            new(ClaimTypes.Role, utilisateur.Role.ToString())
        };

        var cle = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Cle"]!));
        var identifiants = new SigningCredentials(cle, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: identifiants
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
