namespace ImmoPlus.Api.Dtos;

public class EnvoyerMessageDto
{
    public string Contenu { get; set; } = string.Empty;
}

public class MessageDto
{
    public int Id { get; set; }
    public int ReservationId { get; set; }
    public int AuteurId { get; set; }
    public string AuteurNom { get; set; } = string.Empty;
    public string Contenu { get; set; } = string.Empty;
    public DateTime DateEnvoi { get; set; }
    public DateTime? DateLecture { get; set; }
    /// <summary>Message écrit par l'utilisateur connecté.</summary>
    public bool DeMoi { get; set; }
}

public class MessagesNonLusDto
{
    public int Total { get; set; }
    public List<NonLusReservationDto> ParReservation { get; set; } = new();
}

public class NonLusReservationDto
{
    public int ReservationId { get; set; }
    public int Nombre { get; set; }
}
