import { Bath, Bed, BedDouble, Users, Zap } from "lucide-react";
import type { AppartementDto } from "../../types/models";
import { equipementsDe } from "./equipements";

/** Badge des logements réservables sans validation de l'hôte. */
export function BadgeInstantanee({ petit }: { petit?: boolean }) {
  return (
    <span
      className={`inline-flex items-center gap-1 bg-immo-gold/15 text-immo-dark font-semibold rounded-full whitespace-nowrap ${
        petit ? "text-[11px] px-2 py-0.5" : "text-xs px-3 py-1.5"
      }`}
    >
      <Zap size={petit ? 11 : 13} className="text-immo-gold" fill="currentColor" />
      Réservation instantanée
    </span>
  );
}

/** Voyageurs, chambres, lits, salles de bain (seulement les valeurs renseignées). */
export function Capacite({ appartement, compact }: { appartement: AppartementDto; compact?: boolean }) {
  const elements = [
    { valeur: appartement.capacite, icone: Users, un: "voyageur", plusieurs: "voyageurs" },
    { valeur: appartement.chambres, icone: BedDouble, un: "chambre", plusieurs: "chambres" },
    { valeur: appartement.lits, icone: Bed, un: "lit", plusieurs: "lits" },
    { valeur: appartement.sallesDeBain, icone: Bath, un: "salle de bain", plusieurs: "salles de bain" },
  ].filter((e) => e.valeur != null);
  if (elements.length === 0) return null;
  return (
    <ul className={`flex flex-wrap ${compact ? "gap-x-3 gap-y-1 text-xs" : "gap-x-5 gap-y-2 text-sm"} text-gray-600`}>
      {elements.map(({ valeur, icone: Icone, un, plusieurs }) => (
        <li key={un} className="inline-flex items-center gap-1.5">
          <Icone size={compact ? 13 : 16} className="text-immo-gold shrink-0" />
          {compact ? valeur : `${valeur} ${valeur! > 1 ? plusieurs : un}`}
          {compact && <span className="sr-only">{valeur! > 1 ? plusieurs : un}</span>}
        </li>
      ))}
    </ul>
  );
}

/** Équipements : icônes seules (carte d'annonce) ou icônes et libellés (fiche). */
export function Equipements({ cles, compact, max }: { cles?: string[]; compact?: boolean; max?: number }) {
  const liste = equipementsDe(cles);
  if (liste.length === 0) return null;
  const affiches = max ? liste.slice(0, max) : liste;
  const reste = liste.length - affiches.length;

  if (compact) {
    return (
      <ul className="flex flex-wrap items-center gap-1.5">
        {affiches.map(({ cle, libelle, icone: Icone }) => (
          <li
            key={cle}
            title={libelle}
            className="w-7 h-7 rounded-full bg-immo-dark/5 text-gray-600 flex items-center justify-center"
          >
            <Icone size={14} />
            <span className="sr-only">{libelle}</span>
          </li>
        ))}
        {reste > 0 && <li className="text-xs text-gray-500 font-semibold">+{reste}</li>}
      </ul>
    );
  }
  return (
    <ul className="grid grid-cols-1 sm:grid-cols-2 gap-3">
      {affiches.map(({ cle, libelle, icone: Icone }) => (
        <li key={cle} className="flex items-center gap-3 text-sm text-gray-700">
          <Icone size={20} className="text-immo-gold shrink-0" />
          {libelle}
        </li>
      ))}
    </ul>
  );
}
