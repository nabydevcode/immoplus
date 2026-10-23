import { useCallback, useEffect, useState } from "react";
import { apiClient } from "../../api/client";
import type { PagedResultDto } from "../../types/models";
import { messageErreur } from "./outils";

/** Taille de page par défaut de l'API (max 100, 200 pour la carte). */
export const TAILLE_PAGE = 20;

/** Taille maximale d'une page de l'API (listes d'appoint : jointures, menus déroulants). */
export const TAILLE_MAX = 100;

/** Ajoute `page` et `taille` à un chemin de l'API (qui peut déjà avoir des paramètres). */
export function avecPage(chemin: string, page: number, taille: number) {
  const separateur = chemin.includes("?") ? "&" : "?";
  return `${chemin}${separateur}page=${page}&taille=${taille}`;
}

/**
 * Réponse paginée de l'API. Une route pas encore paginée renvoie un simple tableau : il est alors découpé
 * ici (`decouper`) ou gardé entier, pour que l'écran fonctionne dans les deux cas.
 */
export function normaliserPage<T>(
  reponse: PagedResultDto<T> | T[] | null | undefined,
  page: number,
  taille: number,
  decouper = true,
): PagedResultDto<T> {
  if (Array.isArray(reponse)) {
    const total = reponse.length;
    if (!decouper) return { donnees: reponse, pageActuelle: 1, taillePage: total, totalElements: total, totalPages: 1 };
    return {
      donnees: reponse.slice((page - 1) * taille, page * taille),
      pageActuelle: page,
      taillePage: taille,
      totalElements: total,
      totalPages: Math.max(Math.ceil(total / taille), 1),
    };
  }
  if (!reponse) return { donnees: [], pageActuelle: page, taillePage: taille, totalElements: 0, totalPages: 1 };
  return { ...reponse, donnees: reponse.donnees ?? [], totalPages: Math.max(reponse.totalPages ?? 1, 1) };
}

/** Ajoute les éléments d'une page suivante sans doublon (un élément peut glisser d'une page à l'autre). */
function fusionner<T>(deja: T[], suivants: T[]) {
  const ids = new Set(deja.map((x) => (x as { id?: unknown }).id));
  return [
    ...deja,
    ...suivants.filter((x) => (x as { id?: unknown }).id === undefined || !ids.has((x as { id?: unknown }).id)),
  ];
}

interface EtatListe<T> {
  /** Chemin et taille qui ont produit la liste : une autre recherche repart de la page 1. */
  cle: string;
  version: number;
  elements: T[];
  /** Dernière page chargée. */
  page: number;
  total: number;
  totalPages: number;
  erreur: string | null;
  /** Réponse brute de la première page (champs hors liste : moyennes, compteurs…). */
  brut: unknown;
  /** La route a renvoyé un simple tableau (pas encore paginée) : la liste est complète. */
  nonPaginee: boolean;
}

/**
 * Liste chargée page par page avec un bouton « Voir plus » : les pages s'ajoutent les unes aux autres.
 * `total` vient de l'API (totalElements) : il ne dépend pas du nombre de pages chargées.
 * `recharger` relit la première page en gardant la liste affichée jusqu'à la réponse.
 */
