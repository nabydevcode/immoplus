import { useState, type ReactNode } from "react";
import { Home as HomeIcon } from "lucide-react";
import type { PhotoDto } from "../../types/models";
import { useSourcesPhotos } from "../../hooks/useSourcesPhotos";

function Illustration({ className, taille }: { className: string; taille: number }) {
  return (
    <div
      className={`bg-gradient-to-br from-immo-dark to-immo-dark-light rounded-2xl flex items-center justify-center ${className}`}
    >
      <HomeIcon className="text-white/15" size={taille} />
    </div>
  );
}

/**
 * Galerie de la fiche d'un appartement : grande photo + miniatures cliquables.
 * `action` s'affiche par-dessus la grande photo (ex. bouton de visite 360°).
 * `protegees` : photos servies seulement avec le jeton (logement ou hôte désactivé).
 */
export default function Galerie({
  photos = [],
  action,
  protegees,
}: {
  photos?: PhotoDto[];
  action?: ReactNode;
  protegees?: boolean;
}) {
  const [actif, setActif] = useState(0);
  // Grande photo : taille complète ; vignettes : miniatures, plus légères en 3G/4G.
  const source = useSourcesPhotos(photos, protegees);
  const miniature = useSourcesPhotos(photos, protegees, "miniature");
  const surcouche = action && <div className="absolute top-3 left-3 z-10">{action}</div>;

  if (photos.length === 0) {
    return (
      <div className="relative grid grid-cols-1 md:grid-cols-4 gap-2 mb-8">
        {surcouche}
        <Illustration className="md:col-span-2 md:row-span-2 h-64 md:h-96" taille={64} />
        {[0, 1, 2, 3].map((i) => (
          <Illustration key={i} className="hidden md:flex h-[11.5rem]" taille={32} />
        ))}
      </div>
    );
  }

  const photoActive = photos[Math.min(actif, photos.length - 1)];
  return (
    <div className="mb-8">
      <div className="relative">
        {surcouche}
        <img
          src={source(photoActive)}
          alt={`Photo ${actif + 1} sur ${photos.length}`}
          decoding="async"
          className="w-full h-64 md:h-[28rem] object-cover rounded-2xl bg-gray-100"
        />
      </div>
      {photos.length > 1 && (
        <div className="flex gap-2 mt-2 overflow-x-auto pb-1">
          {photos.map((photo, i) => (
            <button
              key={photo.id}
              onClick={() => setActif(i)}
              aria-label={`Afficher la photo ${i + 1}`}
              className={`shrink-0 rounded-lg overflow-hidden ring-2 transition ${
                i === actif ? "ring-immo-gold" : "ring-transparent opacity-70 hover:opacity-100"
              }`}
            >
              <img
                src={miniature(photo)}
                alt=""
                loading="lazy"
                decoding="async"
                className="w-24 h-16 md:w-28 md:h-20 object-cover bg-gray-100"
              />
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
