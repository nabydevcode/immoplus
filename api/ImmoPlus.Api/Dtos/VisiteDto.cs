namespace ImmoPlus.Api.Dtos;

public class ProgrammerVisiteDto
{
    public DateTime DateHeure { get; set; }
    public string? Message { get; set; }
}

public class RepondreVisiteDto
{
    public bool Disponible { get; set; }
    public string? Message { get; set; }
}

public class CloturerVisiteDto
{
    /// <summary>true : visite effectuée ; false : le candidat ne s'est pas présenté.</summary>
    public bool Effectuee { get; set; }
    public string? Notes { get; set; }
}

public class VisiteResponseDto
{
    public int Id { get; set; }
    public DateTime DateHeure { get; set; }
    public string? Message { get; set; }
    public string Statut { get; set; } = string.Empty;
    public string? ReponseLocataire { get; set; }
    /// <summary>Renseigné uniquement pour le bailleur et l'admin.</summary>
    public string? NotesBailleur { get; set; }
}
