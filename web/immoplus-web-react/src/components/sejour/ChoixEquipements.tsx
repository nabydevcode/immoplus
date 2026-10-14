import { EQUIPEMENTS } from "./equipements";

/** Pastilles « Équipements indispensables » des barres de recherche (séjour court et longue durée). */
export default function ChoixEquipements({
  valeur,
  onChange,
}: {
  valeur: string[];
  onChange: (equipements: string[]) => void;
}) {
  const basculer = (cle: string) => onChange(valeur.includes(cle) ? valeur.filter((e) => e !== cle) : [...valeur, cle]);
  return (
    <fieldset>
      <legend className="text-xs font-semibold text-gray-600 mb-2">Équipements indispensables</legend>
      <div className="flex flex-wrap gap-2">
        {EQUIPEMENTS.map(({ cle, libelle, icone: Icone }) => {
          const actif = valeur.includes(cle);
          return (
            <button
              key={cle}
              type="button"
              onClick={() => basculer(cle)}
              aria-pressed={actif}
              className={`inline-flex items-center gap-1.5 text-xs font-semibold px-3 py-1.5 rounded-full border transition-colors ${
                actif
                  ? "border-immo-dark bg-immo-dark text-white"
                  : "border-gray-300 text-gray-600 hover:border-gray-400"
              }`}
            >
              <Icone size={13} />
              {libelle}
            </button>
          );
        })}
      </div>
    </fieldset>
  );
}
