import type { LocationDto } from "../../types/models";

/** Fenêtre d'avis d'une location longue durée (calculée par l'API : après la fin, ou après 6 mois de location). */
function avisOuvert(l: LocationDto, aujourdhui: string) {
  if (l.avisPossible !== undefined) return l.avisPossible;
  return !!l.avisPossibleJusquau && aujourdhui <= l.avisPossibleJusquau.slice(0, 10);
}
export const avisLocatairePossible = (l: LocationDto, aujourdhui: string) =>
  avisOuvert(l, aujourdhui) && !l.avisLocataireDonne;
export const avisBailleurPossible = (l: LocationDto, aujourdhui: string) =>
  avisOuvert(l, aujourdhui) && !l.avisBailleurDonne;
