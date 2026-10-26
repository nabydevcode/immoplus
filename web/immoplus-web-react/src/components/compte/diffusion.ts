/**
 * Messages entre les onglets du même navigateur (aucune requête serveur) : la page /confirmer-email prévient
 * l'écran « Vérifiez votre boîte email » resté ouvert dans un autre onglet que l'adresse vient d'être confirmée.
 * BroadcastChannel quand il existe ; sinon repli sur l'événement « storage » (clé effacée aussitôt écrite).
 */

const CANAL = "immoplus-compte";
/** Clé du repli localStorage : écrite puis effacée immédiatement (rien ne reste stocké). */
const CLE_REPLI = "immoplus-compte-evenement";

export interface MessageCompte {
  type: "emailConfirme";
  /** Adresse confirmée, si l'API l'a renvoyée (absente : l'écran d'attente réagit à toute confirmation). */
  email?: string;
  /** Statut du compte renvoyé par l'API (EnAttente en général, Valide si déjà validé). */
  statut?: string;
}

const avecCanal = () => typeof BroadcastChannel !== "undefined";

function estMessageCompte(valeur: unknown): valeur is MessageCompte {
  return typeof valeur === "object" && valeur !== null && (valeur as MessageCompte).type === "emailConfirme";
}

/** Prévient les autres onglets (sans effet si le navigateur ne le permet pas). */
export function publierCompte(message: MessageCompte) {
  try {
    if (avecCanal()) {
      const canal = new BroadcastChannel(CANAL);
      canal.postMessage(message);
      canal.close();
      return;
    }
    // Repli : l'écriture déclenche « storage » dans les autres onglets ; la clé est effacée aussitôt.
    localStorage.setItem(CLE_REPLI, JSON.stringify({ ...message, horodatage: Date.now() }));
    localStorage.removeItem(CLE_REPLI);
  } catch {
    // Signal facultatif (stockage bloqué, navigation privée…) : l'utilisateur garde le message de la page.
  }
}

/** Écoute les messages des autres onglets ; renvoie la fonction d'arrêt. */
export function ecouterCompte(gestionnaire: (message: MessageCompte) => void): () => void {
  if (avecCanal()) {
    const canal = new BroadcastChannel(CANAL);
    canal.onmessage = (e: MessageEvent) => {
      if (estMessageCompte(e.data)) gestionnaire(e.data);
    };
    return () => canal.close();
  }
  const surStockage = (e: StorageEvent) => {
    if (e.key !== CLE_REPLI || !e.newValue) return;
    try {
      const donnees: unknown = JSON.parse(e.newValue);
      if (estMessageCompte(donnees)) gestionnaire(donnees);
    } catch {
      // Valeur illisible : ignorée.
    }
  };
  window.addEventListener("storage", surStockage);
  return () => window.removeEventListener("storage", surStockage);
}

/** Même adresse email (casse et espaces ignorés) ; sans adresse dans le message, toute confirmation est retenue. */
export const concerneAdresse = (message: MessageCompte, email: string) =>
  !message.email || message.email.trim().toLowerCase() === email.trim().toLowerCase();
