import type { PolitiqueAnnulation } from "../../types/models";

/** Politiques d'annulation des séjours courts : libellé, résumé et règles détaillées. */
export const POLITIQUES: Record<PolitiqueAnnulation, { libelle: string; resume: string; regles: string[] }> = {
  Flexible: {
    libelle: "Flexible",
    resume: "Remboursement intégral jusqu'à la veille de l'arrivée.",
    regles: [
      "Annulation au moins 1 jour avant l'arrivée : remboursement intégral.",
      "Ensuite : les nuitées ne sont pas remboursées.",
    ],
  },
  Moderee: {
    libelle: "Modérée",
    resume: "Remboursement intégral jusqu'à 5 jours avant l'arrivée.",
    regles: [
      "Annulation au moins 5 jours avant l'arrivée : remboursement intégral.",
      "Entre 5 jours et la veille de l'arrivée : 50 % des nuitées remboursés.",
      "Ensuite : les nuitées ne sont pas remboursées.",
    ],
  },
  Stricte: {
    libelle: "Stricte",
    resume: "Remboursement intégral seulement dans les 48 h suivant la réservation, à 14 jours ou plus de l'arrivée.",
    regles: [
      "Annulation dans les 48 h suivant la réservation et au moins 14 jours avant l'arrivée : remboursement intégral.",
      "Sinon, au moins 7 jours avant l'arrivée : 50 % des nuitées remboursés.",
      "Ensuite : les nuitées ne sont pas remboursées.",
    ],
  },
};

export const POLITIQUE_PAR_DEFAUT: PolitiqueAnnulation = "Moderee";

export const politiqueDe = (p: string | null | undefined): PolitiqueAnnulation =>
  p === "Flexible" || p === "Moderee" || p === "Stricte" ? p : POLITIQUE_PAR_DEFAUT;

export const NOTE_POLITIQUE =
  "Les frais de ménage sont toujours remboursés si l'annulation a lieu avant l'arrivée. Si l'hôte annule, le voyageur est remboursé intégralement.";
