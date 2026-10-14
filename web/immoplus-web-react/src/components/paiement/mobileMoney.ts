import type { OperateurMobile, StatutTransactionMobile, TransactionMobileDto } from "../../types/models";

export const OPERATEURS: Record<
  OperateurMobile,
  { nom: string; court: string; fond: string; texte: string; code: string }
> = {
  OrangeMoney: { nom: "Orange Money", court: "orange", fond: "#FF7900", texte: "#000000", code: "#144#" },
  MtnMomo: { nom: "MTN Mobile Money", court: "MTN", fond: "#FFCC00", texte: "#000000", code: "*440#" },
};

const NOMS_OPERATEURS: OperateurMobile[] = ["OrangeMoney", "MtnMomo"];
const NOMS_STATUTS: StatutTransactionMobile[] = ["EnAttente", "Reussie", "Echouee", "Expiree"];

/** L'API peut renvoyer les énumérations en texte ou en nombre. */
export const operateurDe = (t: TransactionMobileDto): OperateurMobile =>
  typeof t.operateur === "number" ? (NOMS_OPERATEURS[t.operateur] ?? "OrangeMoney") : t.operateur;
export const statutDe = (t: TransactionMobileDto): StatutTransactionMobile =>
  typeof t.statut === "number" ? (NOMS_STATUTS[t.statut] ?? "EnAttente") : t.statut;

/** Transaction du fournisseur simulé (développement local) : boutons de simulation affichés. */
export const estSimulation = (t: TransactionMobileDto) => t.simulation === true;

/**
 * Numéro guinéen normalisé sur 9 chiffres commençant par 6 (espaces, +224 et 00224 tolérés),
 * ou null s'il n'est pas valide (même règle que l'API).
 */
export function normaliserTelephone(saisie: string) {
  let chiffres = saisie.replace(/[\s.-]/g, "");
  if (chiffres.startsWith("+224")) chiffres = chiffres.slice(4);
  else if (chiffres.startsWith("00224")) chiffres = chiffres.slice(5);
  else if (chiffres.length === 12 && chiffres.startsWith("224")) chiffres = chiffres.slice(3);
  return /^6\d{8}$/.test(chiffres) ? chiffres : null;
}

/** Durée maximale de suivi d'une transaction (elle expire côté API après 10 minutes). */
export const DUREE_SUIVI_MS = 10 * 60_000;
export const INTERVALLE_SUIVI_MS = 3_000;
