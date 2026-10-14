import { Link } from "react-router-dom";
import { MapPin } from "lucide-react";
import type { AppartementDto } from "../../types/models";
import PhotoCouverture from "./PhotoCouverture";
import BoutonFavori from "../favoris/BoutonFavori";
import { BadgeInstantanee, Capacite, Equipements } from "../sejour/CaracteristiquesLogement";
import { localisation } from "../sejour/equipements";
import { ResumeNote } from "../avis/AffichageAvis";
import { BadgeSuperhote } from "../sejour/Confiance";

/** Carte d'une annonce dans les listes (annonces, favoris), avec le cœur des favoris sur la photo. */
export default function CarteAnnonce({
  appartement: a,
  lien,
  afficherIndisponible = false,
}: {
  appartement: AppartementDto;
  lien: string;
  /** Favoris : signale une annonce qui n'est plus disponible. */
  afficherIndisponible?: boolean;
}) {
  const estLongueDuree = a.typeOffre === "LongueDuree";
  return (
    <div className="group bg-white rounded-2xl overflow-hidden shadow-sm border border-black/5 hover:shadow-lg hover:-translate-y-1 transition-all">
      <div className="relative">
        <PhotoCouverture photos={a.photos} protegees={a.photosProtegees} className="h-40 w-full" />
        <div className="absolute top-2 right-2">
          <BoutonFavori appartementId={a.id} surPhoto />
        </div>
        {afficherIndisponible && (!a.disponible || a.estDesactive) && (
          <span className="absolute bottom-2 left-2 bg-red-600 text-white text-xs font-semibold px-2.5 py-1 rounded-full">
            Plus disponible
          </span>
        )}
      </div>
      <div className="p-5">
        <div className="flex items-center justify-between mb-3">
          <span className="inline-block bg-immo-dark text-white text-xs font-semibold px-2.5 py-1 rounded-full">
            {estLongueDuree
              ? `${a.loyerMensuel?.toLocaleString("fr-FR") ?? "-"} GNF/mois`
              : `${a.prixParNuit?.toLocaleString("fr-FR") ?? "-"} GNF/nuit`}
          </span>
          <ResumeNote note={a.noteMoyenne} nombre={a.nombreAvis} petit />
        </div>
        <p className="font-bold mb-1 truncate">{a.description}</p>
        <p className="text-gray-500 text-sm flex items-center gap-1 mb-3">
          <MapPin size={14} className="shrink-0" />
          <span className="truncate">{localisation(a) ?? a.adresse}</span>
        </p>
        <div className="flex flex-col gap-2.5 mb-4 min-h-0">
          <Capacite appartement={a} compact />
          <Equipements cles={a.equipements} compact max={6} />
          {estLongueDuree && a.dureeAvanceExigee != null && (
            <p className="text-xs text-gray-500">Avance exigée : {a.dureeAvanceExigee} mois</p>
          )}
          {((!estLongueDuree && a.reservationInstantanee) || a.hote?.superhote) && (
            <div className="flex flex-wrap gap-1.5">
              {a.hote?.superhote && <BadgeSuperhote petit />}
              {!estLongueDuree && a.reservationInstantanee && <BadgeInstantanee petit />}
            </div>
          )}
        </div>
        <Link
          to={lien}
          className="block text-center bg-immo-gold text-immo-dark text-sm font-semibold py-2.5 rounded-lg group-hover:bg-immo-gold-light transition-colors"
        >
          Voir l'annonce
        </Link>
      </div>
    </div>
  );
}
