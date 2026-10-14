import { useState } from "react";
import { Star } from "lucide-react";

/** Note affichée en étoiles (arrondie à la demi-étoile près visuellement par remplissage). */
export function NoteEtoiles({ note, taille = 14 }: { note: number; taille?: number }) {
  return (
    <span className="inline-flex items-center gap-0.5" aria-label={`${note} sur 5`} role="img">
      {[1, 2, 3, 4, 5].map((i) => (
        <Star
          key={i}
          size={taille}
          className={i <= Math.round(note) ? "text-immo-gold" : "text-gray-300"}
          fill="currentColor"
        />
      ))}
    </span>
  );
}

/** Saisie d'une note de 1 à 5 étoiles. */
export function SaisieEtoiles({
  valeur,
  onChange,
  libelle,
  taille = 26,
}: {
  valeur: number | null;
  onChange: (note: number) => void;
  libelle: string;
  taille?: number;
}) {
  const [survol, setSurvol] = useState<number | null>(null);
  const affichee = survol ?? valeur ?? 0;
  return (
    <div
      className="inline-flex items-center gap-0.5"
      role="radiogroup"
      aria-label={libelle}
      onMouseLeave={() => setSurvol(null)}
    >
      {[1, 2, 3, 4, 5].map((i) => (
        <button
          key={i}
          type="button"
          role="radio"
          aria-checked={valeur === i}
          aria-label={`${i} étoile${i > 1 ? "s" : ""} sur 5`}
          onClick={() => onChange(i)}
          onMouseEnter={() => setSurvol(i)}
          className="p-0.5 rounded focus:outline-none focus-visible:ring-2 focus-visible:ring-immo-gold/50"
        >
          <Star size={taille} className={i <= affichee ? "text-immo-gold" : "text-gray-300"} fill="currentColor" />
        </button>
      ))}
    </div>
  );
}
