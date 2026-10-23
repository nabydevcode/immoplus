import { useEffect, useRef } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { API_URL } from "../../api/client";
import type { MessageDto } from "../../types/models";

/** Événements poussés par le serveur (MessagesHub côté API). */
export interface EvenementsTempsReel {
  nouveauMessage: MessageDto;
  messagesLus: { reservationId: number; lecteurId: number; dateLecture: string };
  ecrit: { reservationId: number; auteurId: number };
}

// Une seule connexion WebSocket partagée par toute l'application, ouverte à la première écoute.
// La bibliothèque SignalR n'est téléchargée qu'à ce moment-là (visiteurs non connectés : jamais).
let bibliotheque: Promise<typeof import("@microsoft/signalr")> | null = null;
let connexion: HubConnection | null = null;
let jetonConnexion: string | null = null;
let demarrage: Promise<void> | null = null;

async function obtenirConnexion(): Promise<HubConnection | null> {
  if (!localStorage.getItem("authToken")) {
    if (connexion) {
      void connexion.stop();
      connexion = null;
      demarrage = null;
    }
    return null;
  }
  bibliotheque ??= import("@microsoft/signalr");
  let signalr: typeof import("@microsoft/signalr");
  try {
    signalr = await bibliotheque;
  } catch {
    // Téléchargement impossible (hors ligne) : nouvel essai à la prochaine écoute.
    bibliotheque = null;
    return null;
  }
  const { HttpTransportType, HubConnectionBuilder, HubConnectionState, LogLevel } = signalr;
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
      // WebSocket direct sans négociation : avec plusieurs instances de l'API, la négociation et la connexion
      // pourraient sinon atterrir sur deux instances différentes.
      .withUrl(`${API_URL}/hubs/messages`, {
        accessTokenFactory: () => localStorage.getItem("authToken") ?? "",
        skipNegotiation: true,
        transport: HttpTransportType.WebSockets,
      })
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
    let annule = false;
    let ecoute: HubConnection | null = null;
    const recevoir = (donnees: EvenementsTempsReel[K]) => dernier.current(donnees);
    void obtenirConnexion().then((c) => {
      if (annule || !c) return;
      ecoute = c;
      c.on(evenement, recevoir);
    });
    return () => {
      annule = true;
      ecoute?.off(evenement, recevoir);
    };
  }, [evenement, actif]);
}

/** Prévient l'autre participant qu'on est en train d'écrire (sans effet si la connexion n'est pas ouverte). */
export function signalerEcriture(reservationId: number) {
  // Signal facultatif : rien n'est envoyé tant que la connexion n'est pas ouverte.
  if (connexion?.state === "Connected") void connexion.invoke("Ecrit", reservationId).catch(() => {});
}
