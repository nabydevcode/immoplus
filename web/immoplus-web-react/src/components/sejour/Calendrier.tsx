import { ChevronLeft, ChevronRight } from "lucide-react";
import { ajouterMois, casesDuMois, JOURS_SEMAINE, titreMois } from "./dates";

export interface EtatJour {
  /** libre, passé, réservé (par un voyageur) ou bloqué (par l'hôte). */
  ton: "libre" | "passe" | "reserve" | "bloque";
  cliquable: boolean;
  /** Infobulle (ex. motif du blocage). */
  titre?: string;
}

const classesTon: Record<EtatJour["ton"], string> = {
  libre: "text-gray-800 hover:bg-immo-gold/20",
  passe: "text-gray-300",
  reserve: "bg-gray-100 text-gray-400 line-through",
  bloque: "bg-gray-100 text-gray-400 line-through",
};

/** Variante « propriétaire » : les nuits réservées et bloquées se distinguent. */
const classesTonProprietaire: Record<EtatJour["ton"], string> = {
  ...classesTon,
  reserve: "bg-blue-50 text-blue-700",
  bloque: "bg-gray-200 text-gray-500 line-through",
};

function Mois({
  mois,
  etatJour,
  debut,
  fin,
  onChoisir,
  proprietaire,
}: {
  mois: string;
  etatJour: (iso: string) => EtatJour;
  debut: string | null;
  fin: string | null;
  onChoisir: (iso: string) => void;
  proprietaire: boolean;
}) {
  const tons = proprietaire ? classesTonProprietaire : classesTon;
  return (
    <div className="min-w-0">
      <p className="text-center font-bold capitalize mb-3">{titreMois(mois)}</p>
      <div className="grid grid-cols-7 text-center text-[11px] font-semibold text-gray-400 mb-1">
        {JOURS_SEMAINE.map((j, i) => (
          <span key={i}>{j}</span>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-y-1">
        {casesDuMois(mois).map((iso, i) => {
          if (!iso) return <span key={`vide${i}`} />;
          const etat = etatJour(iso);
          const extremite = iso === debut || iso === fin;
          const dansPlage = debut !== null && fin !== null && iso > debut && iso < fin;
          return (
            <button
              key={iso}
              type="button"
              disabled={!etat.cliquable}
              onClick={() => onChoisir(iso)}
              title={etat.titre}
              aria-label={iso}
              aria-pressed={extremite || dansPlage}
              className={`h-9 text-sm rounded-lg transition-colors disabled:cursor-not-allowed ${
                extremite
                  ? "bg-immo-dark text-white font-bold"
                  : dansPlage
                    ? "bg-immo-gold/25 text-immo-dark font-semibold"
                    : `${tons[etat.ton]} ${!etat.cliquable && etat.ton === "libre" ? "text-gray-300 hover:bg-transparent" : ""}`
              }`}
            >
              {Number(iso.slice(8))}
            </button>
          );
        })}
      </div>
    </div>
  );
}

/**
 * Calendrier léger de deux mois (un sous l'autre sur mobile).
 * `debut` et `fin` sont les jours mis en évidence (bornes incluses) ; la logique de sélection reste au parent.
 */
export default function Calendrier({
  mois,
  onChangerMois,
  moisMin,
  moisMax,
  etatJour,
  debut = null,
  fin = null,
  onChoisir,
  proprietaire = false,
}: {
  /** Premier mois affiché (« AAAA-MM »). */
  mois: string;
  onChangerMois: (mois: string) => void;
  moisMin: string;
  moisMax: string;
  etatJour: (iso: string) => EtatJour;
  debut?: string | null;
  fin?: string | null;
  onChoisir: (iso: string) => void;
  proprietaire?: boolean;
}) {
  const suivant = ajouterMois(mois, 1);
  return (
    <div className="relative">
      <div className="absolute inset-x-0 top-0 flex justify-between pointer-events-none">
        <button
          type="button"
          onClick={() => onChangerMois(ajouterMois(mois, -1))}
          disabled={mois <= moisMin}
          aria-label="Mois précédent"
          className="pointer-events-auto w-8 h-8 -mt-1 rounded-full flex items-center justify-center hover:bg-black/5 disabled:opacity-30 disabled:hover:bg-transparent"
        >
          <ChevronLeft size={18} />
        </button>
        <button
          type="button"
          onClick={() => onChangerMois(suivant)}
          disabled={suivant >= moisMax}
          aria-label="Mois suivant"
          className="pointer-events-auto w-8 h-8 -mt-1 rounded-full flex items-center justify-center hover:bg-black/5 disabled:opacity-30 disabled:hover:bg-transparent"
        >
          <ChevronRight size={18} />
        </button>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-6">
        {[mois, suivant].map((m) => (
          <Mois
            key={m}
            mois={m}
            etatJour={etatJour}
            debut={debut}
            fin={fin}
            onChoisir={onChoisir}
            proprietaire={proprietaire}
          />
        ))}
      </div>
    </div>
  );
}
