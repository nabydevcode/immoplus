import { useCallback, useEffect, useState } from "react";
import { chargerFichierProtege, urlApi } from "../api/client";
import type { PhotoDto } from "../types/models";

type PhotoSource = Pick<PhotoDto, "id" | "url" | "urlMiniature">;

/** « miniature » (480 px) pour les cartes, listes et vignettes ; « complete » pour la galerie ouverte. */
export type TaillePhoto = "complete" | "miniature";

/** Chemin de la photo à la taille voulue (la photo complète si l'API ne fournit pas de miniature). */
const chemin = (photo: PhotoSource, taille: TaillePhoto) =>
  taille === "miniature" && photo.urlMiniature ? photo.urlMiniature : photo.url;

/**
 * Sources affichables des photos d'une annonce.
 * - Photos publiques : URL directe de l'API (`<img src>`).
 * - Photos protégées (logement ou hôte désactivé, `photosProtegees`) : l'API ne les sert qu'avec le jeton ;
 *   elles sont téléchargées avec l'en-tête Authorization puis affichées via une URL locale (blob),
 *   libérée au démontage ou quand la liste change. `undefined` tant que la photo n'est pas chargée.
 * - `taille` : miniature (cartes, vignettes) ou photo complète (galerie ouverte, visite 360°).
 */
export function useSourcesPhotos(
  photos: PhotoSource[] | undefined,
  protegees: boolean | undefined,
  taille: TaillePhoto = "complete",
) {
  // Clé stable de la liste à charger (vide si rien à charger avec le jeton).
  const cle = protegees ? (photos ?? []).map((p) => `${p.id}\u0000${chemin(p, taille)}`).join("\u0001") : "";
  const [charges, setCharges] = useState<{ cle: string; urls: ReadonlyMap<number, string> }>({
    cle: "",
    urls: new Map(),
  });

  useEffect(() => {
    if (!cle) return;
    let annule = false;
    const creees: string[] = [];
    for (const entree of cle.split("\u0001")) {
      const [id, url] = entree.split("\u0000");
      chargerFichierProtege(url)
        .then((local) => {
          if (annule) {
            URL.revokeObjectURL(local);
            return;
          }
          creees.push(local);
          setCharges((c) => ({ cle, urls: new Map(c.cle === cle ? c.urls : []).set(Number(id), local) }));
        })
        .catch(() => {
          // Photo indisponible : l'emplacement reste vide.
        });
    }
    return () => {
      annule = true;
      for (const local of creees) URL.revokeObjectURL(local);
    };
  }, [cle]);

  return useCallback(
    (photo: PhotoSource): string | undefined =>
      !protegees ? urlApi(chemin(photo, taille)) : charges.cle === cle ? charges.urls.get(photo.id) : undefined,
    [protegees, charges, cle, taille],
  );
}
