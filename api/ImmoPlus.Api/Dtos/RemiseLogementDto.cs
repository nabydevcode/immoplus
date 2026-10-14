namespace ImmoPlus.Api.Dtos;

public class EnregistrerApportDto
{
    public string ModePaiement { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime DatePaiement { get; set; }
}

public class RemiseClesDto
{
    public DateTime DateRemise { get; set; }
}
