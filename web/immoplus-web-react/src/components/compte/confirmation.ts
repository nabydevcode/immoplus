import { apiClient } from "../../api/client";

export interface ReponseConfirmationEmail {
  message: string;
  statut: string;
  /** Adresse confirmée (si l'API la renvoie) : sert à prévenir l'écran d'attente des autres onglets. */
  email?: string;
}

// Le jeton est à usage unique : un même lien n'est envoyé qu'une fois à l'API, même si l'effet est rejoué
// (mode strict de React, retour sur la page).
const confirmations = new Map<string, Promise<ReponseConfirmationEmail | undefined>>();

/** Confirme l'adresse email avec le jeton reçu par email (un seul appel par jeton). */
export function confirmerEmailUneFois(jeton: string) {
  let promesse = confirmations.get(jeton);
  if (!promesse) {
    promesse = apiClient.post<ReponseConfirmationEmail>("utilisateur/confirmer-email", { jeton });
    confirmations.set(jeton, promesse);
  }
  return promesse;
}
