import type { ReactNode } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { ArrowLeft, MapPin } from "lucide-react";
import { useAppartement } from "../hooks/useAppartement";
import ReservationSejour from "../components/sejour/ReservationSejour";
import { localisation } from "../components/sejour/equipements";
import { selectionDepuisUrl } from "../components/sejour/parametres";
import { MENTION_ADRESSE_MASQUEE } from "../components/sejour/reservations";

function Message({ children }: { children: ReactNode }) {
  return <div className="px-6 py-16 max-w-xl mx-auto text-center text-gray-600">{children}</div>;
}

/** Réservation d'un séjour court (même calendrier et même devis que la fiche de l'annonce). */
export default function Reserver() {
  const { type = "sejour-court", id } = useParams<{ type: string; id: string }>();
  const [parametres] = useSearchParams();
  const { appartement, chargement, erreur } = useAppartement(id);

  if (chargement) return <Message>Chargement...</Message>;
  if (erreur || !appartement) return <Message>{erreur ?? "Annonce introuvable."}</Message>;

  if (appartement.typeOffre !== "SejourCourt") {
    return (
      <Message>
        Ce logement est une location longue durée :{" "}
        <Link to={`/annonces/longue-duree/${id}/candidater`} className="text-immo-gold font-semibold">
          déposez une candidature
        </Link>
        .
      </Message>
    );
  }

  const lieu = localisation(appartement);
  return (
    <div className="px-4 sm:px-6 py-10">
      <div className="max-w-6xl mx-auto">
        <Link
          to={`/annonces/${type}/${id}`}
          className="inline-flex items-center gap-2 text-gray-600 text-sm font-medium hover:text-immo-dark mb-6"
        >
          <ArrowLeft size={16} />
          Retour à l'annonce
        </Link>
        <ReservationSejour appartement={appartement} initial={selectionDepuisUrl(parametres)}>
          <div>
            <h1 className="text-2xl md:text-3xl font-extrabold mb-1">Réserver ce séjour</h1>
            <p className="text-gray-500 text-sm flex items-start gap-1.5">
              <MapPin size={14} className="shrink-0 mt-0.5" />
              <span>
                {appartement.adresseMasquee ? appartement.adresse : `${lieu ? `${lieu} · ` : ""}${appartement.adresse}`}
                {appartement.adresseMasquee && (
                  <span className="block text-xs text-gray-400">{MENTION_ADRESSE_MASQUEE}</span>
                )}
              </span>
            </p>
          </div>
        </ReservationSejour>
      </div>
    </div>
  );
}
