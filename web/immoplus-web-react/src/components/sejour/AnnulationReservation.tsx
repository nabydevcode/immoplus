import { useState } from "react";
import { AlertTriangle, Ban } from "lucide-react";
import { apiClient } from "../../api/client";
import type { ApercuAnnulationDto, ReservationDto } from "../../types/models";
import { MessageErreur } from "../admin/ui";
import {
  classeBoutonDanger,
  classeBoutonSecondaire,
  classeChamp,
  formaterMontant,
  formaterReference,
  messageErreur,
  useChargement,
} from "../admin/outils";
import { POLITIQUES } from "./politiques";

/**
 * Confirmation d'annulation avec l'aperçu du remboursement calculé par l'API pour l'appelant :
 * selon la politique pour le voyageur, intégral si l'hôte (ou l'admin) annule.
 */
export default function AnnulationReservation({
  reservation,
  par,
  onAnnulee,
  onFermer,
}: {
  reservation: ReservationDto;
  par: "Voyageur" | "Hote" | "Admin";
  onAnnulee: () => void;
  onFermer: () => void;
}) {
  const {
    donnees: apercu,
    chargement,
    erreur,
  } = useChargement<ApercuAnnulationDto>(`reservation/${reservation.id}/annulation/apercu`);
  const [motif, setMotif] = useState("");
  const [enCours, setEnCours] = useState(false);
  const [erreurAnnulation, setErreurAnnulation] = useState<string | null>(null);
  const voyageur = par === "Voyageur";

  const confirmer = async () => {
    setErreurAnnulation(null);
    setEnCours(true);
    try {
      await apiClient.post(
        `reservation/${reservation.id}/annuler`,
        voyageur ? undefined : { motif: motif.trim() || null },
      );
      onAnnulee();
    } catch (err) {
      setErreurAnnulation(messageErreur(err, "Impossible d'annuler la réservation."));
      setEnCours(false);
    }
  };

  return (
    <div className="flex flex-col gap-3 bg-red-50/60 border border-red-100 rounded-xl p-4">
      <p className="font-bold text-sm">
        {voyageur ? "Annuler ma réservation" : `Annuler la réservation n° ${formaterReference(reservation.id)}`}
      </p>

      {!voyageur && (
        <p className="text-sm text-red-800 flex items-start gap-1.5">
          <AlertTriangle size={16} className="shrink-0 mt-0.5" />
          <span>
            {par === "Hote"
              ? "Le voyageur sera remboursé intégralement et prévenu par e-mail. Une annulation de votre part empêche d'obtenir le badge Superhôte pendant 12 mois."
              : "Annulation pour le compte de l'hôte : le voyageur est remboursé intégralement."}
          </span>
        </p>
      )}

      {chargement ? (
        <p className="text-sm text-gray-500">Calcul du remboursement...</p>
      ) : erreur || !apercu ? (
        <MessageErreur>{erreur ?? "Impossible de calculer le remboursement."}</MessageErreur>
      ) : (
        <div className="bg-white rounded-lg border border-black/5 p-3 text-sm flex flex-col gap-2">
          {voyageur && (
            <p className="text-gray-500">
              Politique d'annulation{" "}
              <strong className="text-gray-700">{POLITIQUES[apercu.politique]?.libelle ?? apercu.politique}</strong>
            </p>
          )}
          <dl className="grid grid-cols-2 gap-2">
            <div>
              <dt className="text-gray-500 text-xs">Montant payé</dt>
              <dd className="font-semibold">{formaterMontant(apercu.montantPaye)}</dd>
            </div>
            <div>
              <dt className="text-gray-500 text-xs">
                {voyageur ? "Vous serez remboursé" : "Remboursement au voyageur"}
              </dt>
              <dd className={`font-extrabold ${apercu.montantRembourse > 0 ? "text-green-700" : ""}`}>
                {formaterMontant(apercu.montantRembourse)}
              </dd>
            </div>
          </dl>
          {apercu.explication && <p className="text-xs text-gray-600">{apercu.explication}</p>}
        </div>
      )}

      {!voyageur && (
        <label className="text-sm font-medium text-gray-700">
          Motif <span className="text-gray-400 font-normal">(facultatif, transmis au voyageur)</span>
          <textarea
            value={motif}
            onChange={(e) => setMotif(e.target.value)}
            rows={2}
            maxLength={500}
            className={classeChamp}
          />
        </label>
      )}

      {erreurAnnulation && <MessageErreur>{erreurAnnulation}</MessageErreur>}

      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          onClick={confirmer}
          disabled={enCours || chargement}
          className={`${classeBoutonDanger(true)} bg-white`}
        >
          <Ban size={14} />
          {enCours ? "Annulation..." : "Confirmer l'annulation"}
        </button>
        <button type="button" onClick={onFermer} className={`${classeBoutonSecondaire(true)} bg-white`}>
          {voyageur ? "Garder ma réservation" : "Ne pas annuler"}
        </button>
      </div>
    </div>
  );
}
