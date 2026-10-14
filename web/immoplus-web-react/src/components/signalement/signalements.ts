import type { ActionSignalement, CibleSignalement, MotifSignalement, StatutSignalement } from "../../types/models";
import { valeurEnum } from "../admin/outils";

/** Motifs proposés, dans l'ordre de l'énumération de l'API (Arnaque = 0 … Autre = 6). */
export const MOTIFS: { valeur: MotifSignalement; libelle: string }[] = [
  { valeur: "Arnaque", libelle: "Arnaque ou fraude" },
  { valeur: "AnnonceTrompeuse", libelle: "Annonce trompeuse" },
  { valeur: "ContenuInapproprie", libelle: "Contenu inapproprié" },
  { valeur: "Doublon", libelle: "Annonce en double" },
  { valeur: "LogementIndisponible", libelle: "Logement indisponible" },
  { valeur: "ComportementAbusif", libelle: "Comportement abusif" },
  { valeur: "Autre", libelle: "Autre" },
];

export const STATUTS_SIGNALEMENT: {
  valeur: StatutSignalement;
  libelle: string;
  ton: "attente" | "info" | "succes" | "neutre";
}[] = [
  { valeur: "Nouveau", libelle: "Nouveau", ton: "attente" },
  { valeur: "EnCours", libelle: "En cours", ton: "info" },
  { valeur: "Traite", libelle: "Traité", ton: "succes" },
  { valeur: "Rejete", libelle: "Rejeté", ton: "neutre" },
];

export const LIBELLES_CIBLE: Record<CibleSignalement, string> = {
  Appartement: "Annonce",
  Utilisateur: "Utilisateur",
  Avis: "Avis",
  Message: "Message",
};

export const ACTIONS_SIGNALEMENT: { valeur: ActionSignalement; libelle: string; cibles: CibleSignalement[] }[] = [
  {
    valeur: "aucune",
    libelle: "Aucune action sur la cible",
    cibles: ["Appartement", "Utilisateur", "Avis", "Message"],
  },
  { valeur: "desactiverAnnonce", libelle: "Désactiver l'annonce", cibles: ["Appartement"] },
  {
    valeur: "desactiverUtilisateur",
    libelle: "Désactiver le compte",
    cibles: ["Utilisateur", "Appartement", "Avis", "Message"],
  },
  { valeur: "masquerAvis", libelle: "Masquer l'avis", cibles: ["Avis"] },
];

export const libelleMotif = (motif: MotifSignalement | number) =>
  MOTIFS.find((m) => m.valeur === valeurEnum(motif, MOTIFS))?.libelle ?? String(motif);

export const statutSignalement = (statut: StatutSignalement | number) =>
  STATUTS_SIGNALEMENT.find((s) => s.valeur === valeurEnum(statut, STATUTS_SIGNALEMENT)) ?? {
    valeur: String(statut) as StatutSignalement,
    libelle: String(statut),
    ton: "neutre" as const,
  };

export const TEXTE_SIGNALEMENT_MAX = 1000;
