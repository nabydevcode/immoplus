import { useState } from "react";
import L from "leaflet";
import { MapContainer, Marker, TileLayer, useMap, useMapEvents } from "react-leaflet";
import { Crosshair, Trash2 } from "lucide-react";
import { iconePin, TUILES } from "./leaflet";
import { arrondir, CONAKRY, formaterPosition, type Position } from "./position";
import { classeBoutonSecondaire } from "../admin/outils";

function ClicCarte({ onChoisir }: { onChoisir: (p: Position) => void }) {
  useMapEvents({ click: (e) => onChoisir({ lat: arrondir(e.latlng.lat), lng: arrondir(e.latlng.lng) }) });
  return null;
}

/** Bouton « Ma position » : centre la carte et place le marqueur sur la position du téléphone. */
function BoutonMaPosition({ onChoisir }: { onChoisir: (p: Position) => void }) {
  const carte = useMap();
  const [etat, setEtat] = useState<"repos" | "recherche" | "erreur">("repos");
  if (!("geolocation" in navigator)) return null;
  const localiser = () => {
    setEtat("recherche");
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        const p = { lat: arrondir(pos.coords.latitude), lng: arrondir(pos.coords.longitude) };
        onChoisir(p);
        carte.setView([p.lat, p.lng], 17);
        setEtat("repos");
      },
      () => setEtat("erreur"),
      { enableHighAccuracy: true, timeout: 15_000 },
    );
  };
  return (
    <div className="leaflet-top leaflet-right">
      {/* Sans cela, le clic sur le bouton placerait aussi le marqueur à cet endroit. */}
      <div className="leaflet-control" ref={(el) => void (el && L.DomEvent.disableClickPropagation(el))}>
        <button
          type="button"
          onClick={localiser}
          className="inline-flex items-center gap-1.5 bg-white text-gray-800 text-xs font-semibold px-3 py-2 rounded-lg shadow border border-black/10 hover:bg-gray-50"
        >
          <Crosshair size={14} />
          {etat === "recherche" ? "Localisation..." : etat === "erreur" ? "Position indisponible" : "Ma position"}
        </button>
      </div>
    </div>
  );
}

/**
 * Choix de la position d'un logement : clic sur la carte ou déplacement du marqueur.
 * À charger avec React.lazy.
 */
export default function SelecteurPosition({
  valeur,
  onChange,
}: {
  valeur: Position | null;
  onChange: (p: Position | null) => void;
}) {
  const [centre] = useState(valeur ?? CONAKRY);
  return (
    <div>
      <div className="isolate h-72 rounded-xl overflow-hidden border border-gray-300">
        <MapContainer center={[centre.lat, centre.lng]} zoom={valeur ? 16 : 13} className="h-full w-full">
          <TileLayer url={TUILES.url} attribution={TUILES.attribution} />
          <ClicCarte onChoisir={onChange} />
          <BoutonMaPosition onChoisir={onChange} />
          {valeur && (
            <Marker
              position={[valeur.lat, valeur.lng]}
              icon={iconePin}
              draggable
              eventHandlers={{
                dragend: (e) => {
                  const p = e.target.getLatLng();
                  onChange({ lat: arrondir(p.lat), lng: arrondir(p.lng) });
                },
              }}
            />
          )}
        </MapContainer>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2 mt-2 text-xs text-gray-500">
        <span>
          {valeur
            ? `Position choisie : ${formaterPosition(valeur)} (déplacez le marqueur pour l'ajuster)`
            : "Cliquez sur la carte à l'emplacement du logement."}
        </span>
        {valeur && (
          <button type="button" onClick={() => onChange(null)} className={classeBoutonSecondaire(true)}>
            <Trash2 size={13} />
            Retirer la position
          </button>
        )}
      </div>
    </div>
  );
}
