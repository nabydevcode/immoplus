import { formaterMontant } from "../admin/outils";

/** Détail du prix d'un séjour : nuits, réduction, ménage, total ; la caution est affichée à part. */
export default function DetailPrix({
  nombreNuits,
  prixParNuit,
  reductionPct,
  reductionMontant,
  fraisMenage,
  montantTotal,
  caution,
}: {
  nombreNuits: number;
  prixParNuit: number;
  reductionPct?: number | null;
  reductionMontant?: number | null;
  fraisMenage?: number | null;
  montantTotal: number;
  caution?: number | null;
}) {
  return (
    <dl className="flex flex-col gap-2 text-sm">
      <div className="flex justify-between gap-3">
        <dt className="text-gray-600">
          {formaterMontant(prixParNuit)} × {nombreNuits} nuit{nombreNuits > 1 ? "s" : ""}
        </dt>
        <dd className="whitespace-nowrap">{formaterMontant(prixParNuit * nombreNuits)}</dd>
      </div>
      {!!reductionMontant && (
        <div className="flex justify-between gap-3 text-green-700">
          <dt>
            Réduction
            {reductionPct
              ? ` (${reductionPct} %${nombreNuits >= 28 ? ", au mois" : nombreNuits >= 7 ? ", à la semaine" : ""})`
              : ""}
          </dt>
          <dd className="whitespace-nowrap">− {formaterMontant(reductionMontant)}</dd>
        </div>
      )}
      {!!fraisMenage && (
        <div className="flex justify-between gap-3">
          <dt className="text-gray-600">Frais de ménage</dt>
          <dd className="whitespace-nowrap">{formaterMontant(fraisMenage)}</dd>
        </div>
      )}
      <div className="flex justify-between gap-3 border-t border-black/10 pt-2 text-base">
        <dt className="font-bold">Total</dt>
        <dd className="font-extrabold whitespace-nowrap">{formaterMontant(montantTotal)}</dd>
      </div>
      {!!caution && (
        <div className="flex justify-between gap-3 text-xs text-gray-500 bg-immo-dark/[0.03] rounded-lg px-3 py-2">
          <dt>Caution remboursable (non incluse)</dt>
          <dd className="whitespace-nowrap font-semibold">{formaterMontant(caution)}</dd>
        </div>
      )}
    </dl>
  );
}
