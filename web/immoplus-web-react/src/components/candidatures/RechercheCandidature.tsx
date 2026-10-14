import type { FormEvent } from "react";
import { Search, X } from "lucide-react";
import type { useRechercheCandidature } from "./recherche";

export function BarreRecherche({
  recherche,
  onTrouve,
}: {
  recherche: ReturnType<typeof useRechercheCandidature>;
  /** Appelé avec l'id du dossier quand une recherche aboutit à un seul résultat (pour l'ouvrir directement). */
  onTrouve: (id: number) => void;
}) {
  const valider = async (e: FormEvent) => {
    e.preventDefault();
    const id = await recherche.chercher();
    if (id !== null) onTrouve(id);
  };

  return (
    <form onSubmit={valider} className="mb-6">
      <ChampRecherche
        valeur={recherche.texte}
        onChange={recherche.setTexte}
        onEffacer={recherche.effacer}
        placeholder="N° de dossier (ex. 000048), nom du candidat ou adresse"
        libelle="Rechercher une candidature"
      />
      {recherche.numero !== null && recherche.resultats?.length === 0 && !recherche.message && (
        <p className="text-xs text-gray-500 mt-1.5">
          {recherche.enCours ? "Recherche..." : "Appuyez sur Entrée pour rechercher ce dossier."}
        </p>
      )}
      {recherche.message && <p className="text-sm text-red-600 mt-1.5">{recherche.message}</p>}
    </form>
  );
}

/** Champ de recherche avec loupe et bouton d'effacement. */
export function ChampRecherche({
  valeur,
  onChange,
  onEffacer,
  placeholder,
  libelle,
}: {
  valeur: string;
  onChange: (v: string) => void;
  onEffacer: () => void;
  placeholder: string;
  libelle: string;
}) {
  return (
    <div className="relative max-w-md">
      <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
      <input
        value={valeur}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label={libelle}
        className="w-full bg-white border border-gray-300 rounded-lg pl-9 pr-9 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-immo-gold/40 focus:border-immo-gold"
      />
      {valeur && (
        <button
          type="button"
          onClick={onEffacer}
          className="absolute right-2 top-1/2 -translate-y-1/2 p-1 text-gray-400 hover:text-gray-700"
          aria-label="Effacer la recherche"
        >
          <X size={16} />
        </button>
      )}
    </div>
  );
}
