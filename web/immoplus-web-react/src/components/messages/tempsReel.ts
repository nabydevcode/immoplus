import { useEffect, useRef } from "react";
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import { API_URL } from "../../api/client";
import type { MessageDto } from "../../types/models";

/** Événements poussés par le serveur (MessagesHub côté API). */
export interface EvenementsTempsReel {
  nouveauMessage: MessageDto;
  messagesLus: { reservationId: number; lecteurId: number; dateLecture: string };
  ecrit: { reservationId: number; auteurId: number };
}

// Une seule connexion WebSocket partagée par toute l'application, ouverte à la première écoute.
let connexion: HubConnection | null = null;
let jetonConnexion: string | null = null;
let demarrage: Promise<void> | null = null;

function obtenirConnexion(): HubConnection | null {
  const jeton = localStorage.getItem("authToken");
  // Déconnecté, ou autre compte : on repart d'une connexion neuve.
  if (connexion && jeton !== jetonConnexion) {
    void connexion.stop();
    connexion = null;
    demarrage = null;
  }
  if (!jeton) return null;
  if (!connexion) {
    jetonConnexion = jeton;
    connexion = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/messages`, { accessTokenFactory: () => localStorage.getItem("authToken") ?? "" })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
  }
  if (connexion.state === HubConnectionState.Disconnected && !demarrage) {
    const c = connexion;
    demarrage = c.start().catch(() => {
      // Serveur injoignable : l'actualisation périodique des pages prend le relais ; nouvel essai à la prochaine écoute.
    });
    void demarrage.finally(() => {
      if (connexion === c) demarrage = null;
    });
  }
  return connexion;
}

/** Écoute un événement temps réel tant que le composant est affiché (le gestionnaire le plus récent est toujours utilisé). */
export function useTempsReel<K extends keyof EvenementsTempsReel>(
  evenement: K,
  gestionnaire: (donnees: EvenementsTempsReel[K]) => void,
  actif = true,
) {
  const dernier = useRef(gestionnaire);
  useEffect(() => {
    dernier.current = gestionnaire;
  });
  useEffect(() => {
    if (!actif) return;
    const c = obtenirConnexion();
    if (!c) return;
    const recevoir = (donnees: EvenementsTempsReel[K]) => dernier.current(donnees);
    c.on(evenement, recevoir);
    return () => c.off(evenement, recevoir);
  }, [evenement, actif]);
}

/** Prévient l'autre participant qu'on est en train d'écrire (sans effet si la connexion n'est pas ouverte). */
export function signalerEcriture(reservationId: number) {
  const c = obtenirConnexion();
  if (c?.state === HubConnectionState.Connected) void c.invoke("Ecrit", reservationId).catch(() => {});
}
