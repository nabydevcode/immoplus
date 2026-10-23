import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../../api/client";
import type { MessageDto, PagedResultDto } from "../../types/models";
import { messageErreur } from "../admin/outils";
import { avecPage, normaliserPage } from "../admin/pagination";

/** Messages demandés à la fois (les plus récents d'abord côté API). */
export const MESSAGES_PAR_PAGE = 30;

/** Ordre d'affichage : chronologique (date d'envoi puis numéro). */
const chronologique = (a: MessageDto, b: MessageDto) => a.dateEnvoi.localeCompare(b.dateEnvoi) || a.id - b.id;

/** Fusionne des messages reçus avec ceux déjà affichés : la version reçue remplace l'ancienne (accusé « Lu »). */
function fusionner(deja: MessageDto[], recus: MessageDto[]) {
  const parId = new Map(deja.map((m) => [m.id, m]));
  for (const m of recus) parId.set(m.id, m);
  return [...parId.values()].sort(chronologique);
}

/**
 * Fil d'une réservation chargé par pages : les 30 plus récents, puis « Messages précédents » à la demande
 * (GET reservation/{id}/messages?avant={n° du plus ancien affiché} : curseur stable pendant que la conversation avance).
 * `recharger` relit la page la plus récente sans perdre les messages plus anciens déjà affichés.
 * Une route non paginée (tableau simple) est acceptée : tout le fil arrive d'un coup.
 */
export function useFilMessages(reservationId: number) {
  const [messages, setMessages] = useState<MessageDto[] | null>(null);
  const [erreur, setErreur] = useState<string | null>(null);
  // Des messages plus anciens que ceux affichés restent à charger.
  const [anciensRestants, setAnciensRestants] = useState(false);
  const [chargementPrecedents, setChargementPrecedents] = useState(false);
  const [version, setVersion] = useState(0);
  const enCours = useRef(false);
  const premiereReponse = useRef(true);

  /** Page la plus récente, ou (avant = n° du plus ancien affiché) les messages qui le précèdent. */
  const lire = useCallback(
    async (avant?: number) => {
      const chemin = `reservation/${reservationId}/messages${avant ? `?avant=${avant}` : ""}`;
      const reponse = await apiClient.get<PagedResultDto<MessageDto> | MessageDto[]>(
        avecPage(chemin, 1, MESSAGES_PAR_PAGE),
      );
      return normaliserPage(reponse, 1, MESSAGES_PAR_PAGE, false);
    },
    [reservationId],
  );

  // Page la plus récente : au premier affichage, puis à chaque relecture (nouveau message, filet périodique).
  useEffect(() => {
    let annule = false;
    lire()
      .then((resultat) => {
        if (annule) return;
        // Premier chargement : y a-t-il plus ancien ? (une relecture ne touche pas aux messages plus anciens déjà affichés)
        if (premiereReponse.current) {
          premiereReponse.current = false;
          setAnciensRestants(resultat.totalPages > 1);
        }
        setMessages((deja) => fusionner(deja ?? [], resultat.donnees));
        setErreur(null);
      })
      .catch((err) => {
        if (!annule) setErreur(messageErreur(err, "Impossible de charger les messages."));
      });
    return () => {
      annule = true;
    };
  }, [lire, version]);

  const plusAncien = messages?.[0]?.id;
  const chargerPrecedents = useCallback(async () => {
    if (enCours.current || plusAncien === undefined) return;
    enCours.current = true;
    setChargementPrecedents(true);
    try {
      const resultat = await lire(plusAncien);
      setMessages((deja) => fusionner(deja ?? [], resultat.donnees));
      setAnciensRestants(resultat.totalPages > 1);
    } catch (err) {
      setErreur(messageErreur(err, "Impossible de charger les messages précédents."));
    } finally {
      enCours.current = false;
      setChargementPrecedents(false);
    }
  }, [lire, plusAncien]);

  const recharger = useCallback(() => setVersion((v) => v + 1), []);
  /** Ajoute ou met à jour des messages localement (envoi, temps réel). */
  const ajouter = useCallback((recus: MessageDto[]) => setMessages((deja) => deja && fusionner(deja, recus)), []);
  const modifier = useCallback(
    (maj: (m: MessageDto) => MessageDto) => setMessages((deja) => deja && deja.map(maj)),
    [],
  );

  return {
    messages,
    chargement: messages === null && erreur === null,
    erreur,
    aPrecedents: messages !== null && anciensRestants,
    chargementPrecedents,
    chargerPrecedents,
    recharger,
    ajouter,
    modifier,
  };
}
