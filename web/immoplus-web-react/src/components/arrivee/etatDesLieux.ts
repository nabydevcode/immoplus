import type { PhotoEtatDesLieuxDto, ReservationDto } from "../../types/models";
import { ajouterJours, formaterJour, jour } from "../sejour/dates";

export type MomentEtatDesLieux = "Arrivee" | "Depart";

export const PHOTOS_MAX_PAR_MOMENT = 20;
export const TAILLE_MAX_PHOTO_EDL = 10 * 1024 * 1024;
export const TYPES_PHOTO_EDL = ["image/jpeg", "image/png", "image/webp"];
/** Une photo peut être supprimée par son auteur pendant 24 h. */
export const DELAI_SUPPRESSION_MS = 24 * 3_600_000;

export const momentDe = (p: PhotoEtatDesLieuxDto): MomentEtatDesLieux =>
  p.moment === 1 || p.moment === "Depart" ? "Depart" : "Arrivee";

/** Horodatage de l'API (sans fuseau = UTC) en millisecondes. */
export const horodatage = (iso: string) => new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(iso) ? iso : `${iso}Z`).getTime();

/** État des lieux possible pour une réservation confirmée ou terminée (mêmes règles que l'API). */
export const etatDesLieuxPossible = (r: ReservationDto) => r.statut === "Confirmee" || r.statut === "Terminee";

/**
 * Période d'ajout des photos : arrivée à partir de la veille du séjour ;
 * départ de la veille du départ jusqu'à 3 jours après.
 */
export function fenetreAjout(r: ReservationDto, moment: MomentEtatDesLieux, aujourdhui: string) {
  if (moment === "Arrivee") {
    const debut = ajouterJours(jour(r.dateArrivee), -1);
    return aujourdhui >= debut
      ? { ouverte: true, message: null }
      : { ouverte: false, message: `Photos d'arrivée possibles à partir du ${formaterJour(debut)}.` };
  }
  const debut = ajouterJours(jour(r.dateDepart), -1);
  const fin = ajouterJours(jour(r.dateDepart), 3);
  if (aujourdhui < debut)
    return { ouverte: false, message: `Photos de départ possibles à partir du ${formaterJour(debut)}.` };
  if (aujourdhui > fin)
    return { ouverte: false, message: `Ajout de photos de départ fermé depuis le ${formaterJour(fin)}.` };
  return { ouverte: true, message: null };
}
