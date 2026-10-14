import { useState, type FormEvent } from "react";
import { Banknote, CalendarCheck, CalendarDays, Check, KeyRound, Wallet } from "lucide-react";
import { apiClient } from "../../api/client";
import {
  classeBoutonPrincipal,
  classeChamp,
  formaterDate,
  formaterMontant,
  messageErreur,
  useChargement,
  formaterReference,
} from "../admin/outils";
import Echeancier from "../admin/Echeancier";
import { formaterJour, jourLocal } from "../sejour/dates";
import Loyers from "../financement/Loyers";
import FinLocation from "../financement/FinLocation";
import type { CandidatureDto, FinancementDto } from "../../types/models";
import BoutonPdf from "../documents/BoutonPdf";
import { contratLocation } from "../documents/pdf";

const modesPaiement = ["Espèces", "Orange Money", "MTN Mobile Money", "Virement", "Chèque"];
const aujourdhui = jourLocal;

function Pastille({ fait, numero }: { fait: boolean; numero: number }) {
  return (
    <span
      className={`flex items-center justify-center w-7 h-7 rounded-full text-xs font-bold shrink-0 ${
        fait ? "bg-green-600 text-white" : "bg-immo-gold text-immo-dark"
      }`}
    >
      {fait ? <Check size={14} /> : numero}
    </span>
  );
}

/**
 * Après l'attribution (espace admin) : visite, apport, remise des clés puis remboursement.
 * Chaque étape envoie un email au locataire.
 */
