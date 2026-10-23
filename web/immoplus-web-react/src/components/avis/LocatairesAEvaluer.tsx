import { useState } from "react";
import { Star } from "lucide-react";
import type { LocationDto, PagedResultDto } from "../../types/models";
import { avecPage, listeDe, TAILLE_MAX } from "../admin/pagination";
import { formaterReference, useChargement } from "../admin/outils";
import { jourLocal } from "../sejour/dates";
import AvisLocation from "./AvisLocation";
import { avisBailleurPossible } from "./locations";

/** Bailleur : locations longue durée dont il peut évaluer le locataire (après la fin, ou après 6 mois). */
export default function LocatairesAEvaluer() {
  // Locations du bailleur (100 au plus, les plus récentes) ; filtre=AEvaluer demandé à l'API.
  const { donnees: reponse, recharger } = useChargement<PagedResultDto<LocationDto> | LocationDto[]>(
    avecPage("location/lister?filtre=AEvaluer", 1, TAILLE_MAX),
  );
  const donnees = listeDe(reponse);
  const [aujourdhui] = useState(jourLocal);
  // Liste figée au premier affichage : un avis envoyé reste confirmé à sa place jusqu'au prochain chargement.
  const [affichees, setAffichees] = useState<number[] | null>(null);
  const aEvaluer = donnees.filter((l) => affichees?.includes(l.id) ?? avisBailleurPossible(l, aujourdhui));
  if (aEvaluer.length === 0) return null;

  return (
    <section className="bg-white rounded-2xl border border-black/5 shadow-sm p-5 sm:p-6 mb-8">
      <h2 className="font-bold flex items-center gap-2 mb-1">
        <Star size={18} className="text-immo-gold" fill="currentColor" />
        Locataires à évaluer
      </h2>
      <p className="text-sm text-gray-500 mb-4">
        Votre avis aide les autres bailleurs et hôtes ; il est visible par ceux à qui ce locataire fera une demande.
      </p>
      <ul className="flex flex-col gap-4">
        {aEvaluer.map((l) => (
          <li key={l.id} className="flex flex-col gap-2">
            <p className="text-sm">
              <strong>{l.locataireNom}</strong>
              <span className="text-gray-500">
                {" "}
                · {l.appartementAdresse} · location n° {formaterReference(l.id)}
              </span>
            </p>
            <AvisLocation
              location={l}
              pourLogement={false}
              onEnvoye={() => {
                setAffichees(aEvaluer.map((x) => x.id));
                recharger();
              }}
            />
          </li>
        ))}
      </ul>
    </section>
  );
}
