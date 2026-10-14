import { Fragment, useState } from "react";
import { AlarmClock, Banknote, CheckCircle2, ChevronDown, ChevronRight, Clock } from "lucide-react";
import FormulairePaiement from "../admin/FormulairePaiement";
import { classeBoutonSecondaire, formaterDate, formaterMontant, useChargement } from "../admin/outils";
import type { LoyersLocationDto } from "../../types/models";
import { BadgePonctualite, HistoriquePaiements } from "./SuiviPaiements";
import BoutonPdf from "../documents/BoutonPdf";
import { quittanceLoyer } from "../documents/pdf";

const mois = (iso: string) => new Date(iso).toLocaleDateString("fr-FR", { month: "long", year: "numeric" });

/**
 * Loyers mensuels sans frais, après la période couverte par l'avance.
 * `gestion` (admin) : enregistrement des paiements ; sinon lecture seule (locataire).
 */
export default function Loyers({ locationId, gestion = false }: { locationId: number; gestion?: boolean }) {
  const { donnees, chargement, erreur, recharger } = useChargement<LoyersLocationDto>(`location/${locationId}/loyers`);
  const [paiementOuvert, setPaiementOuvert] = useState<number | null>(null);
  const [historiqueOuvert, setHistoriqueOuvert] = useState<number | null>(null);

  if (chargement) return <p className="text-sm text-gray-500">Chargement des loyers...</p>;
  if (erreur || !donnees) return <p className="text-sm text-red-600">{erreur ?? "Loyers indisponibles."}</p>;

  const d = donnees;
  if (!d.debutLoyers) {
    return (
      <p className="text-sm text-gray-500">
        Les loyers ({formaterMontant(d.loyerMensuel)} par mois, sans frais) commenceront à la fin de la période couverte
        par l'avance, comptée à partir de la remise des clés.
      </p>
    );
  }
  if (d.loyers.length === 0) {
    return (
      <p className="text-sm text-gray-500">
        Loyer de {formaterMontant(d.loyerMensuel)} par mois, sans frais, à partir du{" "}
        <strong className="text-gray-700">{formaterDate(d.debutLoyers)}</strong> (fin de la période couverte par
        l'avance). Les échéances apparaîtront un mois avant.
      </p>
    );
  }

  return (
    <div className="space-y-3 text-sm">
      <p className="text-gray-600">
        Loyer de <strong>{formaterMontant(d.loyerMensuel)}</strong> par mois, sans frais, depuis le{" "}
        {formaterDate(d.debutLoyers)}.
      </p>
      {(d.payesATemps + d.payesEnRetard > 0 || d.enRetard > 0) && (
        <div className="flex flex-wrap gap-2 text-xs font-semibold">
          <span className="inline-flex items-center gap-1 bg-green-50 text-green-700 px-2.5 py-1 rounded-full">
            <CheckCircle2 size={13} />
            {d.payesATemps} payé{d.payesATemps > 1 ? "s" : ""} à temps
          </span>
          {d.payesEnRetard > 0 && (
            <span className="inline-flex items-center gap-1 bg-amber-50 text-amber-800 px-2.5 py-1 rounded-full">
              <Clock size={13} />
              {d.payesEnRetard} payé{d.payesEnRetard > 1 ? "s" : ""} en retard
            </span>
          )}
          {d.enRetard > 0 && (
            <span className="inline-flex items-center gap-1 bg-red-50 text-red-700 px-2.5 py-1 rounded-full">
              <AlarmClock size={13} />
              {d.enRetard} en retard actuellement ({formaterMontant(d.montantEnRetard)})
            </span>
          )}
          {d.joursRetardCumules > 0 && (
            <span className="bg-gray-100 text-gray-700 px-2.5 py-1 rounded-full">
              Retard cumulé : {d.joursRetardCumules} j
            </span>
          )}
        </div>
      )}
      <div className="bg-white rounded-xl border border-black/5 overflow-x-auto">
        <table className="w-full">
          <thead>
            <tr className="text-left text-gray-500 border-b border-black/5">
              <th className="px-4 py-2 font-medium">Mois</th>
              <th className="px-4 py-2 font-medium">À payer le</th>
              <th className="px-4 py-2 font-medium">Loyer</th>
              <th className="px-4 py-2 font-medium">Payé</th>
              <th className="px-4 py-2 font-medium">Réglé le</th>
              <th className="px-4 py-2 font-medium">Suivi</th>
              <th className="px-4 py-2" />
            </tr>
          </thead>
          <tbody>
            {[...d.loyers].reverse().map((l) => (
              <Fragment key={l.id}>
                <tr className="border-b border-black/5 last:border-0">
                  <td className="px-4 py-2 capitalize whitespace-nowrap">{mois(l.dateEcheance)}</td>
                  <td className="px-4 py-2 whitespace-nowrap">{formaterDate(l.dateEcheance)}</td>
                  <td className="px-4 py-2 whitespace-nowrap">{formaterMontant(l.montantDu)}</td>
                  <td className="px-4 py-2 whitespace-nowrap">{formaterMontant(l.montantPaye)}</td>
                  <td className="px-4 py-2 whitespace-nowrap text-gray-600">{formaterDate(l.dateReglement)}</td>
                  <td className="px-4 py-2">
                    <BadgePonctualite mensualite={l} masculin />
                  </td>
                  <td className="px-4 py-2 text-right whitespace-nowrap">
                    {l.paiements.length > 0 && (
                      <button
                        onClick={() => setHistoriqueOuvert(historiqueOuvert === l.id ? null : l.id)}
                        className="inline-flex items-center gap-0.5 text-xs font-semibold text-gray-600 hover:text-immo-dark mr-3"
                      >
                        {historiqueOuvert === l.id ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                        {l.paiements.length} paiement{l.paiements.length > 1 ? "s" : ""}
                      </button>
                    )}
                    {l.statut === "Payee" && (
                      <span className="mr-3 inline-block align-middle">
                        <BoutonPdf fichier={quittanceLoyer(l.id)} libelle="Quittance (PDF)" lien />
                      </span>
                    )}
                    {gestion && l.statut !== "Payee" && (
                      <button
                        onClick={() => setPaiementOuvert(paiementOuvert === l.id ? null : l.id)}
                        className={classeBoutonSecondaire(true)}
                      >
                        <Banknote size={14} />
                        Paiement
                      </button>
                    )}
                  </td>
                </tr>
                {historiqueOuvert === l.id && (
                  <tr>
                    <td colSpan={7} className="px-6 py-3 bg-gray-50">
                      <HistoriquePaiements paiements={l.paiements} />
                    </td>
                  </tr>
                )}
                {paiementOuvert === l.id && (
                  <tr>
                    <td colSpan={7} className="px-4 py-3 bg-immo-cream/40">
                      <FormulairePaiement
                        cible={{ loyerId: l.id }}
                        montantSuggere={l.montantDu - l.montantPaye}
                        onAnnuler={() => setPaiementOuvert(null)}
                        onEnregistre={() => {
                          setPaiementOuvert(null);
                          recharger();
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
    </div>
  );
}
