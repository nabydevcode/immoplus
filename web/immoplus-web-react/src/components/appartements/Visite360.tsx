import { useEffect, useRef, useState } from "react";
import { Viewer } from "@photo-sphere-viewer/core";
import "@photo-sphere-viewer/core/index.css";
import { Hand, X } from "lucide-react";
import type { PhotoDto } from "../../types/models";
import { useSourcesPhotos } from "../../hooks/useSourcesPhotos";

/**
 * Visite virtuelle plein écran : un panorama 360° par pièce, que l'on fait tourner à la souris ou au doigt.
 * Chargé à la demande (React.lazy) pour ne pas alourdir les autres pages avec three.js.
 */
export default function Visite360({
  panoramas,
  titre,
  onFermer,
  protegees,
}: {
  panoramas: PhotoDto[];
  /** Panoramas servis seulement avec le jeton (logement ou hôte désactivé) : chargés en local d'abord. */
  protegees?: boolean;
  titre: string;
  onFermer: () => void;
}) {
  const conteneur = useRef<HTMLDivElement>(null);
  const viewer = useRef<Viewer | null>(null);
  const [actif, setActif] = useState(0);
  const [aide, setAide] = useState(true);
  const source = useSourcesPhotos(panoramas, protegees);
  // Panorama d'ouverture : le lecteur n'est créé qu'une fois (dès qu'il est disponible),
  // les pièces changent ensuite via setPanorama.
  const panoramaInitial = source(panoramas[0]);
  const panoramaAffiche = useRef<string | null>(null);
  const panoramaActif = source(panoramas[actif]);

  // Création unique du lecteur, détruit à la fermeture.
  // La création est différée d'un tick : en développement, React monte puis démonte aussitôt le composant
  // (StrictMode) ; un premier lecteur détruit annulerait le téléchargement du panorama que le second attend,
  // car three.js mutualise les requêtes vers une même URL. Le délai annulé, ce premier lecteur n'existe jamais.
  // panoramaInitial ne change plus une fois défini (URL publique, ou URL locale du panorama protégé).
  useEffect(() => {
    if (!panoramaInitial) return;
    const minuteur = window.setTimeout(() => {
      if (!conteneur.current) return;
      panoramaAffiche.current = panoramaInitial;
      viewer.current = new Viewer({
        container: conteneur.current,
        panorama: panoramaInitial,
        navbar: ["zoom", "move", "fullscreen"],
        defaultZoomLvl: 0, // champ de vision le plus large : on voit la pièce entière à l'ouverture
        touchmoveTwoFingers: false,
        mousewheelCtrlKey: false,
        lang: {
          zoom: "Zoom",
          zoomOut: "Dézoomer",
          zoomIn: "Zoomer",
          moveUp: "Haut",
          moveDown: "Bas",
          moveLeft: "Gauche",
          moveRight: "Droite",
          fullscreen: "Plein écran",
          loadError: "Le panorama n'a pas pu être chargé",
          webglError: "Votre navigateur ne permet pas d'afficher la visite 360°",
        },
      });
      viewer.current.addEventListener("position-updated", () => setAide(false), { once: true });
    }, 0);
    return () => {
      window.clearTimeout(minuteur);
      viewer.current?.destroy();
      viewer.current = null;
    };
  }, [panoramaInitial]);

  // Changement de pièce (ou panorama protégé arrivé après le clic).
  useEffect(() => {
    if (!viewer.current || !panoramaActif || panoramaActif === panoramaAffiche.current) return;
    panoramaAffiche.current = panoramaActif;
    viewer.current.setPanorama(panoramaActif);
  }, [panoramaActif]);

  useEffect(() => {
    const touche = (e: KeyboardEvent) => e.key === "Escape" && onFermer();
    window.addEventListener("keydown", touche);
    return () => window.removeEventListener("keydown", touche);
  }, [onFermer]);

  return (
    <div
      className="fixed inset-0 z-[100] bg-black flex flex-col"
      role="dialog"
      aria-modal="true"
      aria-label={`Visite 360° : ${titre}`}
    >
      <div className="flex items-center gap-3 px-4 py-3 bg-immo-dark text-immo-cream">
        <div className="min-w-0 flex-1">
          <p className="text-xs text-immo-gold font-bold uppercase tracking-wider">Visite 360°</p>
          <p className="font-semibold truncate">{titre}</p>
        </div>
        <button onClick={onFermer} className="p-2 rounded-lg hover:bg-white/10" aria-label="Fermer la visite">
          <X size={22} />
        </button>
      </div>

      {panoramas.length > 1 && (
        <div className="flex gap-2 px-4 py-2 bg-immo-dark/95 overflow-x-auto">
          {panoramas.map((p, i) => (
            <button
              key={p.id}
              onClick={() => setActif(i)}
              className={`shrink-0 px-3 py-1.5 rounded-full text-sm font-semibold transition-colors ${
                i === actif ? "bg-immo-gold text-immo-dark" : "bg-white/10 text-immo-cream hover:bg-white/20"
              }`}
            >
              {p.libelle || `Pièce ${i + 1}`}
            </button>
          ))}
        </div>
      )}

      <div className="relative flex-1">
        <div ref={conteneur} className="absolute inset-0" />
        {aide && (
          <p className="pointer-events-none absolute bottom-16 left-1/2 -translate-x-1/2 inline-flex items-center gap-2 bg-black/60 text-white text-sm px-4 py-2 rounded-full">
            <Hand size={16} />
            Faites glisser pour regarder autour de vous
          </p>
        )}
      </div>
    </div>
  );
}
