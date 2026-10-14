import { useState } from "react";
import { useChargement } from "../admin/outils";
import type { AppartementDto, DevisDto } from "../../types/models";
import type { EtatJour } from "./Calendrier";
import { ajouterMois, ecartJours, jourLocal, moisDe, premiereNuitOccupee } from "./dates";
import { useCalendrier } from "./useCalendrier";

export interface SelectionInitiale {
  arrivee: string | null;
  depart: string | null;
  voyageurs: number;
}

/** Choix des dates et des voyageurs d'un séjour court, avec le calendrier et le devis en direct. */
export function useSelectionSejour(appartement: AppartementDto, initial: SelectionInitiale) {
  const [aujourdhui] = useState(jourLocal);
  // Des dates passées dans l'URL ne sont reprises que si elles sont cohérentes.
  const datesValides = !!initial.arrivee && initial.arrivee >= aujourdhui;
  const [arrivee, setArrivee] = useState<string | null>(datesValides ? initial.arrivee : null);
  const [depart, setDepart] = useState<string | null>(
    datesValides && initial.depart && initial.depart > initial.arrivee! ? initial.depart : null,
  );
  const [voyageurs, setVoyageurs] = useState(Math.max(1, initial.voyageurs));
  const [mois, setMois] = useState(() => moisDe(arrivee ?? aujourdhui));

  const calendrier = useCalendrier(appartement.id, aujourdhui, mois);
  const nuits = calendrier.nuits;
  const nuitsMin = Math.max(1, appartement.nuitsMin ?? 1);
  const capaciteMax = appartement.capacite ?? 20;

  const devis = useChargement<DevisDto>(
    arrivee && depart
      ? `reservation/devis?appartementId=${appartement.id}&arrivee=${arrivee}&depart=${depart}&voyageurs=${voyageurs}`
      : null,
  );
  // Le devis affiché doit correspondre aux dates choisies (il reste celui des dates précédentes pendant le calcul).
  const nombreNuits = arrivee && depart ? ecartJours(arrivee, depart) : 0;
  const devisAJour = arrivee && depart && devis.donnees?.nombreNuits === nombreNuits ? devis.donnees : null;

  const choixDepart = arrivee !== null && depart === null;
  const limiteDepart = choixDepart ? premiereNuitOccupee(nuits, arrivee) : null;

  const etatJour = (iso: string): EtatJour => {
    if (iso < aujourdhui) return { ton: "passe", cliquable: false };
    const periode = nuits.get(iso);
    const ton = periode ? (periode.type === "Reservee" ? "reserve" : "bloque") : "libre";
    if (choixDepart && iso > arrivee) {
      // Départ possible jusqu'à la première nuit occupée (le jour d'arrivée d'un autre voyageur).
      if (limiteDepart && iso > limiteDepart) return { ton, cliquable: false };
      if (ecartJours(arrivee, iso) < nuitsMin) return { ton, cliquable: false, titre: `${nuitsMin} nuits minimum` };
      return { ton, cliquable: true, titre: periode ? "Départ possible ce jour-là" : undefined };
    }
    return { ton, cliquable: !periode, titre: periode ? "Indisponible" : undefined };
  };

  const choisir = (iso: string) => {
    if (choixDepart && iso > arrivee) {
      setDepart(iso);
    } else {
      setArrivee(iso);
      setDepart(null);
    }
  };

  const effacer = () => {
    setArrivee(null);
    setDepart(null);
  };

  return {
    aujourdhui,
    arrivee,
    depart,
    voyageurs,
    setVoyageurs: (v: number) => setVoyageurs(Math.min(Math.max(1, v), capaciteMax)),
    capaciteMax,
    nuitsMin,
    nombreNuits,
    mois,
    setMois,
    moisMin: moisDe(aujourdhui),
    moisMax: ajouterMois(moisDe(aujourdhui), 13),
    calendrier,
    etatJour,
    choisir,
    effacer,
    devis: devisAJour,
    erreurDevis: arrivee && depart ? devis.erreur : null,
    calculEnCours: !!arrivee && !!depart && !devisAJour && !devis.erreur,
  };
}

export type SelectionSejour = ReturnType<typeof useSelectionSejour>;
