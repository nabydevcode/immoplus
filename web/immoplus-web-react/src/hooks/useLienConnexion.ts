import { useLocation } from "react-router-dom";

/** Lien vers la connexion qui ramène ensuite sur la page courante (ex. lien d'un email ouvert sans être connecté). */
export function useLienConnexion() {
  const { pathname, search } = useLocation();
  return `/login?retour=${encodeURIComponent(pathname + search)}`;
}
