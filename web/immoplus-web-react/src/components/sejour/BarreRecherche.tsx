import { useState, type FormEvent } from "react";
import { Search, SlidersHorizontal } from "lucide-react";
import type { VilleDto } from "../../types/models";
import { MessageErreur } from "../admin/ui";
import { classeBoutonPrincipal, classeChamp, useChargement } from "../admin/outils";
import { jourLocal } from "./dates";
import { EQUIPEMENTS } from "./equipements";
import ChoixEquipements from "./ChoixEquipements";

/**
 * Recherche de séjours courts. Les critères sont écrits dans l'URL (lien partageable) :
 * ville, quartier, arrivee, depart, voyageurs, equipements (liste séparée par des virgules), prixMax.
 * Le parent remonte le composant (key) quand l'URL change, pour repartir des critères de l'URL.
 */
export default function BarreRecherche({
  parametres,
  onRechercher,
}: {
  parametres: URLSearchParams;
  onRechercher: (parametres: URLSearchParams) => void;
}) {
  const { donnees: villes } = useChargement<VilleDto[]>("appartement/villes?typeOffre=SejourCourt");
  const [aujourdhui] = useState(jourLocal);
  const [ville, setVille] = useState(parametres.get("ville") ?? "");
  const [quartier, setQuartier] = useState(parametres.get("quartier") ?? "");
  const [arrivee, setArrivee] = useState(parametres.get("arrivee") ?? "");
  const [depart, setDepart] = useState(parametres.get("depart") ?? "");
  const [voyageurs, setVoyageurs] = useState(parametres.get("voyageurs") ?? "");
  const [prixMax, setPrixMax] = useState(parametres.get("prixMax") ?? "");
  const [equipements, setEquipements] = useState<string[]>(
    (parametres.get("equipements") ?? "").split(",").filter((e) => EQUIPEMENTS.some((x) => x.cle === e)),
  );
  const [filtresOuverts, setFiltresOuverts] = useState(equipements.length > 0 || !!prixMax);
  const [erreur, setErreur] = useState<string | null>(null);

  const quartiers = villes?.find((v) => v.ville === ville)?.quartiers ?? [];
  const nombreFiltres = equipements.length + (prixMax ? 1 : 0);

  const rechercher = (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    if (!!arrivee !== !!depart) {
      setErreur("Indiquez à la fois la date d'arrivée et la date de départ.");
      return;
    }
    if (arrivee && depart <= arrivee) {
      setErreur("La date de départ doit être après la date d'arrivée.");
      return;
    }
    const suivants = new URLSearchParams();
    const ajouter = (cle: string, valeur: string) => {
      if (valeur.trim()) suivants.set(cle, valeur.trim());
    };
    ajouter("ville", ville);
    ajouter("quartier", quartier);
    ajouter("arrivee", arrivee);
    ajouter("depart", depart);
    ajouter("voyageurs", Number(voyageurs) > 0 ? voyageurs : "");
    ajouter("equipements", equipements.join(","));
    ajouter("prixMax", Number(prixMax) > 0 ? prixMax : "");
    onRechercher(suivants);
  };

  const effacer = () => onRechercher(new URLSearchParams());
  const criteres = [...parametres.keys()].length > 0;

  return (
    <form onSubmit={rechercher} className="bg-white rounded-2xl border border-black/5 shadow-sm p-4 sm:p-5 mb-8">
      <div className="grid grid-cols-2 lg:grid-cols-[1.2fr_1.2fr_1fr_1fr_0.7fr_auto] gap-3 items-end">
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
            list="quartiers-recherche"
            placeholder={ville ? "Tous" : "Ex. Kipé"}
            className={classeChamp}
          />
          <datalist id="quartiers-recherche">
            {(ville ? quartiers : (villes ?? []).flatMap((v) => v.quartiers)).map((q) => (
              <option key={q} value={q} />
            ))}
          </datalist>
        </label>
        <label className="text-xs font-semibold text-gray-600">
          Arrivée
          <input
            type="date"
            min={aujourdhui}
            value={arrivee}
            onChange={(e) => {
              setArrivee(e.target.value);
              if (depart && e.target.value && depart <= e.target.value) setDepart("");
            }}
            className={classeChamp}
          />
        </label>
        <label className="text-xs font-semibold text-gray-600">
          Départ
          <input
            type="date"
            min={arrivee || aujourdhui}
            value={depart}
            onChange={(e) => setDepart(e.target.value)}
            className={classeChamp}
          />
        </label>
        <label className="text-xs font-semibold text-gray-600">
          Voyageurs
          <input
            type="number"
            min="1"
            max="50"
            value={voyageurs}
            onChange={(e) => setVoyageurs(e.target.value)}
            placeholder="1"
            className={classeChamp}
          />
        </label>
        <button type="submit" className={`${classeBoutonPrincipal()} h-[38px] col-span-1`}>
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
          <button type="button" onClick={effacer} className="text-sm font-semibold underline text-gray-600">
            Effacer la recherche
          </button>
        )}
      </div>

      {filtresOuverts && (
        <div className="mt-4 pt-4 border-t border-black/5 flex flex-col gap-4">
          <label className="text-xs font-semibold text-gray-600 max-w-xs">
            Prix max par nuit (GNF)
            <input
              type="number"
              min="0"
              step="1000"
              value={prixMax}
              onChange={(e) => setPrixMax(e.target.value)}
              className={classeChamp}
            />
          </label>
          <ChoixEquipements valeur={equipements} onChange={setEquipements} />
        </div>
      )}

      {erreur && (
        <div className="mt-3">
          <MessageErreur>{erreur}</MessageErreur>
        </div>
      )}
    </form>
  );
}
