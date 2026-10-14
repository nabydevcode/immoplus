import { lazy, Suspense, useEffect, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { AlertCircle, List, Map as IconeCarte, SearchX, X } from "lucide-react";
import { apiClient } from "../api/client";
import { messageErreur } from "../components/admin/outils";
import type { AppartementDto } from "../types/models";
import CarteAnnonce from "../components/appartements/CarteAnnonce";
import BarreRecherche from "../components/sejour/BarreRecherche";
import BarreRechercheLongueDuree from "../components/appartements/BarreRechercheLongueDuree";
import { suffixeSejour } from "../components/sejour/parametres";
import { CLES_ZONE, zoneDepuisUrl, type Zone } from "../components/carte/position";

// Leaflet n'est téléchargé qu'au passage en vue carte.
const CarteAnnonces = lazy(() => import("../components/carte/CarteAnnonces"));

/** Critères de recherche transmis tels quels à l'API, dont la zone de la carte. */
const CRITERES_SEJOUR = ["ville", "quartier", "arrivee", "depart", "voyageurs", "equipements", "prixMax", ...CLES_ZONE];
const CRITERES_LONGUE_DUREE = [
  "ville",
  "quartier",
  "loyerMax",
  "chambresMin",
  "avanceMax",
  "equipements",
  ...CLES_ZONE,
];

function CarteSquelette() {
  return (
    <div className="bg-white rounded-2xl overflow-hidden shadow-sm border border-black/5 animate-pulse">
      <div className="bg-gray-200 h-40" />
      <div className="p-5 space-y-3">
        <div className="h-5 w-24 bg-gray-200 rounded-full" />
        <div className="h-4 w-3/4 bg-gray-200 rounded" />
        <div className="h-3 w-1/2 bg-gray-200 rounded" />
        <div className="h-9 w-full bg-gray-200 rounded-lg mt-4" />
      </div>
    </div>
  );
}

export default function Annonces() {
  const { type = "longue-duree" } = useParams<{ type: string }>();

  const estLongueDuree = type === "longue-duree";
  const titre = estLongueDuree ? "Location longue durée" : "Séjours courts";
  const typeOffreApi = estLongueDuree ? "LongueDuree" : "SejourCourt";
  const [parametres, setParametres] = useSearchParams();
  const criteres = new URLSearchParams();
  for (const cle of estLongueDuree ? CRITERES_LONGUE_DUREE : CRITERES_SEJOUR) {
    const valeur = parametres.get(cle);
    if (valeur) criteres.set(cle, valeur);
  }
  const requete = criteres.toString();
  const cleRecherche = `${typeOffreApi}?${requete}`;
  // Dernier résultat reçu, avec la recherche qui l'a produit : la liste précédente reste disponible
  // (la vue carte la garde affichée) pendant le chargement de la suivante.
  const [resultat, setResultat] = useState<{
    cle: string;
    appartements: AppartementDto[] | null;
    erreur: string | null;
  } | null>(null);
  const chargement = resultat?.cle !== cleRecherche;
  const appartements = resultat?.appartements ?? null;
  const erreur = chargement ? null : (resultat?.erreur ?? null);
  const enRecherche = requete !== "";
  const suffixe = estLongueDuree ? "" : suffixeSejour(parametres);
  const vue = parametres.get("vue") === "carte" ? "carte" : "liste";
  const zone = zoneDepuisUrl(parametres);

  /** Modifie l'URL en gardant la vue (liste/carte) choisie. */
  const naviguer = (suivants: URLSearchParams) => {
    if (vue === "carte") suivants.set("vue", "carte");
    else suivants.delete("vue");
    setParametres(suivants);
  };
  const changerVue = (v: "liste" | "carte") => {
    const suivants = new URLSearchParams(parametres);
    if (v === "carte") suivants.set("vue", "carte");
    else suivants.delete("vue");
    setParametres(suivants, { replace: true });
  };
  const rechercherZone = (z: Zone) => {
    const suivants = new URLSearchParams(parametres);
    for (const cle of CLES_ZONE) suivants.set(cle, String(z[cle]));
    setParametres(suivants);
  };
  /** Une nouvelle recherche garde la zone de la carte si elle était active. */
  const rechercher = (p: URLSearchParams) => {
    if (zone) for (const cle of CLES_ZONE) p.set(cle, String(zone[cle]));
    naviguer(p);
  };
  const retirerZone = () => {
    const suivants = new URLSearchParams(parametres);
    for (const cle of CLES_ZONE) suivants.delete(cle);
    setParametres(suivants);
  };

  useEffect(() => {
    let annule = false;
    apiClient
      // Les logements loués ou retirés par leur bailleur ne sont pas proposés aux locataires.
      .get<AppartementDto[]>(
        `appartement/lister?typeOffre=${typeOffreApi}&disponible=true${requete ? `&${requete}` : ""}`,
      )
      .then((data) => {
        if (!annule) setResultat({ cle: `${typeOffreApi}?${requete}`, appartements: data, erreur: null });
      })
      .catch((err) => {
        if (!annule)
          setResultat((r) => ({
            cle: `${typeOffreApi}?${requete}`,
            appartements: r?.appartements ?? null,
            erreur: messageErreur(err, "Impossible de charger les annonces pour le moment."),
          }));
      });

    return () => {
      annule = true;
    };
  }, [typeOffreApi, requete]);

  return (
    <div className="px-6 py-10">
      <div className="max-w-6xl mx-auto">
        <div className="inline-flex gap-1 mb-8 bg-white border border-black/5 rounded-xl p-1 shadow-sm">
          <Link
            to="/annonces/longue-duree"
            className={`px-4 py-2 rounded-lg text-sm font-semibold transition-colors ${
              estLongueDuree ? "bg-immo-dark text-white" : "text-gray-600 hover:text-immo-dark"
            }`}
          >
            Location longue durée
          </Link>
          <Link
            to="/annonces/sejour-court"
            className={`px-4 py-2 rounded-lg text-sm font-semibold transition-colors ${
              !estLongueDuree ? "bg-immo-dark text-white" : "text-gray-600 hover:text-immo-dark"
            }`}
          >
            Séjours courts
          </Link>
        </div>

        <h1 className="text-2xl md:text-3xl font-extrabold mb-1">{titre}</h1>
        <p className="text-gray-500 text-sm mb-8">
          {chargement
            ? "Recherche en cours..."
            : `${appartements?.length ?? 0} annonce(s) ${enRecherche ? "correspondant à votre recherche" : "disponible(s)"}`}
        </p>

        {estLongueDuree ? (
          <BarreRechercheLongueDuree key={requete} parametres={criteres} onRechercher={rechercher} />
        ) : (
          <BarreRecherche key={requete} parametres={criteres} onRechercher={rechercher} />
        )}

        <div className="flex flex-wrap items-center justify-between gap-3 mb-5">
          <div className="inline-flex gap-1 bg-white border border-black/5 rounded-xl p-1 shadow-sm" role="group">
            {(
              [
                { valeur: "liste", label: "Liste", icone: List },
                { valeur: "carte", label: "Carte", icone: IconeCarte },
              ] as const
            ).map(({ valeur, label, icone: Icone }) => (
              <button
                key={valeur}
                type="button"
                onClick={() => changerVue(valeur)}
                aria-pressed={vue === valeur}
                className={`inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm font-semibold transition-colors ${
                  vue === valeur ? "bg-immo-dark text-white" : "text-gray-600 hover:text-immo-dark"
                }`}
              >
                <Icone size={15} />
                {label}
              </button>
            ))}
          </div>
          {zone && (
            <span className="inline-flex items-center gap-1.5 bg-immo-gold/15 text-immo-dark text-xs font-semibold pl-3 pr-1.5 py-1.5 rounded-full">
              Zone de la carte uniquement
              <button
                type="button"
                onClick={retirerZone}
                aria-label="Retirer le filtre de zone"
                className="w-5 h-5 rounded-full hover:bg-black/10 flex items-center justify-center"
              >
                <X size={13} />
              </button>
            </span>
          )}
        </div>

        {/* En vue carte, la carte reste affichée pendant une nouvelle recherche (elle garde son cadrage). */}
        {chargement && !(vue === "carte" && appartements) ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {Array.from({ length: 3 }).map((_, i) => (
              <CarteSquelette key={i} />
            ))}
          </div>
        ) : erreur ? (
          <div className="flex flex-col items-center text-center gap-3 bg-white rounded-2xl border border-red-100 py-16 px-6">
            <AlertCircle className="text-red-500" size={32} />
            <p className="text-red-600 font-medium">{erreur}</p>
          </div>
        ) : vue === "carte" ? (
          <Suspense fallback={<div className="h-[60vh] min-h-80 rounded-2xl bg-gray-200 animate-pulse" />}>
            <CarteAnnonces
              appartements={appartements ?? []}
              lienFiche={(a) => `/annonces/${type}/${a.id}${suffixe}`}
              zone={zone}
              onRechercherZone={rechercherZone}
            />
          </Suspense>
        ) : appartements && appartements.length > 0 ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {appartements.map((a) => (
              <CarteAnnonce key={a.id} appartement={a} lien={`/annonces/${type}/${a.id}${suffixe}`} />
            ))}
          </div>
        ) : (
          <div className="flex flex-col items-center text-center gap-3 bg-white rounded-2xl border border-black/5 py-16 px-6">
            <SearchX className="text-gray-300" size={36} />
            <p className="text-gray-600 font-medium">Aucune annonce trouvée.</p>
            <p className="text-gray-400 text-sm">
              {enRecherche
                ? estLongueDuree
                  ? "Essayez un autre quartier, un loyer plus élevé ou moins de filtres."
                  : "Essayez d'autres dates, un autre quartier ou moins de filtres."
                : "Revenez bientôt, de nouvelles offres arrivent régulièrement."}
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