export default function SuiviAttribution({
  candidature,
  onMiseAJour,
}: {
  candidature: CandidatureDto;
  onMiseAJour: (c: CandidatureDto) => void;
}) {
  // Jour courant figé au premier rendu (pas de lecture de l'horloge pendant le rendu).
  const [jour] = useState(aujourdhui);
  const [mode, setMode] = useState(modesPaiement[0]);
  const [reference, setReference] = useState("");
  const [datePaiement, setDatePaiement] = useState(aujourdhui);
  const [dateRemise, setDateRemise] = useState(aujourdhui);
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);

  const appeler = async (chemin: string, corps: unknown) => {
    setErreur(null);
    setEnCours(true);
    try {
      onMiseAJour(await apiClient.post<CandidatureDto>(chemin, corps));
    } catch (err) {
      setErreur(messageErreur(err, "L'action a échoué."));
    } finally {
      setEnCours(false);
    }
  };

  const enregistrerApport = (e: FormEvent) => {
    e.preventDefault();
    appeler(`candidature/${candidature.id}/apport`, {
      modePaiement: mode,
      reference: reference.trim() || null,
      datePaiement,
    });
  };

  const apportPaye = !!candidature.dateApportPaye;
  const clesRemises = !!candidature.dateRemiseCles;
  // Étape 4 (si l'avance est financée) : échéancier puis paiements des mensualités, validés par le bailleur.
  const financements = useChargement<FinancementDto[]>(candidature.financementLocationId ? "financement/lister" : null);
  const financement = financements.donnees?.find((f) => f.id === candidature.financementLocationId);
  const visite = candidature.visites?.find((v) => v.statut === "Effectuee");

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-gray-600">
          Logement attribué le {formaterDate(candidature.dateTraitement)}
          {candidature.locationId && <> — location n° {formaterReference(candidature.locationId)}</>}.
        </p>
        {candidature.locationId && (
          <BoutonPdf fichier={contratLocation(candidature.locationId)} libelle="Contrat de location (PDF)" />
        )}
      </div>

      <div className="flex gap-3">
        <Pastille fait numero={1} />
        <div className="flex-1">
          <p className="font-semibold flex items-center gap-1.5">
            <CalendarCheck size={16} className="text-immo-gold" />
            Visite du logement
          </p>
          <p className="text-green-700">
            {visite
              ? `Effectuée le ${formaterJour(visite.dateHeure)}.`
              : "Aucune visite enregistrée (dossier attribué avant la mise en place des visites)."}
          </p>
        </div>
      </div>

      <div className="flex gap-3">
        <Pastille fait={apportPaye} numero={2} />
        <div className="flex-1">
          <p className="font-semibold flex items-center gap-1.5">
            <Banknote size={16} className="text-immo-gold" />
            Paiement de l'apport : {formaterMontant(candidature.apportInitial)}
          </p>
          {apportPaye ? (
            <p className="text-green-700">Reçu le {formaterDate(candidature.dateApportPaye)}.</p>
          ) : (
            <form onSubmit={enregistrerApport} className="mt-2 grid grid-cols-1 sm:grid-cols-4 gap-2 items-end">
              <label className="text-xs font-medium text-gray-700">
                Mode
                <select value={mode} onChange={(e) => setMode(e.target.value)} className={classeChamp}>
                  {modesPaiement.map((m) => (
                    <option key={m}>{m}</option>
                  ))}
                </select>
              </label>
              <label className="text-xs font-medium text-gray-700">
                Référence (optionnel)
                <input value={reference} onChange={(e) => setReference(e.target.value)} className={classeChamp} />
              </label>
              <label className="text-xs font-medium text-gray-700">
                Date
                <input
                  type="date"
                  value={datePaiement}
                  max={jour}
                  onChange={(e) => setDatePaiement(e.target.value)}
                  className={classeChamp}
                />
              </label>
              <button type="submit" disabled={enCours} className={classeBoutonPrincipal(true)}>
                <Check size={14} />
                Apport reçu
              </button>
            </form>
          )}
        </div>
      </div>

      <div className="flex gap-3">
        <Pastille fait={clesRemises} numero={3} />
        <div className="flex-1">
          <p className={`font-semibold flex items-center gap-1.5 ${apportPaye ? "" : "text-gray-400"}`}>
            <KeyRound size={16} className={apportPaye ? "text-immo-gold" : "text-gray-300"} />
            Remise des clés
          </p>
          {clesRemises ? (
            <p className="text-green-700">
              Clés remises le {formaterDate(candidature.dateRemiseCles)} : la location a commencé.
            </p>
          ) : apportPaye ? (
            <div className="mt-2 flex flex-wrap items-end gap-2">
              <label className="text-xs font-medium text-gray-700">
                Date de remise
                <input
                  type="date"
                  value={dateRemise}
                  min={candidature.dateApportPaye?.slice(0, 10)}
                  max={jour}
                  onChange={(e) => setDateRemise(e.target.value)}
                  className={classeChamp}
                />
              </label>
              <button
                onClick={() => appeler(`candidature/${candidature.id}/remise-cles`, { dateRemise })}
                disabled={enCours}
                className={classeBoutonPrincipal(true)}
              >
                <KeyRound size={14} />
                Clés remises
              </button>
            </div>
          ) : (
            <p className="text-gray-400">Possible une fois l'apport reçu.</p>
          )}
        </div>
      </div>

      {candidature.financementLocationId && (
        <div className="flex gap-3">
          <Pastille fait={financement?.statut === "Solde"} numero={4} />
          <div className="flex-1 min-w-0">
            <p className={`font-semibold flex items-center gap-1.5 ${apportPaye ? "" : "text-gray-400"}`}>
              <Wallet size={16} className={apportPaye ? "text-immo-gold" : "text-gray-300"} />
              Remboursement de l'avance financée
              {financement && (
                <span className="font-normal text-gray-500">
                  — {financement.nombreMensualites} mensualité{financement.nombreMensualites > 1 ? "s" : ""} de{" "}
                  {formaterMontant(financement.montantMensualite)}
                </span>
              )}
            </p>
            {!apportPaye ? (
              <p className="text-gray-400">L'échéancier se génère après le paiement de l'apport.</p>
            ) : financement ? (
              <div className="mt-2 space-y-2">
                {financement.statut === "Solde" && (
                  <p className="text-green-700">Financement entièrement remboursé. Historique des mensualités :</p>
                )}
                <Echeancier financement={financement} onModifie={financements.recharger} />
              </div>
            ) : (
              <p className="text-gray-500">Chargement...</p>
            )}
          </div>
        </div>
      )}

      {candidature.locationId && (
        <div className="flex gap-3">
          <Pastille fait={false} numero={candidature.financementLocationId ? 5 : 4} />
          <div className="flex-1 min-w-0">
            <p className={`font-semibold flex items-center gap-1.5 ${clesRemises ? "" : "text-gray-400"}`}>
              <CalendarDays size={16} className={clesRemises ? "text-immo-gold" : "text-gray-300"} />
              Loyers mensuels (sans frais)
            </p>
            <div className="mt-2">
              <Loyers locationId={candidature.locationId} gestion />
            </div>
          </div>
        </div>
      )}

      {candidature.locationId && clesRemises && (
        <div className="flex gap-3">
          <Pastille fait={false} numero={candidature.financementLocationId ? 6 : 5} />
          <div className="flex-1 min-w-0">
            <p className="font-semibold">Fin de location (préavis de 3 mois)</p>
            <div className="mt-2">
              <FinLocation locationId={candidature.locationId} admin />
            </div>
          </div>
        </div>
      )}

      {erreur && <p className="text-red-600">{erreur}</p>}
    </div>
  );
}
