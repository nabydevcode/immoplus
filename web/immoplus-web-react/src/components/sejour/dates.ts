/**
 * Dates « jour » (AAAA-MM-JJ) des séjours courts. Les calculs se font en UTC sur la chaîne,
 * pour ne jamais dépendre du fuseau du navigateur ; seule la date du jour est lue localement.
 */
import type { PeriodeCalendrierDto } from "../../types/models";

export const MOIS = [
  "janvier",
  "février",
  "mars",
  "avril",
  "mai",
  "juin",
  "juillet",
  "août",
  "septembre",
  "octobre",
  "novembre",
  "décembre",
];
export const JOURS_SEMAINE = ["L", "M", "M", "J", "V", "S", "D"];

const deux = (n: number) => String(n).padStart(2, "0");

/** Date du jour dans le fuseau du navigateur (à appeler hors rendu : useState(jourLocal)). */
export function jourLocal() {
  const d = new Date();
  return `${d.getFullYear()}-${deux(d.getMonth() + 1)}-${deux(d.getDate())}`;
}

/** Partie jour d'une date de l'API (« 2026-10-20T00:00:00 » → « 2026-10-20 »). */
export const jour = (iso: string) => iso.slice(0, 10);

const versDate = (iso: string) => new Date(`${jour(iso)}T00:00:00Z`);
const versIso = (d: Date) => d.toISOString().slice(0, 10);

export function ajouterJours(iso: string, n: number) {
  const d = versDate(iso);
  d.setUTCDate(d.getUTCDate() + n);
  return versIso(d);
}

export const ecartJours = (debut: string, fin: string) =>
  Math.round((versDate(fin).getTime() - versDate(debut).getTime()) / 86_400_000);

/** Format valide AAAA-MM-JJ (paramètres d'URL). */
export const estJourValide = (v: string | null): v is string =>
  !!v && /^\d{4}-\d{2}-\d{2}$/.test(v) && !Number.isNaN(versDate(v).getTime());

/** « 12 oct. 2026 » */
export const jourCourt = (iso: string) =>
  versDate(iso).toLocaleDateString("fr-FR", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" });

/** Mois affiché, sous la forme « AAAA-MM ». */
export const moisDe = (iso: string) => iso.slice(0, 7);

export function ajouterMois(mois: string, n: number) {
  const [a, m] = mois.split("-").map(Number);
  const d = new Date(Date.UTC(a, m - 1 + n, 1));
  return `${d.getUTCFullYear()}-${deux(d.getUTCMonth() + 1)}`;
}

export function titreMois(mois: string) {
  const [a, m] = mois.split("-").map(Number);
  return `${MOIS[m - 1]} ${a}`;
}

/** Cases d'un mois (semaine commençant le lundi) : null pour les cases vides du début. */
export function casesDuMois(mois: string): (string | null)[] {
  const [a, m] = mois.split("-").map(Number);
  const decalage = (new Date(Date.UTC(a, m - 1, 1)).getUTCDay() + 6) % 7;
  const nombreJours = new Date(Date.UTC(a, m, 0)).getUTCDate();
  return [
    ...Array.from({ length: decalage }, () => null),
    ...Array.from({ length: nombreJours }, (_, i) => `${mois}-${deux(i + 1)}`),
  ];
}

/** Nuits occupées (jour → période), fin exclue. */
export function nuitsOccupees(periodes: PeriodeCalendrierDto[]) {
  const nuits = new Map<string, PeriodeCalendrierDto>();
  for (const p of periodes) {
    // Une réservation prime sur un blocage pour l'affichage.
    for (let d = jour(p.debut); d < jour(p.fin); d = ajouterJours(d, 1)) {
      if (!nuits.has(d) || p.type === "Reservee") nuits.set(d, p);
    }
  }
  return nuits;
}

/** Première nuit occupée à partir de `debut` (incluse), ou null. */
export function premiereNuitOccupee(nuits: Map<string, unknown>, debut: string) {
  let premiere: string | null = null;
  for (const d of nuits.keys()) if (d >= debut && (premiere === null || d < premiere)) premiere = d;
  return premiere;
}

/** Date « jour » de l'API (AAAA-MM-JJ) au format JJ/MM/AAAA, sans décalage de fuseau. */
export const formaterJour = (iso: string | null | undefined) =>
  iso ? versDate(iso).toLocaleDateString("fr-FR", { timeZone: "UTC" }) : "-";
