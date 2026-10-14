/** Prévient la barre de navigation que des messages ont été lus ou envoyés (pastille des non lus). */
export const EVENEMENT_MESSAGES = "immoplus:messages";

export const signalerMessages = () => window.dispatchEvent(new Event(EVENEMENT_MESSAGES));

/** Coordonnées refusées par l'API tant que la réservation n'est pas confirmée (même règle, pour prévenir). */
export function contientCoordonnees(texte: string) {
  const chiffres = texte.replace(/[\s.-]/g, "");
  return /\d{8,}/.test(chiffres) || /[^\s@]+@[^\s@]+\.[^\s@]+/.test(texte);
}
