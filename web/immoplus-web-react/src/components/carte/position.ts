/**
 * Positions géographiques des logements. Ce module n'importe pas Leaflet : il peut être utilisé
 * par les pages chargées immédiatement, la carte elle-même restant téléchargée à la demande.
 */

export interface Position {
  lat: number;
  lng: number;
}

/** Bornes de la partie visible de la carte (GET appartement/lister?nord=&sud=&est=&ouest=). */
export interface Zone {
  nord: number;
  sud: number;
  est: number;
  ouest: number;
}

/** Centre par défaut : Conakry. */
export const CONAKRY: Position = { lat: 9.537, lng: -13.6773 };

export const CLES_ZONE = ["nord", "sud", "est", "ouest"] as const;

export const positionDe = (lat: number | null | undefined, lng: number | null | undefined): Position | null =>
  lat != null && lng != null ? { lat, lng } : null;

/** Zone lue dans l'URL (les 4 bornes ensemble), sinon null. */
export function zoneDepuisUrl(parametres: URLSearchParams): Zone | null {
  const valeurs = CLES_ZONE.map((cle) => Number(parametres.get(cle) ?? NaN));
  if (valeurs.some((v) => !Number.isFinite(v))) return null;
  const [nord, sud, est, ouest] = valeurs;
  return { nord, sud, est, ouest };
}

/** Arrondi à 5 décimales (≈ 1 m) : suffisant et lisible dans l'URL. */
export const arrondir = (n: number) => Math.round(n * 1e5) / 1e5;

export const formaterPosition = (p: Position) => `${p.lat.toFixed(5)}, ${p.lng.toFixed(5)}`;

/** Lien d'itinéraire vers le logement (application de cartes du téléphone). */
export const lienItineraire = (p: Position) => `https://www.google.com/maps/dir/?api=1&destination=${p.lat},${p.lng}`;