export function useListeProgressive<T, R = unknown>(
  chemin: string | null,
  taille = TAILLE_PAGE,
  /** Lit la liste dans une réponse enveloppée (ex. avis avec leurs moyennes) ; à définir hors du composant. */
  extraire?: (reponse: R) => PagedResultDto<T> | T[],
) {
  const cle = chemin === null ? null : `${chemin}#${taille}`;
  // relecture : nombre de pages relues d'un coup par `recharger` (la liste dépliée reste dépliée).
  const [demande, setDemande] = useState<{ cle: string | null; page: number; version: number; relecture: number }>({
    cle,
    page: 1,
    version: 0,
    relecture: 1,
  });
  const page = demande.cle === cle ? demande.page : 1;
  const version = demande.cle === cle ? demande.version : 0;
  const relecture = demande.cle === cle ? demande.relecture : 1;
  const [etat, setEtat] = useState<EtatListe<T> | null>(null);

  useEffect(() => {
    if (chemin === null || cle === null) return;
    let annule = false;
    // Relecture après une action : les pages déjà affichées en une seule requête (100 éléments au plus).
    const pages = page === 1 && relecture > 1 && relecture * taille <= TAILLE_MAX ? relecture : 1;
    apiClient
      .get<unknown>(avecPage(chemin, page, taille * pages))
      .then((brut) => {
        if (annule) return;
        const reponse = extraire ? extraire(brut as R) : (brut as PagedResultDto<T> | T[]);
        const resultat = normaliserPage(reponse, page, taille, false);
        const totalPages = Array.isArray(reponse)
          ? 1
          : Math.max(Math.ceil(resultat.totalElements / taille), resultat.totalElements ? 1 : resultat.totalPages, 1);
        setEtat((e) => ({
          cle,
          version,
          elements: page === 1 || !e || e.cle !== cle ? resultat.donnees : fusionner(e.elements, resultat.donnees),
          page: Array.isArray(reponse) ? 1 : page === 1 ? Math.min(pages, totalPages) : page,
          total: resultat.totalElements,
          totalPages,
          erreur: null,
          brut: page === 1 || !e || e.cle !== cle ? brut : e.brut,
          nonPaginee: Array.isArray(reponse),
        }));
      })
      .catch((err) => {
        if (annule) return;
        const erreur = messageErreur(err, "Impossible de charger les données.");
        setEtat((e) =>
          e && e.cle === cle
            ? { ...e, version, erreur }
            : { cle, version, elements: [], page, total: 0, totalPages: 1, erreur, brut: null, nonPaginee: false },
        );
        // Page suivante en échec : le bouton « Voir plus » redevient utilisable.
        if (page > 1) setDemande((d) => (d.cle === cle && d.page === page ? { ...d, page: page - 1 } : d));
      });
    return () => {
      annule = true;
    };
  }, [chemin, cle, page, taille, version, relecture, extraire]);

  const actuel = etat && etat.cle === cle ? etat : null;
  const pagesAffichees = actuel?.page ?? 1;
  const chargerPlus = useCallback(
    () =>
      setDemande((d) => ({
        cle,
        page: pagesAffichees + 1,
        version: d.cle === cle ? d.version : 0,
        relecture: d.cle === cle ? d.relecture : 1,
      })),
    [cle, pagesAffichees],
  );
  const recharger = useCallback(
    () =>
      setDemande((d) => ({
        cle,
        page: 1,
        version: (d.cle === cle ? d.version : 0) + 1,
        relecture: pagesAffichees,
      })),
    [cle, pagesAffichees],
  );
  const setElements = useCallback(
    (maj: (elements: T[]) => T[]) => setEtat((e) => (e ? { ...e, elements: maj(e.elements) } : e)),
    [],
  );
  return {
    elements: actuel?.elements ?? [],
    /** Dernière liste reçue, même d'une recherche précédente (la carte la garde affichée pendant le chargement). */
    derniers: etat?.elements ?? null,
    total: actuel?.total ?? 0,
    brut: (actuel?.brut ?? null) as R | null,
    nonPaginee: actuel?.nonPaginee ?? false,
    /** Premier chargement (ou nouvelle recherche) : rien à afficher encore. */
    chargement: chemin !== null && actuel === null,
    /** Une page suivante est en cours de chargement. */
    chargementPlus: actuel !== null && page > 1 && actuel.page < page,
    erreur: actuel?.erreur ?? null,
    aPlus: actuel !== null && actuel.page < actuel.totalPages,
    chargerPlus,
    recharger,
    setElements,
  };
}

/**
 * Liste affichée une page à la fois (tableaux de l'administration) : Précédente / Suivante.
 * La page revient à 1 quand le chemin (filtre, recherche) change.
 */
