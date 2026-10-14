import { useState } from "react";
import { apiClient } from "../../api/client";
import { messageErreur } from "../admin/outils";
import type { CandidatureDto } from "../../types/models";
import { referenceDossier } from "./analyse";

/** « 000048 », « 48 », « n° 000048 », « #48 », « dossier 48 » → 48 ; sinon null (recherche par texte). */
export function extraireNumero(texte: string): number | null {
  const nettoye = texte
    .trim()
    .toLowerCase()
    .replace(/^(dossier|n°|no|#)\s*/, "")
    .replace(/^n°\s*/, "");
  return /^\d+$/.test(nettoye) ? Number(nettoye) : null;
}

const normaliser = (s: string) => s.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();

/**
 * Recherche d'une candidature par numéro de dossier (référence), ou par nom du candidat / adresse.
 * Le numéro est d'abord cherché dans la liste chargée, puis via l'API (qui applique les droits d'accès).
 */
export function useRechercheCandidature(liste: CandidatureDto[], texteInitial = "") {
  const [texte, setTexte] = useState(texteInitial);
  const [trouveeApi, setTrouveeApi] = useState<CandidatureDto | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);

  const numero = extraireNumero(texte);
  const active = texte.trim() !== "";
  const resultats: CandidatureDto[] | null = !active
    ? null
    : numero !== null
      ? liste.some((c) => c.id === numero)
        ? liste.filter((c) => c.id === numero)
        : trouveeApi?.id === numero
          ? [trouveeApi]
          : []
      : liste.filter(
          (c) =>
            normaliser(c.locataireNom).includes(normaliser(texte.trim())) ||
            normaliser(c.appartementAdresse).includes(normaliser(texte.trim())),
        );

  /** Validation (Entrée) : un numéro absent de la liste chargée est cherché dans l'API. Renvoie l'id trouvé. */
  const chercher = async (): Promise<number | null> => {
    setMessage(null);
    if (numero === null) return resultats?.length === 1 ? resultats[0].id : null;
    if (liste.some((c) => c.id === numero)) return numero;
    setEnCours(true);
    try {
      setTrouveeApi(await apiClient.get<CandidatureDto>(`candidature/${numero}`));
      return numero;
    } catch (err) {
      const msg = messageErreur(err, "");
      setMessage(
        msg.includes("accès")
          ? `Le dossier n° ${referenceDossier(numero)} ne concerne pas vos logements.`
          : `Aucun dossier n° ${referenceDossier(numero)}.`,
      );
      return null;
    } finally {
      setEnCours(false);
    }
  };

  const effacer = () => {
    setTexte("");
    setMessage(null);
  };

  return { texte, setTexte, resultats, chercher, effacer, message, enCours, numero };
}

/**
 * Filtre une liste d'après une saisie : un numéro (« 000023 », « 23 », « n° 23 ») est comparé aux références
 * de chaque élément ; un texte est cherché (sans accents ni majuscules) dans ses champs texte.
 * Renvoie null si la saisie est vide (pas de recherche en cours).
 */
export function filtrerRecherche<T>(
  liste: T[],
  saisie: string,
  references: (element: T) => (number | null | undefined)[],
  textes: (element: T) => string[],
): T[] | null {
  const texte = saisie.trim();
  if (!texte) return null;
  const numero = extraireNumero(texte);
  if (numero !== null) return liste.filter((e) => references(e).includes(numero));
  const cherche = normaliser(texte);
  return liste.filter((e) => textes(e).some((t) => normaliser(t).includes(cherche)));
}
