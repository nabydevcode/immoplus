import {
  AirVent,
  Car,
  CookingPot,
  Droplets,
  Fence,
  ShieldCheck,
  ShowerHead,
  Tv,
  WashingMachine,
  Waves,
  Wifi,
  Zap,
  type LucideIcon,
} from "lucide-react";

/** Équipements reconnus par l'API (toute autre clé est refusée). */
export const EQUIPEMENTS: { cle: string; libelle: string; icone: LucideIcon }[] = [
  { cle: "climatisation", libelle: "Climatisation", icone: AirVent },
  { cle: "groupe-electrogene", libelle: "Groupe électrogène", icone: Zap },
  { cle: "wifi", libelle: "Wi-Fi", icone: Wifi },
  { cle: "eau-courante", libelle: "Eau courante", icone: Droplets },
  { cle: "parking", libelle: "Parking", icone: Car },
  { cle: "gardien", libelle: "Gardien", icone: ShieldCheck },
  { cle: "cuisine", libelle: "Cuisine équipée", icone: CookingPot },
  { cle: "television", libelle: "Télévision", icone: Tv },
  { cle: "lave-linge", libelle: "Lave-linge", icone: WashingMachine },
  { cle: "piscine", libelle: "Piscine", icone: Waves },
  { cle: "balcon", libelle: "Balcon", icone: Fence },
  { cle: "eau-chaude", libelle: "Eau chaude", icone: ShowerHead },
];

/** Équipements connus d'une annonce, dans l'ordre de la liste de référence. */
export const equipementsDe = (cles: string[] | null | undefined) => EQUIPEMENTS.filter((e) => cles?.includes(e.cle));

/** « Kipé, Conakry » ; null si ni ville ni quartier. */
export function localisation(a: { ville?: string | null; quartier?: string | null }) {
  const morceaux = [a.quartier, a.ville].filter((m): m is string => !!m?.trim());
  return morceaux.length ? morceaux.join(", ") : null;
}

/** Résumé d'une annonce pour la description de la page (partage). */
export function descriptionAnnonce(a: {
  typeOffre: string;
  prixParNuit?: number | null;
  loyerMensuel?: number | null;
  ville?: string | null;
  quartier?: string | null;
  adresse: string;
}) {
  const prix =
    a.typeOffre === "LongueDuree"
      ? `${a.loyerMensuel?.toLocaleString("fr-FR") ?? "-"} GNF/mois`
      : `${a.prixParNuit?.toLocaleString("fr-FR") ?? "-"} GNF/nuit`;
  return `${a.typeOffre === "LongueDuree" ? "Location longue durée" : "Séjour court"} · ${localisation(a) ?? a.adresse} · ${prix}`;
}
