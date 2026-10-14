/** Page d'administration où retrouver un objet (journal, signalements) ; null s'il n'y en a pas. */
export function lienAdminCible(cible: string, id: number | null | undefined, nom?: string | null): string | null {
  if (id == null) return null;
  switch (cible) {
    case "Candidature":
      return `/admin/candidatures?q=${id}`;
    case "Location":
      return `/admin/locations?q=${id}`;
    case "Financement":
    case "FinancementLocation":
      return `/admin/financements?q=${id}`;
    case "Reservation":
      return `/admin/reservations?q=${id}`;
    case "Appartement":
      return `/admin/appartements?q=${id}`;
    case "Utilisateur":
      // La liste des utilisateurs se filtre par nom / email ; à défaut, l'historique du compte dans le journal.
      return nom ? `/admin/utilisateurs?q=${encodeURIComponent(nom)}` : `/admin/journal?utilisateurId=${id}`;
    case "Signalement":
      return `/admin/signalements?id=${id}`;
    case "PieceIdentite":
      return `/admin/identites`;
    default:
      return null;
  }
}

export const LIBELLES_CIBLES_JOURNAL: Record<string, string> = {
  Utilisateur: "Utilisateur",
  Appartement: "Annonce",
  Candidature: "Candidature",
  Location: "Location",
  Financement: "Financement",
  Reservation: "Réservation",
  Paiement: "Paiement",
  PaiementMobile: "Paiement mobile",
  Signalement: "Signalement",
  PieceIdentite: "Pièce d'identité",
  Sauvegarde: "Sauvegarde",
  Export: "Export",
};
