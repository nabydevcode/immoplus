import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import L from "leaflet";
import { MapContainer, Marker, Popup, TileLayer, useMap, useMapEvents } from "react-leaflet";
import { MapPin, Search } from "lucide-react";
import type { AppartementDto } from "../../types/models";
import PhotoCouverture from "../appartements/PhotoCouverture";
import { ResumeNote } from "../avis/AffichageAvis";
import { localisation } from "../sejour/equipements";
import { iconePrix, TUILES } from "./leaflet";
import { formaterMontantCompact } from "../admin/outils";
import { arrondir, CONAKRY, type Zone } from "./position";

type Geolocalise = AppartementDto & { latitude: number; longitude: number };

/** Prix affiché : loyer mensuel (longue durée) ou prix par nuit (séjour court). */
const prixDe = (a: AppartementDto) =>
  a.typeOffre === "LongueDuree"
    ? { montant: a.loyerMensuel, unite: "GNF/mois" }
    : { montant: a.prixParNuit, unite: "GNF/nuit" };

/**
 * Cadre la carte sur la zone recherchée, sinon sur les logements affichés, et signale les déplacements
 * faits par l'utilisateur (pas ceux du cadrage) pour proposer « Rechercher dans cette zone ».
 */
function Cadrage({
  zone,
  logements,
  onDeplace,
}: {
  zone: Zone | null;
  logements: Geolocalise[];
  onDeplace: () => void;
}) {
  const carte = useMap();
  const ignorer = useRef(false);
  const dejaCadre = useRef(false);
  // Cible du cadrage sérialisée : l'effet ne se relance que si elle change vraiment.
  const cible = JSON.stringify(
    zone
      ? {
          parZone: true,
          points: [
            [zone.sud, zone.ouest],
            [zone.nord, zone.est],
          ],
        }
      : { parZone: false, points: logements.map((a) => [a.latitude, a.longitude]) },
  );

  useEffect(() => {
    const { parZone, points } = JSON.parse(cible) as { parZone: boolean; points: [number, number][] };
    // Une recherche par zone garde la vue choisie par l'utilisateur.
    if (points.length === 0 || (parZone && dejaCadre.current)) return;
    dejaCadre.current = true;
    ignorer.current = true;
    carte.fitBounds(
      L.latLngBounds(points),
      parZone ? { animate: false } : { animate: false, padding: [40, 40], maxZoom: 15 },
    );
    ignorer.current = false;
  }, [carte, cible]);

  useMapEvents({
    moveend: () => {
      if (!ignorer.current) onDeplace();
    },
  });
  return null;
}

/**
 * Carte des annonces : un marqueur avec le prix (par nuit ou par mois), une fenêtre vers la fiche,
 * et « Rechercher dans cette zone » après un déplacement. À charger avec React.lazy.
 */
export default function CarteAnnonces({
  appartements,
  lienFiche,
  zone,
  onRechercherZone,
}: {
  appartements: AppartementDto[];
  lienFiche: (a: AppartementDto) => string;
  zone: Zone | null;
  onRechercherZone: (zone: Zone) => void;
}) {
  const [carte, setCarte] = useState<L.Map | null>(null);
  const [deplacee, setDeplacee] = useState(false);
  const [selection, setSelection] = useState<number | null>(null);
  const geolocalises = appartements.filter((a): a is Geolocalise => a.latitude != null && a.longitude != null);

  const rechercher = () => {
    if (!carte) return;
    const b = carte.getBounds();
    setDeplacee(false);
    onRechercherZone({
      nord: arrondir(b.getNorth()),
      sud: arrondir(b.getSouth()),
      est: arrondir(b.getEast()),
      ouest: arrondir(b.getWest()),
    });
  };

  return (
    <div className="isolate relative h-[60vh] min-h-80 max-h-[640px] rounded-2xl overflow-hidden border border-black/5 shadow-sm">
      <MapContainer ref={setCarte} center={[CONAKRY.lat, CONAKRY.lng]} zoom={12} className="h-full w-full">
        <TileLayer url={TUILES.url} attribution={TUILES.attribution} />
        <Cadrage zone={zone} logements={geolocalises} onDeplace={() => setDeplacee(true)} />
        {geolocalises.map((a) => (
          <Marker
            key={a.id}
            position={[a.latitude, a.longitude]}
            icon={iconePrix(`${formaterMontantCompact(prixDe(a).montant)} GNF`, selection === a.id)}
            zIndexOffset={selection === a.id ? 1000 : 0}
            eventHandlers={{
              popupopen: () => setSelection(a.id),
              popupclose: () => setSelection((s) => (s === a.id ? null : s)),
            }}
          >
            <Popup offset={[0, -26]} minWidth={220} maxWidth={240}>
              <div className="font-sans -m-1">
                <PhotoCouverture
                  photos={a.photos}
                  protegees={a.photosProtegees}
                  className="h-24 w-full rounded-lg mb-2"
                  tailleIcone={24}
                />
                <p className="font-bold text-sm !m-0 truncate">{a.description || a.adresse}</p>
                <p className="text-xs text-gray-500 !m-0 flex items-center gap-1">
                  <MapPin size={12} className="shrink-0" />
                  <span className="truncate">{localisation(a) ?? a.adresse}</span>
                </p>
                <div className="flex items-center justify-between gap-2 mt-1.5">
                  <span className="text-sm font-extrabold">
                    {prixDe(a).montant?.toLocaleString("fr-FR") ?? "-"}{" "}
                    <span className="font-normal text-xs">{prixDe(a).unite}</span>
                  </span>
                  <ResumeNote note={a.noteMoyenne} nombre={a.nombreAvis} petit />
                </div>
                <Link
                  to={lienFiche(a)}
                  className="block text-center bg-immo-gold !text-immo-dark text-xs font-semibold py-2 rounded-lg mt-2 hover:bg-immo-gold-light"
                >
                  Voir l'annonce
                </Link>
              </div>
            </Popup>
          </Marker>
        ))}
      </MapContainer>

      {deplacee && selection === null && (
        <button
          type="button"
          onClick={rechercher}
          className="absolute top-3 left-1/2 -translate-x-1/2 z-[1000] inline-flex items-center gap-1.5 bg-immo-dark text-immo-cream text-sm font-semibold px-4 py-2 rounded-full shadow-lg hover:bg-immo-dark-light whitespace-nowrap"
        >
          <Search size={15} className="text-immo-gold" />
          Rechercher dans cette zone
        </button>
      )}
      {geolocalises.length < appartements.length && (
        <p className="absolute bottom-2 left-2 z-[1000] bg-white/90 text-gray-600 text-xs px-2.5 py-1 rounded-lg shadow max-w-[70%]">
          {appartements.length - geolocalises.length} logement(s) sans position ne figurent pas sur la carte.
        </p>
      )}
    </div>
  );
}
