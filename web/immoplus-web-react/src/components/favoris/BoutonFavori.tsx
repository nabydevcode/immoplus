import { useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { Heart } from "lucide-react";
import { useAuth } from "../../context/auth";
import { messageErreur } from "../admin/outils";
import { useFavoris } from "./favoris";

/**
 * Cœur « favori » d'une annonce. Visiteur : renvoie vers la connexion (puis revient ici).
 * Absent pour l'administrateur. `surPhoto` : pastille ronde posée sur la photo d'une carte.
 */
export default function BoutonFavori({
  appartementId,
  surPhoto = false,
}: {
  appartementId: number;
  surPhoto?: boolean;
}) {
  const { estConnecte, roleUtilisateur } = useAuth();
  const { estFavori, basculer } = useFavoris();
  const navigate = useNavigate();
  const { pathname, search } = useLocation();
  const [erreur, setErreur] = useState<string | null>(null);

  if (estConnecte && roleUtilisateur === "Admin") return null;
  const favori = estConnecte && estFavori(appartementId);
  const libelle = favori ? "Retirer des favoris" : "Ajouter aux favoris";

  const cliquer = async () => {
    setErreur(null);
    if (!estConnecte) {
      navigate(`/login?retour=${encodeURIComponent(pathname + search)}`);
      return;
    }
    try {
      await basculer(appartementId);
    } catch (err) {
      setErreur(messageErreur(err, "Impossible de modifier vos favoris."));
    }
  };

  const coeur = (
    <Heart size={surPhoto ? 18 : 16} className={favori ? "text-red-500" : ""} fill={favori ? "currentColor" : "none"} />
  );

  if (surPhoto) {
    return (
      <span className="relative inline-flex">
        <button
          type="button"
          onClick={cliquer}
          aria-label={libelle}
          aria-pressed={favori}
          title={erreur ?? libelle}
          className="w-9 h-9 rounded-full bg-white/90 hover:bg-white text-gray-700 shadow flex items-center justify-center transition-transform active:scale-90"
        >
          {coeur}
        </button>
        {/* Sur mobile, le title n'est pas visible : l'erreur s'affiche sous le cœur. */}
        {erreur && (
          <span
            role="alert"
            className="absolute top-full right-0 mt-1 w-44 bg-white text-red-600 text-xs rounded-lg shadow px-2 py-1"
          >
            {erreur}
          </span>
        )}
      </span>
    );
  }
  return (
    <span className="inline-flex flex-col">
      <button
        type="button"
        onClick={cliquer}
        aria-pressed={favori}
        className="inline-flex items-center gap-1.5 text-sm font-semibold px-3 py-2 rounded-lg border border-gray-300 text-gray-700 hover:bg-gray-50 transition-colors"
      >
        {coeur}
        {favori ? "Enregistré" : "Enregistrer"}
      </button>
      {erreur && (
        <span role="alert" className="text-xs text-red-600 mt-1">
          {erreur}
        </span>
      )}
    </span>
  );
}
