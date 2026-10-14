import { useState } from "react";
import { CalendarX2, DoorOpen, Undo2 } from "lucide-react";
import { apiClient } from "../../api/client";
import {
  classeBoutonDanger,
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterDate,
  messageErreur,
  useChargement,
} from "../admin/outils";
import type { LocationDto } from "../../types/models";

const PREAVIS_MOIS = 3;

/**
 * Fin de location avec un préavis de 3 mois.
 * - Locataire : donne (ou annule) son propre préavis.
 * - Admin : donne le préavis pour le locataire ou le bailleur, l'annule, puis termine la location à la date de fin.
 */
export default function FinLocation({
  locationId,
  admin = false,
  onChange,
}: {
  locationId: number;
  admin?: boolean;
  onChange?: () => void;
}) {
  const { donnees: location, chargement, erreur, setDonnees } = useChargement<LocationDto>(`location/${locationId}`);
  const [formulaire, setFormulaire] = useState(false);
  const [donnePar, setDonnePar] = useState<"Locataire" | "Bailleur">("Locataire");
  const [motif, setMotif] = useState("");
  const [enCours, setEnCours] = useState(false);
  const [erreurAction, setErreurAction] = useState<string | null>(null);
  const [aujourdhui] = useState(() => new Date(new Date().toDateString()));

  if (chargement) return <p className="text-sm text-gray-500">Chargement...</p>;
  if (erreur || !location) return <p className="text-sm text-red-600">{erreur}</p>;

  const appeler = async (chemin: string, corps?: unknown, confirmation?: string) => {
    if (confirmation && !window.confirm(confirmation)) return;
    setErreurAction(null);
    setEnCours(true);
    try {
      setDonnees(await apiClient.post<LocationDto>(chemin, corps));
      setFormulaire(false);
      onChange?.();
    } catch (err) {
      setErreurAction(messageErreur(err, "L'action a échoué."));
    } finally {
      setEnCours(false);
    }
  };

  const finSiPreavisAujourdhui = new Date(aujourdhui);
  finSiPreavisAujourdhui.setMonth(finSiPreavisAujourdhui.getMonth() + PREAVIS_MOIS);
  const finAtteinte = !!location.dateFinPrevue && new Date(location.dateFinPrevue) <= aujourdhui;
  const peutAnnuler = !!location.datePreavis && !finAtteinte && (admin || location.preavisDonnePar === "Locataire");

  let contenu;
  if (location.statut === "Terminee") {
    contenu = <p className="text-green-700">Location terminée le {formaterDate(location.dateFin)}.</p>;
  } else if (location.statut !== "EnCours") {
    contenu = <p className="text-gray-500">Location résiliée.</p>;
  } else if (!location.dateRemiseCles) {
    contenu = <p className="text-gray-400">Un préavis pourra être donné une fois les clés remises.</p>;
  } else if (location.datePreavis) {
    contenu = (
      <div className="space-y-2">
        <p className="bg-amber-50 border border-amber-200 text-amber-900 rounded-lg px-3 py-2">
          Préavis donné le {formaterDate(location.datePreavis)} par{" "}
          {location.preavisDonnePar === "Bailleur" ? "le bailleur" : "le locataire"} :{" "}
          <strong>fin de la location le {formaterDate(location.dateFinPrevue)}</strong>.
          {location.motifPreavis && <span className="block text-amber-800 italic">« {location.motifPreavis} »</span>}
        </p>
        <div className="flex flex-wrap gap-2">
          {admin && finAtteinte && (
            <button
              onClick={() =>
                appeler(
                  `location/${location.id}/terminer`,
                  undefined,
                  "Terminer la location ? Le logement redeviendra disponible dans les annonces.",
                )
              }
              disabled={enCours}
              className={classeBoutonPrincipal(true)}
            >
              <DoorOpen size={14} />
              Terminer la location
            </button>
          )}
          {admin && !finAtteinte && (
            <p className="text-gray-500 text-xs self-center">
              La location pourra être terminée à partir du {formaterDate(location.dateFinPrevue)}.
            </p>
          )}
          {peutAnnuler && (
            <button
              onClick={() =>
                appeler(
                  `location/${location.id}/annuler-preavis`,
                  undefined,
                  "Annuler le préavis ? La location continuera sans date de fin.",
                )
              }
              disabled={enCours}
              className={classeBoutonSecondaire(true)}
            >
              <Undo2 size={14} />
              Annuler le préavis
            </button>
          )}
        </div>
      </div>
    );
  } else if (formulaire) {
    contenu = (
      <div className="space-y-3">
        <p className="text-gray-600">
          Avec un préavis donné aujourd'hui, la location se terminera le{" "}
          <strong>{formaterDate(finSiPreavisAujourdhui.toISOString())}</strong> ({PREAVIS_MOIS} mois). Les loyers
          restent dus jusqu'à cette date.
        </p>
        {admin && (
          <label className="block text-xs font-medium text-gray-700 max-w-xs">
            Préavis donné par
            <select
              value={donnePar}
              onChange={(e) => setDonnePar(e.target.value as "Locataire" | "Bailleur")}
              className={classeChamp}
            >
              <option value="Locataire">Le locataire</option>
              <option value="Bailleur">Le bailleur</option>
            </select>
          </label>
        )}
        <label className="block text-xs font-medium text-gray-700">
          Motif (optionnel)
          <input value={motif} onChange={(e) => setMotif(e.target.value)} maxLength={300} className={classeChamp} />
        </label>
        <div className="flex gap-2">
          <button
            onClick={() =>
              appeler(
                `location/${location.id}/preavis`,
                { donnePar: admin ? donnePar : undefined, motif: motif.trim() || null },
                `Confirmer le préavis ? La location se terminera le ${formaterDate(finSiPreavisAujourdhui.toISOString())}.`,
              )
            }
            disabled={enCours}
            className={classeBoutonDanger(true)}
          >
            <CalendarX2 size={14} />
            Confirmer le préavis
          </button>
          <button onClick={() => setFormulaire(false)} className={classeBoutonSecondaire(true)}>
            Annuler
          </button>
        </div>
      </div>
    );
  } else {
    contenu = (
      <div className="flex flex-wrap items-center gap-3">
        <p className="text-gray-600">
          Location en cours, sans date de fin. Préavis de {PREAVIS_MOIS} mois pour y mettre fin.
        </p>
        <button onClick={() => setFormulaire(true)} className={classeBoutonSecondaire(true)}>
          <CalendarX2 size={14} />
          {admin ? "Enregistrer un préavis" : "Donner mon préavis"}
        </button>
      </div>
    );
  }

  return (
    <div className="text-sm space-y-2">
      {contenu}
      {erreurAction && <p className="text-red-600">{erreurAction}</p>}
    </div>
  );
}
