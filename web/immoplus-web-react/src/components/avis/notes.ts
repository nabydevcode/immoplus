import type { AvisDto, MoyennesAvisDto } from "../../types/models";
import { MOIS } from "../sejour/dates";

/** Sous-notes facultatives données par le voyageur au logement. */
export const CRITERES_AVIS: { cle: keyof MoyennesAvisDto & keyof AvisDto; libelle: string }[] = [
  { cle: "proprete", libelle: "Propreté" },
  { cle: "communication", libelle: "Communication" },
  { cle: "emplacement", libelle: "Emplacement" },
  { cle: "rapportQualitePrix", libelle: "Qualité-prix" },
];

export const COMMENTAIRE_MIN = 10;
export const COMMENTAIRE_MAX = 1000;
export const REPONSE_MAX = 500;

/** 4.75 → « 4,8 » */
export const formaterNote = (note: number) =>
  note.toLocaleString("fr-FR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** « 2026-10-12T… » ou « 2026-10 » → « octobre 2026 » */
export function moisAnnee(iso: string | null | undefined) {
  if (!iso || !/^\d{4}-\d{2}/.test(iso)) return null;
  const [a, m] = iso.slice(0, 7).split("-").map(Number);
  return `${MOIS[m - 1]} ${a}`;
}

/** Délai de réponse moyen de l'hôte, en clair. */
export function libelleDelaiReponse(heures: number | null | undefined) {
  if (heures == null) return null;
  if (heures <= 1) return "en moins d'une heure";
  if (heures < 24) return `en ${heures} h environ`;
  const jours = Math.round(heures / 24);
  return jours <= 1 ? "en un jour environ" : `en ${jours} jours environ`;
}
