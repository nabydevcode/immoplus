import type { AppartementDto, CandidatureDto } from "../../types/models";

export const libellesSituation: Record<string, { label: string; note: string }> = {
  CDI: { label: "CDI", note: "Contrat à durée indéterminée" },
  CDD: { label: "CDD", note: "Contrat à durée limitée" },
  Independant: { label: "Indépendant", note: "Revenus pouvant varier" },
  DebutActiviteRevenuFixe: { label: "Début d'activité", note: "Revenu fixe, activité récente" },
  Autre: { label: "Autre", note: "Situation à préciser avec le candidat" },
};

/** Même calcul que FinancementServices côté API, pour simuler le financement avant d'accepter. */
export function simulerFinancement(
  candidature: CandidatureDto,
  appartement: AppartementDto | undefined,
  tauxFrais: number,
) {
  const loyer = appartement?.loyerMensuel ?? 0;
  const duree = appartement?.dureeAvanceExigee ?? 0;
  const avance = loyer * duree;
  const apport = candidature.apportInitial;
  const moisCouverts = loyer > 0 ? apport / loyer : 0;
  const couverture = avance > 0 ? Math.min(apport / avance, 1) : 0;
  if (!appartement || apport >= avance) return { avance, moisCouverts, couverture, financement: null };

  const nombreMensualites = duree - Math.floor(apport / loyer);
  const montant = avance - apport;
  return {
    avance,
    moisCouverts,
    couverture,
    financement: { montant, nombreMensualites, mensualite: (montant / nombreMensualites) * (1 + tauxFrais) },
  };
}

export const libellesTypeDocument: Record<string, string> = {
  PieceIdentite: "Pièce d'identité",
  JustificatifRevenu: "Justificatif de revenus",
  JustificatifDomicile: "Justificatif de domicile",
  Autre: "Autre document",
};

/** « Pièce d'identité », « Justificatif de revenus du garant »… */
export const libellePiece = (piece: { proprietaire: string; typeDocument: string }) =>
  `${libellesTypeDocument[piece.typeDocument] ?? piece.typeDocument}${piece.proprietaire === "Garant" ? " du garant" : ""}`;

export const VISITE_ACTIVE = ["Proposee", "Confirmee", "Indisponible"];

export const libellesStatutVisite: Record<string, { label: string; classe: string }> = {
  Proposee: { label: "En attente de réponse", classe: "bg-amber-100 text-amber-700" },
  Confirmee: { label: "Présence confirmée", classe: "bg-green-100 text-green-700" },
  Indisponible: { label: "Candidat indisponible", classe: "bg-red-100 text-red-700" },
  Effectuee: { label: "Visite effectuée", classe: "bg-green-100 text-green-700" },
  Absent: { label: "Candidat absent", classe: "bg-red-100 text-red-700" },
  Annulee: { label: "Annulée", classe: "bg-gray-100 text-gray-600" },
};

/** « mercredi 08/10/2026 à 10h00 » */
export const formaterDateHeure = (iso: string) => {
  const d = new Date(iso);
  return `${d.toLocaleDateString("fr-FR", { weekday: "long", day: "2-digit", month: "2-digit", year: "numeric" })} à ${d
    .toLocaleTimeString("fr-FR", { hour: "2-digit", minute: "2-digit" })
    .replace(":", "h")}`;
};

/** Référence d'un dossier de candidature : son numéro sur 6 chiffres (48 → « 000048 »). */
export { formaterReference as referenceDossier } from "../admin/outils";
