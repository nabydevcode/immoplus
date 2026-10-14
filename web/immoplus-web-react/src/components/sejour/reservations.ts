import type { ReservationDto } from "../../types/models";
import { ajouterJours, ecartJours } from "./dates";

export type StatutReservation = ReservationDto["statut"];

export const statutsReservation: Record<
  StatutReservation,
  { label: string; ton: "attente" | "succes" | "danger" | "neutre" | "info" }
> = {
  DemandeEnvoyee: { label: "En attente de l'hôte", ton: "info" },
  EnAttente: { label: "En attente de paiement", ton: "attente" },
  Confirmee: { label: "Confirmée", ton: "succes" },
  Refusee: { label: "Refusée", ton: "danger" },
  Annulee: { label: "Annulée", ton: "danger" },
  Terminee: { label: "Terminée", ton: "neutre" },
};

/** Réservation qui bloque encore les dates (demande, attente de paiement ou confirmée). */
export const estActive = (r: ReservationDto) =>
  r.statut === "DemandeEnvoyee" || r.statut === "EnAttente" || r.statut === "Confirmee";

export const nombreNuits = (r: ReservationDto) => r.nombreNuits ?? ecartJours(r.dateArrivee, r.dateDepart);

/** Sans réponse sous 48 h, une demande est refusée automatiquement : date et heure limites de réponse. */
export function limiteReponse(r: ReservationDto) {
  const creation = new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(r.dateCreation) ? r.dateCreation : `${r.dateCreation}Z`);
  return new Date(creation.getTime() + 48 * 3_600_000).toLocaleString("fr-FR", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/** Dernier jour pour évaluer un séjour terminé (null si l'avis n'est pas ou plus possible). */
function avisOuvert(r: ReservationDto, aujourdhui: string) {
  return r.statut === "Terminee" && !!r.avisPossibleJusquau && aujourdhui <= r.avisPossibleJusquau.slice(0, 10);
}
export const avisVoyageurPossible = (r: ReservationDto, aujourdhui: string) =>
  avisOuvert(r, aujourdhui) && !r.avisVoyageurDonne;
export const avisHotePossible = (r: ReservationDto, aujourdhui: string) =>
  avisOuvert(r, aujourdhui) && !r.avisHoteDonne;

/** La messagerie se ferme 30 jours après un refus, une annulation ou la fin du séjour. */
export function messagerieOuverte(r: ReservationDto, aujourdhui: string) {
  const reference =
    r.statut === "Annulee"
      ? (r.dateAnnulation ?? r.dateCreation)
      : r.statut === "Refusee"
        ? (r.dateReponseHote ?? r.dateCreation)
        : r.statut === "Terminee"
          ? r.dateDepart
          : null;
  return reference === null || ajouterJours(reference.slice(0, 10), 30) >= aujourdhui;
}

/** Montant restant à rembourser au voyageur après une annulation. */
export const resteARembourser = (r: ReservationDto) =>
  Math.max((r.montantARembourser ?? 0) - (r.montantRembourse ?? 0), 0);

export const libellesAnnuleePar = {
  Voyageur: "le voyageur",
  Hote: "l'hôte",
  Admin: "ImmoPlus",
  Systeme: "ImmoPlus (automatiquement)",
} as const;

/** Horodatage de l'API (UTC sans fuseau) → Date. */
const dateUtc = (date: string) => new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(date) ? date : `${date}Z`);

/** Échéance de paiement d'une réservation en attente : « 14 oct. à 15:30 » (null si non concernée). */
export function echeancePaiement(r: ReservationDto) {
  if (r.statut !== "EnAttente" || !r.payerAvant) return null;
  const d = dateUtc(r.payerAvant);
  if (Number.isNaN(d.getTime())) return null;
  const jour = d.toLocaleDateString("fr-FR", { day: "numeric", month: "short" });
  const heure = d.toLocaleTimeString("fr-FR", { hour: "2-digit", minute: "2-digit" });
  return `${jour} à ${heure}`;
}

/** Mention affichée tant que l'adresse d'un séjour court est approximative. */
export const MENTION_ADRESSE_MASQUEE = "Adresse exacte communiquée après confirmation";
