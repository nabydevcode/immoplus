namespace ImmoPlus.Web.Models;

public class UtilisateurDto
{
    public int Id { get; set; }
    public string Nom { get; set; } = "";
    public string Email { get; set; } = "";
    public string Telephone { get; set; } = "";
    public string Role { get; set; } = "";
    public string Statut { get; set; } = "";
}

public class InscriptionModele
{
    public string Nom { get; set; } = "";
    public string Telephone { get; set; } = "";
    public string Email { get; set; } = "";
    public string MotDePasse { get; set; } = "";
    public string Role { get; set; } = "";
}
