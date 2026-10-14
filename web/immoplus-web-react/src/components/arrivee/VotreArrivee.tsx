import { lazy, Suspense } from "react";
import { Link } from "react-router-dom";
import { Clock, KeyRound, MapPin, Navigation, Phone } from "lucide-react";
import type { ReservationDto } from "../../types/models";
import { formaterJour } from "../sejour/dates";
import { lienItineraire, positionDe } from "../carte/position";

const CarteLogement = lazy(() => import("../carte/CarteLogement"));

/**
 * Informations d'arrivée d'une réservation confirmée : adresse exacte, heures, instructions,
 * téléphone de l'hôte et mini-carte. Côté hôte : ce que reçoit le voyageur.
 */
export default function VotreArrivee({
  reservation: r,
  pourHote = false,
}: {
  reservation: ReservationDto;
  pourHote?: boolean;
}) {
  const position = positionDe(r.latitudeExacte, r.longitudeExacte);
  const adresse = r.adresseComplete || r.appartementAdresse;

  return (
    <section className="border border-black/10 rounded-xl p-4 flex flex-col gap-4">
      <div>
        <h3 className="font-bold flex items-center gap-2">
          <KeyRound size={17} className="text-immo-gold" />
          {pourHote ? "Arrivée du voyageur" : "Votre arrivée"}
        </h3>
        {pourHote && (
          <p className="text-xs text-gray-500 mt-0.5">
            Ces informations sont envoyées au voyageur par e-mail la veille de son arrivée.
          </p>
        )}
      </div>

      <div className="grid sm:grid-cols-2 gap-4 text-sm">
        <div className="flex flex-col gap-3">
          <p className="flex items-start gap-2">
            <MapPin size={16} className="text-gray-400 shrink-0 mt-0.5" />
            <span className="min-w-0 wrap-break-word">{adresse}</span>
          </p>
          <p className="flex items-start gap-2">
            <Clock size={16} className="text-gray-400 shrink-0 mt-0.5" />
            <span>
              Arrivée le <strong>{formaterJour(r.dateArrivee)}</strong> à partir de{" "}
              <strong>{r.heureArrivee || "14:00"}</strong>
              <br />
              Départ le <strong>{formaterJour(r.dateDepart)}</strong> avant <strong>{r.heureDepart || "11:00"}</strong>
            </span>
          </p>
          {!pourHote && r.telephoneHote && (
            <p className="flex items-center gap-2">
              <Phone size={16} className="text-gray-400 shrink-0" />
              <span>
                Hôte{r.bailleurNom ? ` (${r.bailleurNom})` : ""} :{" "}
                <a
                  href={`tel:${r.telephoneHote.replace(/\s/g, "")}`}
                  className="font-semibold text-immo-dark underline"
                >
                  {r.telephoneHote}
                </a>
              </span>
            </p>
          )}
        </div>

        <div className="bg-immo-dark/[0.03] rounded-xl p-3">
          <p className="text-xs font-semibold text-gray-500 mb-1">Instructions d'arrivée</p>
          {r.instructionsArrivee ? (
            <p className="whitespace-pre-line wrap-break-word text-gray-800">{r.instructionsArrivee}</p>
          ) : pourHote ? (
            <p className="text-gray-500">
              Aucune instruction renseignée.{" "}
              <Link to="/bailleur/logements" className="font-semibold text-immo-gold">
                Ajoutez-les dans l'annonce
              </Link>{" "}
              (accès, clés, wifi…).
            </p>
          ) : (
            <p className="text-gray-500">
              L'hôte n'a pas encore indiqué d'instructions : contactez-le par la messagerie.
            </p>
          )}
        </div>
      </div>

      {position && (
        <div className="flex flex-col gap-2">
          <Suspense fallback={<div className="h-48 rounded-2xl bg-gray-200 animate-pulse" />}>
            <CarteLogement position={position} className="h-48" />
          </Suspense>
          <a
            href={lienItineraire(position)}
            target="_blank"
            rel="noopener noreferrer"
            className="self-start inline-flex items-center gap-1.5 text-sm font-semibold text-immo-dark hover:text-immo-gold"
          >
            <Navigation size={15} />
            Itinéraire
          </a>
        </div>
      )}
    </section>
  );
}
