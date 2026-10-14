import { useEffect, useState } from "react";
import { apiClient } from "../../api/client";
import type { CompteursAdminDto } from "../../types/models";

/** Prévient le menu admin qu'un signalement ou une pièce d'identité vient d'être traité (pastilles). */
const EVENEMENT_COMPTEURS = "immoplus:compteurs-admin";
export const signalerCompteursAdmin = () => window.dispatchEvent(new Event(EVENEMENT_COMPTEURS));

const ACTUALISATION_MS = 120_000;

/** Pastilles du menu admin : signalements « Nouveau » et pièces d'identité en attente. */
export function useCompteursAdmin(actif: boolean) {
  const [compteurs, setCompteurs] = useState({ signalements: 0, identites: 0 });

  useEffect(() => {
    if (!actif) return;
    let annule = false;
    const charger = () => {
      apiClient
        .get<CompteursAdminDto>("admin/compteurs")
        .then((c) => {
          if (!annule) setCompteurs({ signalements: c.signalementsNouveaux, identites: c.identitesEnAttente });
        })
        .catch(() => {
          // Pastilles non essentielles : on garde les dernières valeurs connues.
        });
    };
    charger();
    const minuterie = window.setInterval(charger, ACTUALISATION_MS);
    window.addEventListener(EVENEMENT_COMPTEURS, charger);
    return () => {
      annule = true;
      window.clearInterval(minuterie);
      window.removeEventListener(EVENEMENT_COMPTEURS, charger);
    };
  }, [actif]);

  return compteurs;
}
