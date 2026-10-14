import { useMemo } from "react";
import { useChargement } from "../admin/outils";
import type { PeriodeCalendrierDto } from "../../types/models";
import { ajouterMois, moisDe, nuitsOccupees } from "./dates";

/**
 * Périodes occupées d'un logement, de `aujourdhui` jusqu'à la fin des deux mois affichés
 * (au moins six mois) : une seule requête tant qu'on reste dans cette fenêtre.
 */
export function useCalendrier(appartementId: number, aujourdhui: string, moisAffiche: string) {
  const finFenetre = ajouterMois(moisAffiche, 2);
  const finMin = ajouterMois(moisDe(aujourdhui), 7);
  const au = `${finFenetre > finMin ? finFenetre : finMin}-01`;
  const chargement = useChargement<PeriodeCalendrierDto[]>(
    `appartement/${appartementId}/calendrier?du=${aujourdhui}&au=${au}`,
  );
  const nuits = useMemo(() => nuitsOccupees(chargement.donnees ?? []), [chargement.donnees]);
  return { ...chargement, nuits };
}
