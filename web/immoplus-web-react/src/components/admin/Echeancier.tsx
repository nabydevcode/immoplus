import { Fragment, useState } from "react";
import { Banknote, CalendarPlus, CheckCheck, ChevronDown, ChevronRight } from "lucide-react";
import { apiClient } from "../../api/client";
import FormulairePaiement from "./FormulairePaiement";
import { MessageErreur } from "./ui";
import { BadgePonctualite, BilanRetards, HistoriquePaiements } from "../financement/SuiviPaiements";
import {
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterDate,
  formaterMontant,
  messageErreur,
  useChargement,
} from "./outils";
import type { FinancementDto, MensualiteDto } from "../../types/models";
import { jourLocal } from "../sejour/dates";

const modesPaiement = ["Espèces", "Orange Money", "MTN Mobile Money", "Virement", "Chèque"];

/**
 * Remboursement anticipé : un versement qui solde toutes les mensualités restantes (une trace par mensualité).
 * Le loyer normal démarre toujours à la fin de la période couverte par l'avance.
 */
function SoldeAnticipe({
  financementId,
  reste,
  onSolde,
}: {
  financementId: number;
  reste: number;
  onSolde: () => void;
}) {
  const [ouvert, setOuvert] = useState(false);
  const [mode, setMode] = useState(modesPaiement[0]);
  const [reference, setReference] = useState("");
  const [aujourdhui] = useState(jourLocal);
  const [date, setDate] = useState(aujourdhui);
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);

  const solder = async () => {
    if (
      !window.confirm(`Enregistrer le remboursement anticipé de ${formaterMontant(reste)} et clôturer le financement ?`)
    )
      return;
    setErreur(null);
    setEnCours(true);
    try {
      await apiClient.post(`financement/${financementId}/solder`, {
        modePaiement: mode,
        reference: reference.trim() || null,
        datePaiement: date,
      });
      setOuvert(false);
      onSolde();
    } catch (err) {
      setErreur(messageErreur(err, "Le remboursement anticipé a échoué."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <div className="bg-immo-cream/60 border border-black/5 rounded-xl p-3 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span>
          Reste à rembourser : <strong>{formaterMontant(reste)}</strong>
        </span>
        {!ouvert && (
          <button onClick={() => setOuvert(true)} className={classeBoutonSecondaire(true)}>
            <CheckCheck size={14} />
            Remboursement anticipé
          </button>
        )}
      </div>
      {ouvert && (
        <div className="mt-3 grid grid-cols-1 sm:grid-cols-4 gap-2 items-end">
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
              value={date}
              max={aujourdhui}
              onChange={(e) => setDate(e.target.value)}
              className={classeChamp}
            />
          </label>
          <div className="flex gap-2">
            <button onClick={solder} disabled={enCours} className={classeBoutonPrincipal(true)}>
              {enCours ? "Enregistrement..." : "Solder"}
            </button>
            <button onClick={() => setOuvert(false)} className={classeBoutonSecondaire(true)}>
              Annuler
            </button>
          </div>
        </div>
      )}
      {erreur && <p className="text-red-600 mt-2">{erreur}</p>}
    </div>
  );
}

/** Échéancier d'un financement : génération (après l'apport), mensualités et enregistrement des paiements. */
export default function Echeancier({ financement, onModifie }: { financement: FinancementDto; onModifie: () => void }) {
  const { donnees, chargement, erreur, recharger } = useChargement<MensualiteDto[]>(
    `financement/${financement.id}/mensualites`,
  );
  const [paiementOuvert, setPaiementOuvert] = useState<number | null>(null);
  const [historiqueOuvert, setHistoriqueOuvert] = useState<number | null>(null);
  const [enCours, setEnCours] = useState(false);
  const [erreurAction, setErreurAction] = useState<string | null>(null);

  const genererEcheancier = async () => {
    setErreurAction(null);
    setEnCours(true);
    try {
      await apiClient.post(`financement/${financement.id}/generer-echeancier`);
      recharger();
      onModifie();
    } catch (err) {
      setErreurAction(messageErreur(err, "Impossible de générer l'échéancier."));
    } finally {
      setEnCours(false);
    }
  };

  const mensualites = donnees ?? [];

  return (
    <div className="space-y-4">
      <div className="grid sm:grid-cols-4 gap-4 text-sm">
        <div>
          <p className="text-gray-500 text-xs">Montant total</p>
          <p className="font-semibold">{formaterMontant(financement.montantTotal)}</p>
        </div>
        <div>
          <p className="text-gray-500 text-xs">Apport initial</p>
          <p className="font-semibold">{formaterMontant(financement.apportInitial)}</p>
        </div>
        <div>
          <p className="text-gray-500 text-xs">À rembourser</p>
          <p className="font-semibold">{formaterMontant(financement.montantARembourser)}</p>
        </div>
        <div>
          <p className="text-gray-500 text-xs">Frais</p>
          <p className="font-semibold">{(financement.tauxFrais * 100).toLocaleString("fr-FR")} %</p>
        </div>
      </div>

      {erreurAction && <MessageErreur>{erreurAction}</MessageErreur>}

      {chargement ? (
        <p className="text-gray-500 text-sm">Chargement de l'échéancier...</p>
      ) : erreur ? (
        <MessageErreur>{erreur}</MessageErreur>
      ) : mensualites.length === 0 ? (
        <div className="flex flex-wrap items-center gap-3">
          <p className="text-sm text-gray-500">L'échéancier n'a pas encore été généré.</p>
          <button onClick={genererEcheancier} disabled={enCours} className={classeBoutonPrincipal(true)}>
            <CalendarPlus size={14} />
            {enCours ? "Génération..." : "Générer l'échéancier"}
          </button>
        </div>
      ) : (
        <>
          <BilanRetards financement={financement} />
          {mensualites.some((m) => m.statut !== "Payee") && (
            <SoldeAnticipe
              financementId={financement.id}
              reste={mensualites.reduce((total, m) => total + m.montantDu - m.montantPaye, 0)}
              onSolde={() => {
                recharger();
                onModifie();
              }}
            />
          )}
          <div className="bg-white rounded-xl border border-black/5 overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-gray-500 border-b border-black/5">
                  <th className="px-4 py-2 font-medium">Échéance</th>
                  <th className="px-4 py-2 font-medium">Échéance le</th>
                  <th className="px-4 py-2 font-medium">Dû</th>
                  <th className="px-4 py-2 font-medium">Payé</th>
                  <th className="px-4 py-2 font-medium">Réglée le</th>
                  <th className="px-4 py-2 font-medium">Suivi</th>
                  <th className="px-4 py-2" />
                </tr>
              </thead>
              <tbody>
                {mensualites.map((m) => (
                  <Fragment key={m.id}>
                    <tr className="border-b border-black/5 last:border-0">
                      <td className="px-4 py-2">n° {m.numeroEcheance}</td>
                      <td className="px-4 py-2 whitespace-nowrap">{formaterDate(m.dateEcheance)}</td>
                      <td className="px-4 py-2 whitespace-nowrap">{formaterMontant(m.montantDu)}</td>
                      <td className="px-4 py-2 whitespace-nowrap">{formaterMontant(m.montantPaye)}</td>
                      <td className="px-4 py-2 whitespace-nowrap text-gray-600">{formaterDate(m.dateReglement)}</td>
                      <td className="px-4 py-2">
                        <BadgePonctualite mensualite={m} />
                      </td>
                      <td className="px-4 py-2 text-right whitespace-nowrap">
                        {m.paiements.length > 0 && (
                          <button
                            onClick={() => setHistoriqueOuvert(historiqueOuvert === m.id ? null : m.id)}
                            className="inline-flex items-center gap-0.5 text-xs font-semibold text-gray-600 hover:text-immo-dark mr-3"
                          >
                            {historiqueOuvert === m.id ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                            {m.paiements.length} paiement{m.paiements.length > 1 ? "s" : ""}
                          </button>
                        )}
                        {m.statut !== "Payee" && (
                          <button
                            onClick={() => setPaiementOuvert(paiementOuvert === m.id ? null : m.id)}
                            className={classeBoutonSecondaire(true)}
                          >
                            <Banknote size={14} />
                            Paiement
                          </button>
                        )}
                      </td>
                    </tr>
                    {historiqueOuvert === m.id && (
                      <tr>
                        <td colSpan={7} className="px-6 py-3 bg-gray-50">
                          <HistoriquePaiements paiements={m.paiements} />
                        </td>
                      </tr>
                    )}
                    {paiementOuvert === m.id && (
                      <tr>
                        <td colSpan={7} className="px-4 py-3 bg-immo-cream/40">
                          <FormulairePaiement
                            cible={{ mensualiteId: m.id }}
                            montantSuggere={m.montantDu - m.montantPaye}
                            onAnnuler={() => setPaiementOuvert(null)}
                            onEnregistre={() => {
                              setPaiementOuvert(null);
                              recharger();
                              onModifie();
                            }}
                          />
                        </td>
                      </tr>
                    )}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
