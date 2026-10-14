import { useEffect, useState } from "react";
import { apiClient, ApiError } from "../api/client";
import type { AppartementDto } from "../types/models";
import { messageErreur } from "../components/admin/outils";

/** Charge un appartement par son id (mêmes règles de visibilité que la liste) ; null s'il est introuvable. */
export function useAppartement(id: string | undefined) {
  // Résultat associé à l'id demandé : en changeant d'annonce, on repasse en chargement sans effet synchrone.
  const [resultat, setResultat] = useState<{
    id: string | undefined;
    appartement: AppartementDto | null;
    erreur: string | null;
  } | null>(null);

  useEffect(() => {
    let annule = false;
    const numero = Number(id);
    // Identifiant invalide : inutile d'interroger l'API, l'annonce est introuvable.
    const requete =
      Number.isInteger(numero) && numero > 0 ? apiClient.get<AppartementDto>(`appartement/${numero}`) : null;
    (requete ?? Promise.resolve(null))
      .then((appartement) => {
        if (!annule) setResultat({ id, appartement: appartement ?? null, erreur: null });
      })
      .catch((err) => {
        if (annule) return;
        // 404 : annonce inexistante ou masquée pour ce visiteur, affichée comme « introuvable ».
        const introuvable = err instanceof ApiError && err.statut === 404;
        setResultat({
          id,
          appartement: null,
          erreur: introuvable ? null : messageErreur(err, "Impossible de charger l'annonce."),
        });
      });
    return () => {
      annule = true;
    };
  }, [id]);

  const actuel = resultat?.id === id ? resultat : null;
  return { appartement: actuel?.appartement ?? null, chargement: actuel === null, erreur: actuel?.erreur ?? null };
}
