import { useState } from "react";
import { Check, X } from "lucide-react";
import { apiClient } from "../../api/client";
import type { ReservationDto } from "../../types/models";
import { MessageErreur } from "../admin/ui";
import {
  classeBoutonDanger,
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  messageErreur,
} from "../admin/outils";

/** Accepter ou refuser (avec un motif facultatif) une demande de réservation — hôte ou admin. */
export default function ReponseDemande({
  reservation,
  onTraitee,
}: {
  reservation: ReservationDto;
  onTraitee: () => void;
}) {
  const [refusOuvert, setRefusOuvert] = useState(false);
  const [motif, setMotif] = useState("");
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);

  const repondre = async (action: "accepter" | "refuser") => {
    setErreur(null);
    setEnCours(true);
    try {
      await apiClient.post(
        `reservation/${reservation.id}/${action}`,
        action === "refuser" ? { motif: motif.trim() || null } : undefined,
      );
      onTraitee();
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'enregistrer votre réponse."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <div className="flex flex-col gap-2">
      {refusOuvert ? (
        <div className="flex flex-col gap-2 bg-red-50/60 rounded-xl p-3">
          <label className="text-sm font-medium text-gray-700">
            Motif du refus <span className="text-gray-400 font-normal">(facultatif, transmis au voyageur)</span>
            <textarea
              value={motif}
              onChange={(e) => setMotif(e.target.value)}
              rows={2}
              maxLength={500}
              placeholder="Ex. logement en travaux à ces dates"
              className={classeChamp}
            />
          </label>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => repondre("refuser")}
              disabled={enCours}
              className={classeBoutonDanger(true)}
            >
              <X size={14} />
              {enCours ? "Envoi..." : "Confirmer le refus"}
            </button>
            <button type="button" onClick={() => setRefusOuvert(false)} className={classeBoutonSecondaire(true)}>
              Retour
            </button>
          </div>
        </div>
      ) : (
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => repondre("accepter")}
            disabled={enCours}
            className={classeBoutonPrincipal(true)}
          >
            <Check size={14} />
            {enCours ? "Envoi..." : "Accepter"}
          </button>
          <button
            type="button"
            onClick={() => setRefusOuvert(true)}
            disabled={enCours}
            className={classeBoutonDanger(true)}
          >
            <X size={14} />
            Refuser
          </button>
        </div>
      )}
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
    </div>
  );
}
