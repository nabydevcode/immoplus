import { AlarmClock, CheckCircle2, Clock, History } from "lucide-react";
import { formaterDate, formaterMontant } from "../admin/outils";
import BoutonPdf from "../documents/BoutonPdf";
import { recuPaiement } from "../documents/pdf";
import type { FinancementDto, MensualiteDto, PaiementEnregistreDto } from "../../types/models";

const jours = (n: number) => `${n} jour${n > 1 ? "s" : ""}`;

/** Ponctualité d'une mensualité : à temps, payée en retard, ou en retard en ce moment. */
export function BadgePonctualite({
  mensualite: m,
  masculin = false,
}: {
  /** Une mensualité ou un loyer : seuls les champs de ponctualité sont utilisés. */
  mensualite: Pick<MensualiteDto, "ponctualite" | "joursRetard" | "montantPaye" | "montantDu">;
  /** « Payé » pour un loyer, « Payée » pour une mensualité. */
  masculin?: boolean;
}) {
  const e = masculin ? "" : "e";
  const partielle = m.montantPaye > 0 && m.montantPaye < m.montantDu;
  const styles = {
    ATemps: { classe: "bg-green-100 text-green-700", texte: `Payé${e} à temps` },
    PayeeEnRetard: { classe: "bg-amber-100 text-amber-800", texte: `Payé${e} avec ${jours(m.joursRetard)} de retard` },
    EnRetard: {
      classe: "bg-red-100 text-red-700",
      texte: `En retard de ${jours(m.joursRetard)}${partielle ? " (partielle)" : ""}`,
    },
    AVenir: { classe: "bg-gray-100 text-gray-600", texte: partielle ? `Partiellement payé${e}` : "À venir" },
  }[m.ponctualite];
  return (
    <span className={`text-xs font-semibold px-2.5 py-1 rounded-full whitespace-nowrap ${styles.classe}`}>
      {styles.texte}
    </span>
  );
}

/** Historique des paiements d'une mensualité (y compris les paiements partiels). */
export function HistoriquePaiements({ paiements }: { paiements: PaiementEnregistreDto[] }) {
  if (paiements.length === 0) return <p className="text-gray-500 text-sm">Aucun paiement enregistré.</p>;
  return (
    <ul className="space-y-1 text-sm">
      {paiements.map((p) => (
        <li key={p.id} className="flex flex-wrap items-center gap-x-3 gap-y-0.5 text-gray-700">
          <History size={14} className="text-gray-400" />
          <span className="font-medium">{formaterDate(p.datePaiement)}</span>
          <span>{formaterMontant(p.montant)}</span>
          <span className="text-gray-500">{p.modePaiement}</span>
          {p.reference && <span className="text-gray-400">réf. {p.reference}</span>}
          <BoutonPdf fichier={recuPaiement(p.id)} libelle="Reçu (PDF)" lien />
        </li>
      ))}
    </ul>
  );
}

/** Bilan de ponctualité d'un financement. */
export function BilanRetards({ financement: f }: { financement: FinancementDto }) {
  const payees = f.mensualitesPayeesATemps + f.mensualitesPayeesEnRetard;
  if (payees === 0 && f.mensualitesEnRetard === 0) return null;
  return (
    <div className="flex flex-wrap gap-2 text-xs font-semibold">
      <span className="inline-flex items-center gap-1 bg-green-50 text-green-700 px-2.5 py-1 rounded-full">
        <CheckCircle2 size={13} />
        {f.mensualitesPayeesATemps} payée{f.mensualitesPayeesATemps > 1 ? "s" : ""} à temps
      </span>
      {f.mensualitesPayeesEnRetard > 0 && (
        <span className="inline-flex items-center gap-1 bg-amber-50 text-amber-800 px-2.5 py-1 rounded-full">
          <Clock size={13} />
          {f.mensualitesPayeesEnRetard} payée{f.mensualitesPayeesEnRetard > 1 ? "s" : ""} en retard
        </span>
      )}
      {f.mensualitesEnRetard > 0 && (
        <span className="inline-flex items-center gap-1 bg-red-50 text-red-700 px-2.5 py-1 rounded-full">
          <AlarmClock size={13} />
          {f.mensualitesEnRetard} en retard actuellement ({formaterMontant(f.montantEnRetard)})
        </span>
      )}
      {f.joursRetardCumules > 0 && (
        <span className="inline-flex items-center gap-1 bg-gray-100 text-gray-700 px-2.5 py-1 rounded-full">
          Retard cumulé : {jours(f.joursRetardCumules)}
        </span>
      )}
    </div>
  );
}
