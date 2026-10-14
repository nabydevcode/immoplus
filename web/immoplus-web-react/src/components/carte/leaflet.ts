/** Réglages communs des cartes Leaflet (importé uniquement par les composants chargés paresseusement). */
import L from "leaflet";
import "leaflet/dist/leaflet.css";

export const TUILES = {
  url: "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
  attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
};

const echapper = (texte: string) =>
  texte.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);

/**
 * Icônes en HTML (divIcon) : pas d'image à servir. Le conteneur fait 0 × 0 px au point exact,
 * le contenu est décalé en CSS pour que sa pointe tombe sur la position.
 */
export const iconePin = L.divIcon({
  className: "",
  iconSize: [0, 0],
  iconAnchor: [0, 0],
  html: `<svg width="30" height="40" viewBox="0 0 30 40" style="position:absolute;left:-15px;top:-40px;filter:drop-shadow(0 2px 3px rgba(0,0,0,.35))">
    <path d="M15 0C6.7 0 0 6.7 0 15c0 11.2 15 25 15 25s15-13.8 15-25C30 6.7 23.3 0 15 0z" fill="#15161a"/>
    <circle cx="15" cy="15" r="6" fill="#d4a94a"/>
  </svg>`,
});

/** Étiquette de prix (« 450 k ») pour la carte des annonces. */
export function iconePrix(texte: string, actif = false) {
  return L.divIcon({
    className: "",
    iconSize: [0, 0],
    iconAnchor: [0, 0],
    html: `<span style="position:absolute;left:0;top:0;transform:translate(-50%,-100%);white-space:nowrap;
      padding:3px 8px;border-radius:999px;font:700 12px/1.3 'Plus Jakarta Sans',system-ui,sans-serif;
      box-shadow:0 1px 4px rgba(0,0,0,.3);border:1px solid rgba(0,0,0,.15);
      background:${actif ? "#15161a" : "#fff"};color:${actif ? "#d4a94a" : "#15161a"}">${echapper(texte)}</span>`,
  });
}
