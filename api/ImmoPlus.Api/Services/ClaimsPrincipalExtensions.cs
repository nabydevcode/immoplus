namespace ImmoPlus.Api.Services;

using System.Security.Claims;
using ImmoPlus.Api.Models.Enums;

public static class ClaimsPrincipalExtensions
{
    public static int ObtenirId(this ClaimsPrincipal utilisateur) =>
        int.Parse(utilisateur.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static RoleUtilisateur ObtenirRole(this ClaimsPrincipal utilisateur) =>
        Enum.Parse<RoleUtilisateur>(utilisateur.FindFirstValue(ClaimTypes.Role)!);
}
