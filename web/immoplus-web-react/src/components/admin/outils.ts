import { useCallback, useEffect, useState } from "react";
import { apiClient } from "../../api/client";
import { formaterJour } from "../sejour/dates";

export function formaterMontant(montant: number | null | undefined) {
  // Le franc guinéen n'a pas de subdivision : jamais de décimales.
  return montant == null ? "-" : `${montant.toLocaleString("fr-FR", { maximumFractionDigits: 0 })} GNF`;
}

/** Montant abrégé, sans devise : 450 000 → « 450 k », 1 200 000 → « 1,2 M ». */
export function formaterMontantCompact(montant: number | null | undefined) {
  if (montant == null) return "-";
  if (Math.abs(montant) >= 1_000_000)
    return `${(montant / 1_000_000).toLocaleString("fr-FR", { maximumFractionDigits: 1 })} M`;
  if (Math.abs(montant) >= 1_000) return `${Math.round(montant / 1_000).toLocaleString("fr-FR")} k`;
  return montant.toLocaleString("fr-FR", { maximumFractionDigits: 0 });
}

/** Référence d'un dossier ou d'une location : son numéro sur 6 chiffres (23 → « 000023 »). */
export const formaterReference = (id: number) => String(id).padStart(6, "0");

/**
 * Date de l'API au format JJ/MM/AAAA.
 * - Date « jour » (AAAA-MM-JJ, ou minuit sans fuseau : échéance, arrivée…) : affichée telle quelle, sans décalage.
 * - Horodatage (dépôt, paiement…) : l'API l'enregistre en UTC sans le préciser ; il est converti au fuseau du navigateur.
 */
export function formaterDate(date: string | null | undefined) {
  if (!date) return "-";
  if (/^\d{4}-\d{2}-\d{2}(T00:00(:00(\.0+)?)?)?$/.test(date)) return formaterJour(date);
  const d = new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(date) ? date : `${date}Z`);
  return Number.isNaN(d.getTime()) ? "-" : d.toLocaleDateString("fr-FR");
}

/** Horodatage de l'API (UTC) au format JJ/MM/AAAA HH:MM, dans le fuseau du navigateur. */
export function formaterDateHeure(date: string | null | undefined) {
  if (!date) return "-";
  const d = new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(date) ? date : `${date}Z`);
  return Number.isNaN(d.getTime())
    ? "-"
    : d.toLocaleString("fr-FR", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      });
}

export function messageErreur(err: unknown, defaut = "Une erreur est survenue.") {
  if (!(err instanceof Error)) return defaut;
  if (err.message === "Erreur 401") return "Votre session a expiré. Reconnectez-vous.";
  if (err.message === "Erreur 403") return "Vous n'avez pas accès à cette ressource ou à cette action.";
  if (err.message === "Erreur 404") return "Élément introuvable : il a peut-être été supprimé.";
  if (/^Erreur 5\d\d$/.test(err.message)) return "Le serveur a rencontré un problème. Réessayez dans un instant.";
  if (err instanceof TypeError) return "Connexion au serveur impossible. Vérifiez votre connexion internet.";
  return err.message;
}

/**
 * Charge une ressource de l'API ; `setDonnees` permet de la mettre à jour localement après une action.
 * Avec `chemin` à null, rien n'est chargé (utile quand la ressource n'a pas lieu d'être).
 */
export function useChargement<T>(chemin: string | null) {
  const [donnees, setDonnees] = useState<T | null>(null);
  const [chargement, setChargement] = useState(chemin !== null);
  const [erreur, setErreur] = useState<string | null>(null);
  const [version, setVersion] = useState(0);

  useEffect(() => {
    if (chemin === null) return;
    let annule = false;
    apiClient
      .get<T>(chemin)
      .then((data) => {
        if (!annule) {
          setDonnees(data);
          setErreur(null);
        }
      })
      .catch((err) => {
        if (!annule) setErreur(messageErreur(err, "Impossible de charger les données."));
      })
      .finally(() => {
        if (!annule) setChargement(false);
      });
    return () => {
      annule = true;
    };
  }, [chemin, version]);

  const recharger = useCallback(() => setVersion((v) => v + 1), []);
  return { donnees, setDonnees, chargement, erreur, recharger };
}

/** Les énumérations de l'API peuvent arriver en texte ou en nombre (index de l'énumération). */
export function valeurEnum<T extends string>(valeur: T | number, liste: { valeur: T }[]): T | undefined {
  return typeof valeur === "number" ? liste[valeur]?.valeur : liste.find((x) => x.valeur === valeur)?.valeur;
}

export const MODES_PAIEMENT = ["Espèces", "Orange Money", "MTN Mobile Money", "Virement", "Chèque"];

const tailleBouton = (petit?: boolean) => (petit ? "text-xs px-3 py-1.5" : "text-sm px-4 py-2");
const baseBouton =
  "inline-flex items-center justify-center gap-1.5 font-semibold rounded-lg transition-colors disabled:opacity-60";

export const classeBoutonPrincipal = (petit?: boolean) =>
  `${baseBouton} ${tailleBouton(petit)} bg-immo-gold text-immo-dark hover:bg-immo-gold-light`;
export const classeBoutonSecondaire = (petit?: boolean) =>
  `${baseBouton} ${tailleBouton(petit)} border border-gray-300 text-gray-700 hover:bg-gray-50`;
export const classeBoutonDanger = (petit?: boolean) =>
  `${baseBouton} ${tailleBouton(petit)} border border-red-300 text-red-600 hover:bg-red-50`;
export const classeChamp =
  "w-full border border-gray-300 rounded-lg px-3 py-2 mt-1 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-immo-gold/40 focus:border-immo-gold";