export function useListePaginee<T>(
  chemin: string | null,
  taille = TAILLE_PAGE,
  /** Route non paginée (tableau simple) : découper ici en pages (sinon la liste entière est gardée). */
  decouper = true,
) {
  const [choix, setChoix] = useState<{ chemin: string | null; page: number }>({ chemin, page: 1 });
  const page = choix.chemin === chemin ? choix.page : 1;
  const [etat, setEtat] = useState<{
    cle: string;
    version: number;
    resultat: PagedResultDto<T> | null;
    erreur: string | null;
    nonPaginee: boolean;
  } | null>(null);
  const [version, setVersion] = useState(0);
  const cle = chemin === null ? null : avecPage(chemin, page, taille);

  useEffect(() => {
    if (cle === null) return;
    let annule = false;
    apiClient
      .get<PagedResultDto<T> | T[]>(cle)
      .then((reponse) => {
        if (!annule)
          setEtat({
            cle,
            version,
            resultat: normaliserPage(reponse, page, taille, decouper),
            erreur: null,
            nonPaginee: Array.isArray(reponse),
          });
      })
      .catch((err) => {
        if (!annule)
          setEtat((e) => ({
            cle,
            version,
            resultat: e?.cle === cle ? e.resultat : null,
            erreur: messageErreur(err, "Impossible de charger les données."),
            nonPaginee: e?.nonPaginee ?? false,
          }));
      });
    return () => {
      annule = true;
    };
  }, [cle, page, taille, version, decouper]);

  const setPage = useCallback((p: number) => setChoix({ chemin, page: Math.max(p, 1) }), [chemin]);
  const recharger = useCallback(() => setVersion((v) => v + 1), []);
  const setElements = useCallback(
    (maj: (elements: T[]) => T[]) =>
      setEtat((e) => (e?.resultat ? { ...e, resultat: { ...e.resultat, donnees: maj(e.resultat.donnees) } } : e)),
    [],
  );
  // Pendant le passage à une autre page, la page précédente reste affichée (pas de clignotement).
  const resultat = etat?.resultat ?? null;
  return {
    page,
    setPage,
    elements: resultat?.donnees ?? [],
    total: resultat?.totalElements ?? 0,
    totalPages: resultat?.totalPages ?? 1,
    chargement: chemin !== null && etat === null,
    /** Une autre page (ou une autre recherche) est en cours de chargement. */
    actualisation: etat !== null && (etat.cle !== cle || etat.version !== version),
    erreur: etat?.cle === cle ? etat.erreur : null,
    nonPaginee: etat?.nonPaginee ?? false,
    recharger,
    setElements,
  };
}

/** Valeur qui ne suit la saisie qu'après une pause (recherche envoyée à l'API sans requête à chaque touche). */
export function useValeurDifferee<T>(valeur: T, delaiMs = 350) {
  const [differee, setDifferee] = useState(valeur);
  useEffect(() => {
    const minuterie = window.setTimeout(() => setDifferee(valeur), delaiMs);
    return () => window.clearTimeout(minuterie);
  }, [valeur, delaiMs]);
  return differee;
}

/** Ajoute des paramètres (déjà encodés, « a=b&c=d ») à un chemin de l'API. */
export function avecParametres(chemin: string, ...parametres: (string | null | undefined | false)[]) {
  const ajout = parametres.filter(Boolean).join("&");
  if (!ajout) return chemin;
  return `${chemin}${chemin.includes("?") ? "&" : "?"}${ajout}`;
}

export interface OptionsListeAOnglets<T, F extends string> {
  /** Route de liste (ex. « reservation/lister »), null pour ne rien charger. */
  base: string | null;
  onglet: F;
  onglets: readonly F[];
  /** Paramètres envoyés à l'API pour un onglet (« statut=EnCours », « » pour tout) ; fonction définie hors du composant. */
  requeteOnglet: (onglet: F) => string;
  /** Recherche (n° de référence, nom, adresse) envoyée à l'API en `q` : elle porte sur tous les onglets. */
  recherche?: string;
  taille?: number;
  /** « progressif » : bouton « Voir plus » ; « pages » : Précédente / Suivante (tableaux d'administration). */
  mode: "progressif" | "pages";
  /**
   * Route pas encore paginée (tableau complet) : onglets, tri et recherche calculés ici comme auparavant.
   * Fonctions définies hors du composant.
   */
  local: {
    dansOnglet: (onglet: F, x: T) => boolean;
    trier?: (onglet: F, a: T, b: T) => number;
    rechercher?: (liste: T[], texte: string) => T[];
  };
}

/**
 * Liste à onglets paginée par l'API : seul l'onglet affiché est chargé (page par page), et le nombre d'éléments
 * de chaque onglet est demandé à part (taille=1, totalElements) : les compteurs ne dépendent pas des pages chargées.
 * Tant que la route renvoie un simple tableau, tout est calculé localement comme avant la pagination.
 */
