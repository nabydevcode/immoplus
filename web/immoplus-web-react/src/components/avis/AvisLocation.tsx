import { useState } from "react";
import { CheckCircle2, Star } from "lucide-react";
import type { LocationDto } from "../../types/models";
import { classeBoutonPrincipal } from "../admin/outils";
import { formaterJour, jourLocal } from "../sejour/dates";
import FormulaireAvis from "./FormulaireAvis";
import { avisBailleurPossible, avisLocatairePossible } from "./locations";

/**
 * Avis sur une location longue durée : le locataire note le logement et le bailleur, le bailleur note le locataire.
 * N'affiche rien tant que l'avis n'est pas possible.
 */
export default function AvisLocation({
  location,
  pourLogement,
  onEnvoye,
}: {
  location: LocationDto;
  /** true : avis du locataire sur le logement ; false : avis du bailleur sur le locataire. */
  pourLogement: boolean;
  onEnvoye: () => void;
}) {
  const [aujourdhui] = useState(jourLocal);
  const [ouvert, setOuvert] = useState(false);
  const [envoye, setEnvoye] = useState(false);
  const donne = pourLogement ? location.avisLocataireDonne : location.avisBailleurDonne;
  const possible = pourLogement
    ? avisLocatairePossible(location, aujourdhui)
    : avisBailleurPossible(location, aujourdhui);

  if (envoye || donne) {
    return (
      <p className="flex items-center gap-2 text-sm text-green-700" role="status">
        <CheckCircle2 size={16} className="shrink-0" />
        {pourLogement
          ? "Votre avis sur ce logement est publié. Merci !"
          : `Votre avis sur ${location.locataireNom} est enregistré.`}
      </p>
    );
  }
  if (!possible) return null;

  if (ouvert) {
    return (
      <FormulaireAvis
        locationId={location.id}
        pourLogement={pourLogement}
        cible={location.locataireNom}
        dateLimite={location.avisPossibleJusquau}
        onEnvoye={() => {
          setEnvoye(true);
          setOuvert(false);
          onEnvoye();
        }}
        onAnnuler={() => setOuvert(false)}
      />
    );
  }
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-immo-gold/30 bg-immo-gold/10 px-4 py-3 text-sm">
      <p className="text-gray-800">
        {location.statut === "EnCours"
          ? pourLogement
            ? "Vous louez ce logement depuis plus de 6 mois : partagez votre expérience."
            : `${location.locataireNom} loue votre logement depuis plus de 6 mois : vous pouvez l'évaluer.`
          : pourLogement
            ? "Location terminée : partagez votre expérience."
            : `Location terminée : évaluez ${location.locataireNom}.`}
        {location.avisPossibleJusquau && location.statut !== "EnCours" && (
          <span className="block text-xs text-gray-500">
            Possible jusqu'au {formaterJour(location.avisPossibleJusquau.slice(0, 10))}.
          </span>
        )}
      </p>
      <button type="button" onClick={() => setOuvert(true)} className={classeBoutonPrincipal(true)}>
        <Star size={14} />
        Laisser un avis
      </button>
    </div>
  );
}
