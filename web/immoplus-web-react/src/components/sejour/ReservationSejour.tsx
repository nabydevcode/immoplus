import { useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { CalendarCheck, Minus, Plus, Send, Zap } from "lucide-react";
import { apiClient } from "../../api/client";
import { useAuth } from "../../context/auth";
import type { AppartementDto, ReservationDto } from "../../types/models";
import { MessageErreur } from "../admin/ui";
import { classeBoutonPrincipal, formaterMontant, formaterReference, messageErreur } from "../admin/outils";
import Calendrier from "./Calendrier";
import DetailPrix from "./DetailPrix";
import { jourCourt } from "./dates";
import { POLITIQUES, politiqueDe } from "./politiques";
import { useSelectionSejour, type SelectionInitiale, type SelectionSejour } from "./useSelectionSejour";
import { useLienConnexion } from "../../hooks/useLienConnexion";

function Legende() {
  return (
    <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-gray-500 mt-4">
      <span className="inline-flex items-center gap-1.5">
        <span className="w-3 h-3 rounded bg-immo-dark" /> Vos dates
      </span>
      <span className="inline-flex items-center gap-1.5">
        <span className="w-3 h-3 rounded bg-gray-100 border border-gray-200" /> Nuit indisponible
      </span>
    </div>
  );
}

/** Section « Disponibilités » de la fiche : calendrier de deux mois pour choisir l'arrivée puis le départ. */
export function CalendrierSejour({ selection }: { selection: SelectionSejour }) {
  const { arrivee, depart, nombreNuits, nuitsMin } = selection;
  const consigne = !arrivee
    ? "Choisissez votre date d'arrivée."
    : !depart
      ? `Choisissez votre date de départ${nuitsMin > 1 ? ` (${nuitsMin} nuits minimum)` : ""}.`
      : `${nombreNuits} nuit${nombreNuits > 1 ? "s" : ""} · du ${jourCourt(arrivee)} au ${jourCourt(depart)}`;
  return (
    <section className="bg-white rounded-2xl border border-black/5 shadow-sm p-5 sm:p-6">
      <div className="flex flex-wrap items-baseline justify-between gap-2 mb-5">
        <div>
          <h2 className="font-extrabold text-lg">Disponibilités</h2>
          <p className="text-sm text-gray-500">{consigne}</p>
        </div>
        {arrivee && (
          <button type="button" onClick={selection.effacer} className="text-sm font-semibold underline text-gray-700">
            Effacer les dates
          </button>
        )}
      </div>
      {selection.calendrier.erreur && (
        <div className="mb-3">
          <MessageErreur>{selection.calendrier.erreur}</MessageErreur>
        </div>
      )}
      <Calendrier
        mois={selection.mois}
        onChangerMois={selection.setMois}
        moisMin={selection.moisMin}
        moisMax={selection.moisMax}
        etatJour={selection.etatJour}
        debut={arrivee}
        fin={depart}
        onChoisir={selection.choisir}
      />
      <Legende />
    </section>
  );
}

function Confirmation({ reservation }: { reservation: ReservationDto }) {
  const demande = reservation.statut === "DemandeEnvoyee";
  return (
    <div className="flex flex-col items-center text-center gap-3">
      <span className="flex items-center justify-center w-12 h-12 rounded-full bg-green-50 text-green-600">
        {demande ? <Send size={22} /> : <CalendarCheck size={24} />}
      </span>
      <h3 className="font-extrabold text-lg">{demande ? "Demande envoyée" : "Réservation enregistrée"}</h3>
      <p className="text-sm text-gray-500">
        Du <strong className="text-gray-700">{jourCourt(reservation.dateArrivee)}</strong> au{" "}
        <strong className="text-gray-700">{jourCourt(reservation.dateDepart)}</strong> · n°{" "}
        {formaterReference(reservation.id)}
      </p>
      <p className="text-2xl font-extrabold">{formaterMontant(reservation.montantTotal)}</p>
      <p className="text-sm text-amber-800 bg-amber-50 rounded-lg px-4 py-2">
        {demande
          ? "L'hôte a 48 h pour accepter votre demande. Vous serez prévenu par e-mail ; vous paierez ensuite pour confirmer."
          : "En attente de paiement : votre séjour sera confirmé dès réception du règlement."}
      </p>
      <Link to="/mes-reservations" className={`${classeBoutonPrincipal()} w-full mt-1`}>
        Voir mes réservations
      </Link>
    </div>
  );
}

/** Encadré de réservation : dates, voyageurs, prix détaillé en direct et bouton « Réserver » / « Demander à réserver ». */
export function EncadreReservation({
  appartement,
  selection,
}: {
  appartement: AppartementDto;
  selection: SelectionSejour;
}) {
  const { estConnecte, roleUtilisateur } = useAuth();
  const lienConnexion = useLienConnexion();
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);
  const [reservation, setReservation] = useState<ReservationDto | null>(null);
  const { arrivee, depart, voyageurs, devis } = selection;
  const instantanee = devis?.reservationInstantanee ?? appartement.reservationInstantanee ?? false;
  const politique = politiqueDe(appartement.politiqueAnnulation);

  const reserver = async () => {
    if (!arrivee || !depart) return;
    setErreur(null);
    setEnCours(true);
    try {
      setReservation(
        await apiClient.post<ReservationDto>("reservation/creer", {
          appartementId: appartement.id,
          dateArrivee: arrivee,
          dateDepart: depart,
          nombreVoyageurs: voyageurs,
        }),
      );
      selection.calendrier.recharger();
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'enregistrer la réservation. Réessayez plus tard."));
    } finally {
      setEnCours(false);
    }
  };

  if (reservation) {
    return (
      <div className="bg-white rounded-2xl p-6 shadow-sm border border-black/5">
        <Confirmation reservation={reservation} />
      </div>
    );
  }

  const reductions = [
    appartement.reductionSemainePct ? `−${appartement.reductionSemainePct} % dès 7 nuits` : null,
    appartement.reductionMoisPct ? `−${appartement.reductionMoisPct} % dès 28 nuits` : null,
  ].filter(Boolean);

  return (
    <div className="bg-white rounded-2xl p-5 sm:p-6 shadow-sm border border-black/5 flex flex-col gap-4">
      <div className="flex items-baseline justify-between gap-2 flex-wrap">
        <p>
          <span className="text-2xl font-extrabold">{formaterMontant(appartement.prixParNuit)}</span>
          <span className="text-gray-500 text-sm"> / nuit</span>
        </p>
        {instantanee && (
          <span className="inline-flex items-center gap-1 text-xs font-semibold text-immo-dark">
            <Zap size={13} className="text-immo-gold" fill="currentColor" />
            Instantanée
          </span>
        )}
      </div>

      <div className="grid grid-cols-2 border border-gray-300 rounded-xl overflow-hidden text-sm">
        <div className="px-3 py-2 border-r border-gray-300">
          <p className="text-[11px] font-bold uppercase text-gray-500">Arrivée</p>
          <p className={arrivee ? "font-semibold" : "text-gray-400"}>{arrivee ? jourCourt(arrivee) : "Choisir"}</p>
        </div>
        <div className="px-3 py-2">
          <p className="text-[11px] font-bold uppercase text-gray-500">Départ</p>
          <p className={depart ? "font-semibold" : "text-gray-400"}>{depart ? jourCourt(depart) : "Choisir"}</p>
        </div>
        <div className="col-span-2 border-t border-gray-300 px-3 py-2 flex items-center justify-between">
          <div>
            <p className="text-[11px] font-bold uppercase text-gray-500">Voyageurs</p>
            <p className="font-semibold">
              {voyageurs} voyageur{voyageurs > 1 ? "s" : ""}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => selection.setVoyageurs(voyageurs - 1)}
              disabled={voyageurs <= 1}
              aria-label="Un voyageur de moins"
              className="w-8 h-8 rounded-full border border-gray-300 flex items-center justify-center disabled:opacity-30"
            >
              <Minus size={14} />
            </button>
            <button
              type="button"
              onClick={() => selection.setVoyageurs(voyageurs + 1)}
              disabled={voyageurs >= selection.capaciteMax}
              aria-label="Un voyageur de plus"
              className="w-8 h-8 rounded-full border border-gray-300 flex items-center justify-center disabled:opacity-30"
            >
              <Plus size={14} />
            </button>
          </div>
        </div>
      </div>
      {appartement.capacite != null && (
        <p className="text-xs text-gray-500 -mt-2">
          {appartement.capacite} voyageur{appartement.capacite > 1 ? "s" : ""} maximum
          {selection.nuitsMin > 1 ? ` · ${selection.nuitsMin} nuits minimum` : ""}
        </p>
      )}

      {!arrivee || !depart ? (
        <p className="text-sm text-gray-500">Sélectionnez vos dates dans le calendrier pour voir le prix détaillé.</p>
      ) : selection.erreurDevis ? (
        <MessageErreur>{selection.erreurDevis}</MessageErreur>
      ) : !devis ? (
        <p className="text-sm text-gray-500">Calcul du prix...</p>
      ) : !devis.disponible ? (
        <p className="text-sm text-amber-800 bg-amber-50 rounded-lg px-3 py-2">
          {devis.raison ?? "Ces dates ne sont pas disponibles."}
        </p>
      ) : (
        <DetailPrix {...devis} />
      )}

      {reductions.length > 0 && <p className="text-xs text-green-700 font-semibold">{reductions.join(" · ")}</p>}

      {erreur && <MessageErreur>{erreur}</MessageErreur>}

      {!appartement.disponible ? (
        <p className="text-sm text-gray-500">Ce logement n'est plus disponible.</p>
      ) : !estConnecte ? (
        <Link to={lienConnexion} className={`${classeBoutonPrincipal()} w-full py-3`}>
          Se connecter pour réserver
        </Link>
      ) : roleUtilisateur !== "Locataire" ? (
        <p className="text-sm text-gray-500">Seul un compte Locataire peut réserver.</p>
      ) : (
        <button
          type="button"
          onClick={reserver}
          disabled={enCours || !devis?.disponible}
          className={`${classeBoutonPrincipal()} w-full py-3 text-base disabled:cursor-not-allowed`}
        >
          {enCours ? "Envoi..." : instantanee ? "Réserver" : "Demander à réserver"}
        </button>
      )}
      {estConnecte && roleUtilisateur === "Locataire" && appartement.disponible && (
        <p className="text-xs text-gray-500 text-center">
          {instantanee
            ? "Réservation immédiate : vous paierez ensuite pour la confirmer."
            : "L'hôte a 48 h pour accepter ; aucun paiement avant son accord."}
        </p>
      )}
      <p className="text-xs text-gray-500 text-center">
        Annulation {POLITIQUES[politique].libelle.toLowerCase()} :{" "}
        {POLITIQUES[politique].resume.charAt(0).toLowerCase()}
        {POLITIQUES[politique].resume.slice(1)}
      </p>
    </div>
  );
}

/**
 * Mise en page d'une fiche séjour court : `children` (présentation du logement), le calendrier puis `suite` à gauche,
 * l'encadré de réservation à droite (en dessous sur mobile).
 */
export default function ReservationSejour({
  appartement,
  initial,
  children,
  suite,
}: {
  appartement: AppartementDto;
  initial: SelectionInitiale;
  children?: ReactNode;
  /** Sections affichées sous le calendrier (avis, hôte, conditions d'annulation). */
  suite?: ReactNode;
}) {
  const selection = useSelectionSejour(appartement, initial);
  return (
    <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
      <div className="md:col-span-2 flex flex-col gap-6 min-w-0">
        {children}
        <CalendrierSejour selection={selection} />
        {suite}
      </div>
      <div className="md:sticky md:top-20 h-fit">
        <EncadreReservation appartement={appartement} selection={selection} />
      </div>
    </div>
  );
}
