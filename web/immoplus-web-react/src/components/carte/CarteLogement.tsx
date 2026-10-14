import { Circle, MapContainer, Marker, TileLayer } from "react-leaflet";
import { iconePin, TUILES } from "./leaflet";
import type { Position } from "./position";

/** Rayon du cercle de position approximative (coordonnées publiques arrondies à ~100 m). */
const RAYON_APPROXIMATIF = 250;

/**
 * Carte d'un logement : cercle autour de la position approximative (public)
 * ou marqueur à l'adresse exacte (propriétaire, admin, réservation confirmée).
 * À charger avec React.lazy : Leaflet n'est téléchargé que si la carte s'affiche.
 */
export default function CarteLogement({
  position,
  approximatif = false,
  className = "h-64",
}: {
  position: Position;
  approximatif?: boolean;
  className?: string;
}) {
  const centre: [number, number] = [position.lat, position.lng];
  return (
    // isolate : les calques Leaflet (z-index 400+) restent sous la barre de navigation.
    <div className={`isolate rounded-2xl overflow-hidden border border-black/5 ${className}`}>
      <MapContainer center={centre} zoom={approximatif ? 15 : 16} scrollWheelZoom={false} className="h-full w-full">
        <TileLayer url={TUILES.url} attribution={TUILES.attribution} />
        {approximatif ? (
          <Circle
            center={centre}
            radius={RAYON_APPROXIMATIF}
            pathOptions={{ color: "#b07d24", weight: 2, fillColor: "#d4a94a", fillOpacity: 0.25 }}
          />
        ) : (
          <Marker position={centre} icon={iconePin} />
        )}
      </MapContainer>
    </div>
  );
}
