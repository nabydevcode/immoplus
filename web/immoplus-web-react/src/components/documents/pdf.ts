import { formaterReference } from "../admin/outils";

/** Document PDF généré par l'API : route à télécharger avec le jeton et nom du fichier enregistré. */
export interface DocumentPdf {
  chemin: string;
  nomFichier: string;
}

export const quittanceLoyer = (loyerId: number): DocumentPdf => ({
  chemin: `loyer/${loyerId}/quittance.pdf`,
  nomFichier: `quittance-loyer-${formaterReference(loyerId)}.pdf`,
});

export const recuSejour = (reservationId: number): DocumentPdf => ({
  chemin: `reservation/${reservationId}/recu.pdf`,
  nomFichier: `recu-sejour-${formaterReference(reservationId)}.pdf`,
});

export const recuPaiement = (paiementId: number): DocumentPdf => ({
  chemin: `paiement/${paiementId}/recu.pdf`,
  nomFichier: `recu-paiement-${formaterReference(paiementId)}.pdf`,
});

export const contratLocation = (locationId: number): DocumentPdf => ({
  chemin: `location/${locationId}/contrat.pdf`,
  nomFichier: `contrat-location-${formaterReference(locationId)}.pdf`,
});

export const etatDesLieuxPdf = (reservationId: number): DocumentPdf => ({
  chemin: `reservation/${reservationId}/etat-des-lieux.pdf`,
  nomFichier: `etat-des-lieux-${formaterReference(reservationId)}.pdf`,
});

/** Taille d'un fichier lisible : 1 536 → « 1,5 Ko ». */
export function formaterTaille(octets: number) {
  if (octets < 1024) return `${octets} o`;
  const unites = ["Ko", "Mo", "Go"];
  let valeur = octets / 1024;
  let i = 0;
  while (valeur >= 1024 && i < unites.length - 1) {
    valeur /= 1024;
    i++;
  }
  return `${valeur.toLocaleString("fr-FR", { maximumFractionDigits: 1 })} ${unites[i]}`;
}
