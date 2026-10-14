import { Receipt } from "lucide-react";
import { formaterDate, formaterMontant, useChargement } from "../admin/outils";
import { MessageErreur } from "../admin/ui";
import type { PaiementEnregistreDto } from "../../types/models";
import BoutonPdf from "../documents/BoutonPdf";
import { recuPaiement, recuSejour } from "../documents/pdf";

/** Paiements reçus pour une réservation (voyageur, hôte, admin). Pour relire la liste, changer sa `key`. */
export default function HistoriquePaiements({ reservationId }: { reservationId: number }) {
  const { donnees, chargement, erreur } = useChargement<PaiementEnregistreDto[]>(
    `reservation/${reservationId}/paiements`,
  );
  const paiements = [...(donnees ?? [])].sort((a, b) => b.datePaiement.localeCompare(a.datePaiement));

  return (
    <section className="border border-black/5 rounded-xl p-4">
      <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
        <h3 className="font-bold text-sm flex items-center gap-1.5">
          <Receipt size={15} className="text-immo-gold" />
          Historique des paiements
        </h3>
        {paiements.length > 0 && <BoutonPdf fichier={recuSejour(reservationId)} libelle="Reçu du séjour (PDF)" />}
      </div>
      {chargement ? (
        <p className="text-sm text-gray-500">Chargement...</p>
      ) : erreur ? (
        <MessageErreur>{erreur}</MessageErreur>
      ) : paiements.length === 0 ? (
        <p className="text-sm text-gray-500">Aucun paiement reçu pour cette réservation.</p>
      ) : (
        <ul className="divide-y divide-black/5">
          {paiements.map((p) => (
            <li key={p.id} className="py-2 flex flex-wrap items-baseline justify-between gap-x-4 gap-y-0.5 text-sm">
              <span className="min-w-0">
                <span className="font-semibold">{p.modePaiement}</span>
                <span className="text-gray-500"> · {formaterDate(p.datePaiement)}</span>
                {p.reference && <span className="block text-xs text-gray-400 break-all">Réf. {p.reference}</span>}
              </span>
              <span className="flex items-center gap-3">
                <BoutonPdf fichier={recuPaiement(p.id)} libelle="Reçu (PDF)" lien />
                <strong className="text-green-700 whitespace-nowrap">{formaterMontant(p.montant)}</strong>
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