export function useListeAOnglets<T, F extends string>(options: OptionsListeAOnglets<T, F>) {
  const { base, onglet, onglets, requeteOnglet, recherche = "", taille = TAILLE_PAGE, mode, local } = options;
  const q = recherche.trim();
  const chemin = base === null ? null : avecParametres(base, q ? `q=${encodeURIComponent(q)}` : requeteOnglet(onglet));
  const progressif = useListeProgressive<T>(mode === "progressif" ? chemin : null, taille);
  const pages = useListePaginee<T>(mode === "pages" ? chemin : null, taille, false);
  const liste = mode === "progressif" ? progressif : pages;

  // Compteurs des onglets : une petite requête par onglet (totalElements).
  const [compteurs, setCompteurs] = useState<{ cle: string; valeurs: Partial<Record<F, number>> } | null>(null);
  const [versionCompteurs, setVersionCompteurs] = useState(0);
  const cleOnglets = onglets.join(",");
  const cleCompteurs = base === null ? null : `${base}#${cleOnglets}#${versionCompteurs}`;
  const dansOnglet = local.dansOnglet;
  useEffect(() => {
    if (base === null || cleCompteurs === null) return;
    let annule = false;
    const liste = cleOnglets.split(",") as F[];
    Promise.all(
      liste.map((o) => apiClient.get<PagedResultDto<T> | T[]>(avecPage(avecParametres(base, requeteOnglet(o)), 1, 1))),
    )
      .then((reponses) => {
        if (annule) return;
        const valeurs: Partial<Record<F, number>> = {};
        // Réponse en tableau simple : compteurs calculés localement sur la liste complète.
        // Élément renvoyé hors de l'onglet : l'API ignore ce filtre, son total n'est pas celui de l'onglet.
        reponses.forEach((r, i) => {
          if (Array.isArray(r) || !r) return;
          const premier = r.donnees?.[0];
          if (premier !== undefined && !dansOnglet(liste[i], premier)) return;
          valeurs[liste[i]] = r.totalElements ?? 0;
        });
        setCompteurs({ cle: cleCompteurs, valeurs });
      })
      .catch(() => {
        // Compteurs facultatifs : la liste reste utilisable.
      });
    return () => {
      annule = true;
    };
  }, [base, cleCompteurs, cleOnglets, requeteOnglet, dansOnglet]);

  const recharger = useCallback(() => {
    liste.recharger();
    setVersionCompteurs((v) => v + 1);
  }, [liste]);

  // Mode local (route non paginée) : la liste reçue est complète.
  // Filet de sécurité si l'API ignore le paramètre d'onglet : on ne montre que les éléments de l'onglet.
  let elements = q ? liste.elements : liste.elements.filter((x) => local.dansOnglet(onglet, x));
  const valeurs: Partial<Record<F, number>> = { ...(compteurs?.valeurs ?? {}) };
  let total = liste.total;
  let totalPages = mode === "pages" ? pages.totalPages : 1;
  if (liste.nonPaginee) {
    const tous = liste.elements;
    for (const o of onglets) valeurs[o] = tous.filter((x) => local.dansOnglet(o, x)).length;
    const filtres = q ? (local.rechercher?.(tous, q) ?? tous) : tous.filter((x) => local.dansOnglet(onglet, x));
    if (!q && local.trier) filtres.sort((a, b) => local.trier!(onglet, a, b));
    total = filtres.length;
    totalPages = Math.max(Math.ceil(total / taille), 1);
    elements = mode === "pages" ? filtres.slice((pages.page - 1) * taille, pages.page * taille) : filtres;
  }

  return {
    elements,
    /** Nombre d'éléments de chaque onglet (absent tant qu'il n'est pas connu). */
    compteurs: valeurs,
    /** Les compteurs ont été reçus (un onglet sans compteur est alors un filtre que l'API ne connaît pas). */
    compteursCharges: compteurs !== null || liste.nonPaginee,
    /** Nombre d'éléments de la liste affichée (onglet ou recherche), toutes pages confondues. */
    total,
    chargement: liste.chargement,
    erreur: liste.erreur,
    /** Liste complète reçue d'une route non paginée. */
    nonPaginee: liste.nonPaginee,
    recharger,
    setElements: liste.setElements,
    // Mode « progressif »
    aPlus: mode === "progressif" && !liste.nonPaginee && progressif.aPlus,
    chargementPlus: progressif.chargementPlus,
    chargerPlus: progressif.chargerPlus,
    // Mode « pages »
    page: pages.page,
    setPage: pages.setPage,
    totalPages,
    actualisation: mode === "pages" ? pages.actualisation : false,
  };
}

/** Éléments d'une réponse de liste, paginée ou non. */
export const listeDe = <T>(reponse: PagedResultDto<T> | T[] | null | undefined): T[] =>
  Array.isArray(reponse) ? reponse : (reponse?.donnees ?? []);

/**
 * Texte de recherche envoyé à l'API en `q` : un numéro de référence saisi « n° 000012 », « #12 » ou « 000012 »
 * devient « 12 » ; un nom ou une adresse est envoyé tel quel.
 */
export function texteRecherche(saisie: string) {
  const texte = saisie.trim();
  const nettoye = texte
    .toLowerCase()
    .replace(/^(dossier|n°|no|#)\s*/, "")
    .replace(/^n°\s*/, "");
  return /^\d+$/.test(nettoye) ? String(Number(nettoye)) : texte;
}
