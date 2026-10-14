import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { apiClient, ApiError } from "../../api/client";
import { useAuth } from "../../context/auth";
import { FavorisContext, type FavorisValeur } from "./favoris";

/** Identifiants des annonces favorites de l'utilisateur connecté, partagés par les cartes, la fiche et la page « Mes favoris ». */
export default function FavorisProvider({ children }: { children: ReactNode }) {
  const { estConnecte, roleUtilisateur, utilisateurId } = useAuth();
  const actif = estConnecte && roleUtilisateur !== "Admin" && utilisateurId !== null;
  // Les favoris chargés sont rattachés au compte : ceux d'un compte précédent ne s'affichent jamais.
  const [etat, setEtat] = useState<{ pour: number | null; ids: Set<number> }>({ pour: null, ids: new Set() });

  useEffect(() => {
    if (!actif) return;
    let annule = false;
    apiClient
      .get<number[]>("favoris/ids")
      .then((ids) => {
        if (!annule) setEtat({ pour: utilisateurId, ids: new Set(ids ?? []) });
      })
      .catch(() => {
        // Facultatif : sans la liste, les cœurs restent vides.
      });
    return () => {
      annule = true;
    };
  }, [actif, utilisateurId]);

  const ids = useMemo(
    () => (actif && etat.pour === utilisateurId ? etat.ids : new Set<number>()),
    [actif, etat, utilisateurId],
  );

  const modifier = useCallback(
    (appartementId: number, ajouter: boolean) =>
      setEtat((e) => {
        const suivants = new Set(e.ids);
        if (ajouter) suivants.add(appartementId);
        else suivants.delete(appartementId);
        return { pour: utilisateurId, ids: suivants };
      }),
    [utilisateurId],
  );

  const basculer = useCallback(
    async (appartementId: number) => {
      const ajouter = !ids.has(appartementId);
      modifier(appartementId, ajouter);
      try {
        if (ajouter) await apiClient.post(`favoris/${appartementId}`);
        else await apiClient.delete(`favoris/${appartementId}`);
      } catch (err) {
        // Retrait d'une annonce supprimée entre-temps (404) : le favori n'existe plus, le retrait est acquis.
        if (!ajouter && err instanceof ApiError && err.statut === 404) return;
        modifier(appartementId, !ajouter);
        throw err;
      }
    },
    [ids, modifier],
  );

  const valeur = useMemo<FavorisValeur>(
    () => ({ actif, estFavori: (id) => ids.has(id), basculer }),
    [actif, ids, basculer],
  );

  return <FavorisContext.Provider value={valeur}>{children}</FavorisContext.Provider>;
}
