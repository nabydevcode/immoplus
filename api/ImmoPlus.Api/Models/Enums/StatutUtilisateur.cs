namespace ImmoPlus.Api.Models.Enums;

public enum StatutUtilisateur
{
    EnAttente,
    Valide,
    Refuse,
    /// <summary>Compte suspendu par un administrateur : connexion et sessions bloquées, annonces masquées.</summary>
    Desactive
}
