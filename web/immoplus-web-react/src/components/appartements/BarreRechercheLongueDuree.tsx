import { useState, type FormEvent } from "react";
import { Search, SlidersHorizontal } from "lucide-react";
import type { VilleDto } from "../../types/models";
import { classeBoutonPrincipal, classeChamp, useChargement } from "../admin/outils";
import { EQUIPEMENTS } from "../sejour/equipements";
import ChoixEquipements from "../sejour/ChoixEquipements";

/**
 * Recherche de locations longue durée. Les critères sont écrits dans l'URL (lien partageable) :
 * ville, quartier, loyerMax, chambresMin, avanceMax (mois), equipements (liste séparée par des virgules).
 * Le parent remonte le composant (key) quand l'URL change, pour repartir des critères de l'URL.
 */
export default function BarreRechercheLongueDuree({
  parametres,
  onRechercher,
}: {
  parametres: URLSearchParams;
  onRechercher: (parametres: URLSearchParams) => void;
}) {
  const { donnees: villes } = useChargement<VilleDto[]>("appartement/villes?typeOffre=LongueDuree");
  const [ville, setVille] = useState(parametres.get("ville") ?? "");
  const [quartier, setQuartier] = useState(parametres.get("quartier") ?? "");
  const [loyerMax, setLoyerMax] = useState(parametres.get("loyerMax") ?? "");
  const [chambresMin, setChambresMin] = useState(parametres.get("chambresMin") ?? "");
  const [avanceMax, setAvanceMax] = useState(parametres.get("avanceMax") ?? "");
  const [equipements, setEquipements] = useState<string[]>(
    (parametres.get("equipements") ?? "").split(",").filter((e) => EQUIPEMENTS.some((x) => x.cle === e)),
  );
  const [filtresOuverts, setFiltresOuverts] = useState(equipements.length > 0 || !!avanceMax);

  const quartiers = villes?.find((v) => v.ville === ville)?.quartiers ?? [];
  const nombreFiltres = equipements.length + (avanceMax ? 1 : 0);

  const rechercher = (e: FormEvent) => {
    e.preventDefault();
    const suivants = new URLSearchParams();
    const ajouter = (cle: string, valeur: string) => {
      if (valeur.trim()) suivants.set(cle, valeur.trim());
    };
    ajouter("ville", ville);
    ajouter("quartier", quartier);
    ajouter("loyerMax", Number(loyerMax) > 0 ? loyerMax : "");
    ajouter("chambresMin", Number(chambresMin) > 0 ? chambresMin : "");
    ajouter("avanceMax", Number(avanceMax) > 0 ? avanceMax : "");
    ajouter("equipements", equipements.join(","));
    onRechercher(suivants);
  };

  const criteres = [...parametres.keys()].length > 0;

  return (
    <form onSubmit={rechercher} className="bg-white rounded-2xl border border-black/5 shadow-sm p-4 sm:p-5 mb-8">
      <div className="grid grid-cols-2 lg:grid-cols-[1.2fr_1.2fr_1fr_0.8fr_auto] gap-3 items-end">
        <label className="text-xs font-semibold text-gray-600">
          Ville
          <select
            value={ville}
            onChange={(e) => {
              setVille(e.target.value);
              setQuartier("");
            }}
            className={classeChamp}
          >
            <option value="">Toutes</option>
            {villes?.map((v) => (
              <option key={v.ville} value={v.ville}>
                {v.ville}
              </option>
            ))}
            {/* Ville de l'URL absente de la liste : on la garde sélectionnable. */}
            {ville && villes && !villes.some((v) => v.ville === ville) && <option value={ville}>{ville}</option>}
          </select>
        </label>
        <label className="text-xs font-semibold text-gray-600">
          Quartier
          <input
            value={quartier}
            onChange={(e) => setQuartier(e.target.value)}
            list="quartiers-longue-duree"
            placeholder={ville ? "Tous" : "Ex. Kipé"}
            className={classeChamp}
          />
          <datalist id="quartiers-longue-duree">
            {(ville ? quartiers : (villes ?? []).flatMap((v) => v.quartiers)).map((q) => (
              <option key={q} value={q} />
            ))}
          </datalist>
        </label>
        <label className="text-xs font-semibold text-gray-600">
          Loyer max / mois (GNF)
          <input
            type="number"
            min="0"
            step="50000"
            value={loyerMax}
            onChange={(e) => setLoyerMax(e.target.value)}
            className={classeChamp}
          />
        </label>
        <label className="text-xs font-semibold text-gray-600">
          Chambres min.
          <input
            type="number"
            min="1"
            max="20"
            value={chambresMin}
            onChange={(e) => setChambresMin(e.target.value)}
            placeholder="1"
            className={classeChamp}
          />
        </label>
        <button type="submit" className={`${classeBoutonPrincipal()} h-[38px] col-span-2 lg:col-span-1`}>
          <Search size={16} />
          Rechercher
        </button>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 mt-3">
        <button
          type="button"
          onClick={() => setFiltresOuverts((v) => !v)}
          aria-expanded={filtresOuverts}
          className="inline-flex items-center gap-1.5 text-sm font-semibold text-gray-700 hover:text-immo-dark"
        >
          <SlidersHorizontal size={15} />
          Plus de filtres{nombreFiltres > 0 ? ` (${nombreFiltres})` : ""}
        </button>
        {criteres && (
          <button
            type="button"
            onClick={() => onRechercher(new URLSearchParams())}
            className="text-sm font-semibold underline text-gray-600"
          >
            Effacer la recherche
          </button>
        )}
      </div>

      {filtresOuverts && (
        <div className="mt-4 pt-4 border-t border-black/5 flex flex-col gap-4">
          <label className="text-xs font-semibold text-gray-600 max-w-xs">
            Avance exigée au plus (mois)
            <input
              type="number"
              min="1"
              max="24"
              value={avanceMax}
              onChange={(e) => setAvanceMax(e.target.value)}
              className={classeChamp}
            />
          </label>
          <ChoixEquipements valeur={equipements} onChange={setEquipements} />
        </div>
      )}
    </form>
  );
}
