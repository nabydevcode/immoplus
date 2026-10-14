import type { SelectionInitiale } from "./useSelectionSejour";
import { estJourValide } from "./dates";

/** Dates et voyageurs repris de l'URL (recherche partageable → fiche → réservation). */
export function selectionDepuisUrl(parametres: URLSearchParams): SelectionInitiale {
  const arrivee = parametres.get("arrivee");
  const depart = parametres.get("depart");
  const voyageurs = Number(parametres.get("voyageurs"));
  return {
    arrivee: estJourValide(arrivee) ? arrivee : null,
    depart: estJourValide(depart) ? depart : null,
    voyageurs: Number.isInteger(voyageurs) && voyageurs > 0 ? voyageurs : 1,
  };
}

/** Partie de l'URL à transmettre d'une recherche vers une fiche (« ?arrivee=…&depart=…&voyageurs=… »). */
export function suffixeSejour(parametres: URLSearchParams) {
  const suite = new URLSearchParams();
  for (const cle of ["arrivee", "depart", "voyageurs"]) {
    const valeur = parametres.get(cle);
    if (valeur) suite.set(cle, valeur);
  }
  const texte = suite.toString();
  return texte ? `?${texte}` : "";
}
