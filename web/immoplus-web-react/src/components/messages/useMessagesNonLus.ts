import { useEffect, useState } from "react";
import { useLocation } from "react-router-dom";
import { apiClient } from "../../api/client";
import type { MessagesNonLusDto } from "../../types/models";
import { EVENEMENT_MESSAGES } from "./evenements";
import { useTempsReel } from "./tempsReel";

/**
 * Messages non lus de l'utilisateur (total et par réservation) : relus à chaque changement de page,
 * après lecture d'un fil et en temps réel (nouveau message reçu, messages lus sur un autre onglet).
 */
export function useNonLus(
  actif: boolean,
  /** Onglets de reservation/lister (AVenir, Demandes…) : non-lus de chacun (GET messages/non-lus?filtre=…). */
  filtres: readonly string[] = [],
) {
  const { pathname } = useLocation();
  const [donnees, setDonnees] = useState<MessagesNonLusDto | null>(null);
  const [version, setVersion] = useState(0);
  const [parFiltre, setParFiltre] = useState<Record<string, number>>({});
  const cleFiltres = filtres.join(",");
  const relire = () => setVersion((v) => v + 1);

  useEffect(() => {
    if (!actif) return;
    const relireSurEvenement = () => setVersion((v) => v + 1);
    window.addEventListener(EVENEMENT_MESSAGES, relireSurEvenement);
    return () => window.removeEventListener(EVENEMENT_MESSAGES, relireSurEvenement);
  }, [actif]);
  useTempsReel("nouveauMessage", (m) => !m.deMoi && relire(), actif);
  useTempsReel("messagesLus", relire, actif);

  useEffect(() => {
    if (!actif) return;
    let annule = false;
    apiClient
      .get<MessagesNonLusDto>("messages/non-lus")
      .then((d) => {
        if (!annule) setDonnees(d ?? null);
      })
      .catch(() => {
        // Pastilles facultatives : une erreur ne doit pas gêner la navigation.
      });
    if (cleFiltres)
      Promise.all(
        cleFiltres
          .split(",")
          .map((f) =>
            apiClient.get<MessagesNonLusDto>(`messages/non-lus?filtre=${f}`).then((d) => [f, d?.total ?? 0] as const),
          ),
      )
        .then((valeurs) => {
          if (!annule) setParFiltre(Object.fromEntries(valeurs));
        })
        .catch(() => {
          // Pastilles facultatives.
        });
    return () => {
      annule = true;
    };
  }, [actif, pathname, version, cleFiltres]);

  const parReservation = new Map((donnees?.parReservation ?? []).map((p) => [p.reservationId, p.nombre]));
  return {
    /** Faux tant que le premier chargement n'est pas arrivé. */
    charge: actif && donnees !== null,
    total: actif ? (donnees?.total ?? 0) : 0,
    parReservation,
    /** Non-lus de chaque onglet demandé (toutes pages confondues). */
    parFiltre,
  };
}

/** Total des messages non lus (pastille de la barre de navigation). */
export function useMessagesNonLus(actif: boolean) {
  return useNonLus(actif).total;
}
