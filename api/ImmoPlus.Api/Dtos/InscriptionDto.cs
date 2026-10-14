namespace ImmoPlus.Api.Dtos;

public class InscriptionDto
{
    public string Nom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MotDePasse { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

/// <summary>Création d'un administrateur par un administrateur (validé d'office).</summary>
public class CreerAdminDto
{
    public string Nom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MotDePasse { get; set; } = string.Empty;
}
