namespace ImmoPlus.Web.Models;

public class AppartementDto
{
    public int Id { get; set; }
    public int BailleurId { get; set; }
    public string BailleurNom { get; set; } = "";
    public string Adresse { get; set; } = "";
    public string? Description { get; set; }
    public bool Disponible { get; set; }
    public string TypeOffre { get; set; } = "";

    public decimal? LoyerMensuel { get; set; }
    public int? DureeAvanceExigee { get; set; }

    public decimal? PrixParNuit { get; set; }
    public int? DureeMaxMois { get; set; }
}

public class AppartementModele
{
    public int BailleurId { get; set; }
    public string Adresse { get; set; } = "";
    public string? Description { get; set; }
    public string TypeOffre { get; set; } = "LongueDuree";

    public decimal? LoyerMensuel { get; set; }
    public int? DureeAvanceExigee { get; set; }

    public decimal? PrixParNuit { get; set; }
    public int? DureeMaxMois { get; set; } = 1;
}
