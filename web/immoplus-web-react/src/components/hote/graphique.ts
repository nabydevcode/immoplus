/** Couleurs des revenus : séjours en or, loyers en bleu, remboursements (déduits, sous l'axe) en rouge. */
export const COULEURS_REVENUS = { sejours: "#b07d24", loyers: "#2277b8", remboursements: "#c0392b" } as const;

/** Échelle « ronde » de l'axe : maximum et pas (1, 2 ou 5 × 10ⁿ), 4 graduations environ. */
export function graduations(valeurMax: number) {
  if (valeurMax <= 0) return { max: 1, pas: 1 };
  const brut = valeurMax / 4;
  const puissance = 10 ** Math.floor(Math.log10(brut));
  const pas = [1, 2, 5, 10].map((m) => m * puissance).find((p) => p >= brut) ?? 10 * puissance;
  return { max: Math.ceil(valeurMax / pas) * pas, pas };
}
