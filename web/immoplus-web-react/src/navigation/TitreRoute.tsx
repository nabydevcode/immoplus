import { useEffect } from "react";
import { useLocation } from "react-router-dom";
import { titreDeRoute } from "./titres";

/** Met à jour le titre de l'onglet à chaque changement de page. */
export default function TitreRoute() {
  const { pathname } = useLocation();
  useEffect(() => {
    document.title = titreDeRoute(pathname);
  }, [pathname]);
  return null;
}
