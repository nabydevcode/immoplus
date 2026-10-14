import { useState } from "react";
import { formaterMontant, formaterMontantCompact } from "../admin/outils";
import { MOIS } from "../sejour/dates";
import { COULEURS_REVENUS, graduations } from "./graphique";

type Serie = "sejours" | "loyers" | "remboursements";
const LIBELLES: Record<Serie, string> = {
  sejours: "Séjours courts",
  loyers: "Loyers",
  remboursements: "Remboursements (déduits)",
};

/**
 * Revenus par mois en barres empilées (séjours courts en bas, loyers au-dessus), en CSS ;
 * les remboursements aux voyageurs, déduits des revenus, descendent sous l'axe.
 * Survol ou focus d'un mois : détail ; tableau des valeurs dépliable pour l'accessibilité.
 */
export default function GraphiqueRevenus({
  annee,
  revenus,
}: {
  annee: number;
  revenus: { mois: number; sejours: number; loyers: number; remboursements?: number }[];
}) {
  const [actif, setActif] = useState<number | null>(null);
  const parMois = Array.from({ length: 12 }, (_, i) => {
    const r = revenus.find((x) => x.mois === i + 1);
    return {
      mois: i + 1,
      sejours: r?.sejours ?? 0,
      loyers: r?.loyers ?? 0,
      remboursements: Math.abs(r?.remboursements ?? 0),
    };
  });
  const net = (m: (typeof parMois)[number]) => m.sejours + m.loyers - m.remboursements;
  // Une série entièrement vide n'est pas tracée (ni dans la légende).
  const series = (["sejours", "loyers", "remboursements"] as Serie[]).filter((s) => parMois.some((m) => m[s] > 0));
  const avecRemboursements = series.includes("remboursements");
  // Axe : de -bas (remboursements) à +haut (encaissements), avec le même pas.
  const maxHaut = Math.max(...parMois.map((m) => m.sejours + m.loyers), 0);
  const maxBas = Math.max(...parMois.map((m) => m.remboursements), 0);
  const { pas } = graduations(Math.max(maxHaut, maxBas));
  const haut = Math.ceil(maxHaut / pas) * pas;
  const bas = Math.ceil(maxBas / pas) * pas;
  const etendue = haut + bas || 1;
  const position = (v: number) => ((v + bas) / etendue) * 100;
  const ticks = Array.from({ length: Math.round(etendue / pas) + 1 }, (_, i) => i * pas - bas);
  const detail = actif !== null ? parMois[actif - 1] : null;

  if (series.length === 0) {
    return <p className="text-sm text-gray-500 py-10 text-center">Aucun revenu enregistré en {annee}.</p>;
  }

  return (
    <div>
      {series.length > 1 && (
        <ul className="flex flex-wrap gap-x-4 gap-y-1 mb-4 text-xs text-gray-600">
          {series.map((s) => (
            <li key={s} className="inline-flex items-center gap-1.5">
              <span className="w-3 h-3 rounded-sm" style={{ backgroundColor: COULEURS_REVENUS[s] }} />
              {LIBELLES[s]}
            </li>
          ))}
        </ul>
      )}

      <div className="flex gap-2 pt-2">
        {/* Axe des montants */}
        <div className="relative w-9 shrink-0 h-48 text-[10px] text-gray-400 text-right" aria-hidden="true">
          {ticks.map((t) => (
            <span key={t} className="absolute right-0 -translate-y-1/2" style={{ bottom: `${position(t)}%` }}>
              {t < 0 ? "−" : ""}
              {formaterMontantCompact(Math.abs(t))}
            </span>
          ))}
        </div>

        <div className="relative flex-1 min-w-0">
          <div className="relative h-48">
            {ticks.map((t) => (
              <div
                key={t}
                className={`absolute inset-x-0 border-t ${t === 0 ? "border-gray-300" : "border-gray-100"}`}
                style={{ bottom: `${position(t)}%` }}
                aria-hidden="true"
              />
            ))}
            <div className="absolute inset-0 flex items-end gap-[2px] sm:gap-1.5">
              {parMois.map((m) => {
                return (
                  <button
                    key={m.mois}
                    type="button"
                    onMouseEnter={() => setActif(m.mois)}
                    onMouseLeave={() => setActif((a) => (a === m.mois ? null : a))}
                    onFocus={() => setActif(m.mois)}
                    onBlur={() => setActif((a) => (a === m.mois ? null : a))}
                    onClick={() => setActif((a) => (a === m.mois ? null : m.mois))}
                    aria-label={`${MOIS[m.mois - 1]} ${annee} : ${formaterMontant(net(m))}`}
                    className={`relative flex-1 h-full rounded-t-md transition-colors ${
                      actif === m.mois ? "bg-immo-dark/[0.04]" : ""
                    }`}
                  >
                    <span
                      className="absolute inset-x-0 flex flex-col justify-end items-center gap-[2px]"
                      style={{ bottom: `${position(0)}%`, height: `${((m.sejours + m.loyers) / etendue) * 100}%` }}
                    >
                      {/* Empilement de haut en bas : loyers puis séjours ; seul le segment du haut est arrondi. */}
                      {(["loyers", "sejours"] as Serie[])
                        .filter((s) => m[s] > 0)
                        .map((s, i) => (
                          <span
                            key={s}
                            className={`block w-full max-w-8 ${i === 0 ? "rounded-t" : ""}`}
                            style={{
                              height: `calc(${(m[s] / (m.sejours + m.loyers)) * 100}% - 1px)`,
                              minHeight: 2,
                              backgroundColor: COULEURS_REVENUS[s],
                            }}
                          />
                        ))}
                    </span>
                    {m.remboursements > 0 && (
                      <span
                        className="absolute inset-x-0 flex justify-center"
                        style={{ top: `${100 - position(0)}%`, height: `${(m.remboursements / etendue) * 100}%` }}
                      >
                        <span
                          className="block w-full max-w-8 h-full rounded-b"
                          style={{ minHeight: 2, backgroundColor: COULEURS_REVENUS.remboursements }}
                        />
                      </span>
                    )}
                  </button>
                );
              })}
            </div>

            {detail && (
              <div
                className="absolute z-10 -top-2 w-52 bg-immo-dark text-white text-xs rounded-lg px-3 py-2 shadow-lg pointer-events-none"
                // Centrée sur le mois, sans jamais sortir du graphique (largeur fixe de 13 rem).
                style={{
                  left: `clamp(0rem, calc(${((detail.mois - 0.5) / 12) * 100}% - 6.5rem), calc(100% - 13rem))`,
                  transform: "translateY(-100%)",
                }}
                role="status"
              >
                <p className="font-bold capitalize mb-1">
                  {MOIS[detail.mois - 1]} {annee}
                </p>
                {(["sejours", "loyers", "remboursements"] as Serie[])
                  .filter((s) => s !== "remboursements" || avecRemboursements)
                  .map((s) => (
                    <p key={s} className="flex items-center gap-1.5">
                      <span className="w-2 h-2 rounded-sm" style={{ backgroundColor: COULEURS_REVENUS[s] }} />
                      {s === "remboursements" ? "Remboursements" : LIBELLES[s]} :{" "}
                      {s === "remboursements" && detail[s] > 0 ? "− " : ""}
                      {formaterMontant(detail[s])}
                    </p>
                  ))}
                <p className="font-semibold mt-1">Revenus nets : {formaterMontant(net(detail))}</p>
              </div>
            )}
          </div>

          <div className="flex gap-[2px] sm:gap-1.5 mt-1.5 text-[10px] sm:text-xs text-gray-500" aria-hidden="true">
            {parMois.map((m) => (
              <span key={m.mois} className="flex-1 text-center truncate">
                <span className="sm:hidden">{MOIS[m.mois - 1][0].toUpperCase()}</span>
                <span className="hidden sm:inline">{MOIS[m.mois - 1].slice(0, 3)}</span>
              </span>
            ))}
          </div>
        </div>
      </div>

      <details className="mt-4 text-sm">
        <summary className="cursor-pointer text-gray-500 hover:text-immo-dark text-xs font-semibold">
          Voir les montants par mois
        </summary>
        <div className="overflow-x-auto mt-2">
          <table className="w-full text-xs">
            <thead>
              <tr className="text-left text-gray-500 border-b border-black/5">
                <th className="py-1.5 pr-3 font-medium">Mois</th>
                <th className="py-1.5 pr-3 font-medium text-right">Séjours courts</th>
                <th className="py-1.5 pr-3 font-medium text-right">Loyers</th>
                {avecRemboursements && <th className="py-1.5 pr-3 font-medium text-right">Remboursements</th>}
                <th className="py-1.5 font-medium text-right">Revenus nets</th>
              </tr>
            </thead>
            <tbody>
              {parMois.map((m) => (
                <tr key={m.mois} className="border-b border-black/5 last:border-0">
                  <td className="py-1.5 pr-3 capitalize">{MOIS[m.mois - 1]}</td>
                  <td className="py-1.5 pr-3 text-right whitespace-nowrap">{formaterMontant(m.sejours)}</td>
                  <td className="py-1.5 pr-3 text-right whitespace-nowrap">{formaterMontant(m.loyers)}</td>
                  {avecRemboursements && (
                    <td
                      className={`py-1.5 pr-3 text-right whitespace-nowrap ${m.remboursements > 0 ? "text-red-700" : ""}`}
                    >
                      {m.remboursements > 0 ? `− ${formaterMontant(m.remboursements)}` : formaterMontant(0)}
                    </td>
                  )}
                  <td className="py-1.5 text-right font-semibold whitespace-nowrap">{formaterMontant(net(m))}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  );
}
