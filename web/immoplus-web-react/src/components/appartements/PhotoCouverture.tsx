import { Home as HomeIcon, Rotate3d } from "lucide-react";
import type { PhotoDto } from "../../types/models";
import { useSourcesPhotos } from "../../hooks/useSourcesPhotos";

/**
 * Première photo classique de l'annonce, ou une illustration neutre si le bailleur n'en a pas ajouté.
 * Un badge « 360° » signale une visite virtuelle disponible. Miniature (480 px) : cartes et listes ; la hauteur
 * est fixée par `className` pour éviter les sauts de mise en page.
 * `protegees` : photos servies seulement avec le jeton (logement ou hôte désactivé).
 */
export default function PhotoCouverture({
  photos,
  className = "",
  tailleIcone = 40,
  badge360 = true,
  protegees,
}: {
  photos?: PhotoDto[];
  className?: string;
  tailleIcone?: number;
  badge360?: boolean;
  protegees?: boolean;
}) {
  const couverture = photos?.find((p) => !p.est360);
  const source = useSourcesPhotos(couverture ? [couverture] : [], protegees, "miniature");
  const src = couverture ? source(couverture) : undefined;
  const aVisite360 = badge360 && photos?.some((p) => p.est360);
  return (
    <div className={`relative overflow-hidden ${className}`}>
      {couverture ? (
        src ? (
          <img
            src={src}
            alt=""
            loading="lazy"
            decoding="async"
            className="absolute inset-0 w-full h-full object-cover"
          />
        ) : (
          <div className="absolute inset-0 bg-gray-200 animate-pulse" />
        )
      ) : (
        <div className="absolute inset-0 bg-gradient-to-br from-immo-dark to-immo-dark-light flex items-center justify-center">
          <HomeIcon className="text-white/20" size={tailleIcone} />
        </div>
      )}
      {aVisite360 && (
        <span className="absolute top-2 left-2 inline-flex items-center gap-1 bg-immo-dark/80 text-immo-gold text-xs font-bold px-2 py-1 rounded-full">
          <Rotate3d size={13} />
          360°
        </span>
      )}
    </div>
  );
}
