import { Fragment, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { ChevronDown, ChevronRight } from "lucide-react";
import ExamenCandidature from "../../components/candidatures/ExamenCandidature";
import { BarreRecherche } from "../../components/candidatures/RechercheCandidature";
import { useRechercheCandidature } from "../../components/candidatures/recherche";
import { referenceDossier } from "../../components/candidatures/analyse";
import { Badge, EnTete, EtatListe, Onglets, Pagination, Tableau } from "../../components/admin/ui";
import {
  avecPage,
  listeDe,
  TAILLE_MAX,
  texteRecherche,
  useListeAOnglets,
  useValeurDifferee,
} from "../../components/admin/pagination";
import { formaterDate, formaterMontant, useChargement } from "../../components/admin/outils";
import type { AppartementDto, CandidatureDto, PagedResultDto } from "../../types/models";

const statuts = {
  EnAttente: { label: "En attente", ton: "attente" },
  Acceptee: { label: "Acceptée", ton: "succes" },
  Refusee: { label: "Refusée", ton: "danger" },
  ListeAttente: { label: "Liste d'attente", ton: "info" },
  Preselectionnee: { label: "Visite", ton: "info" },
  Annulee: { label: "Annulée", ton: "neutre" },
} as const;

type Filtre = "EnAttente" | "Toutes";
const ONGLETS = ["EnAttente", "Toutes"] as const;
/** Onglet → paramètre de l'API (candidature/lister?statut=…). */
const requeteOnglet = (f: Filtre) => (f === "EnAttente" ? "statut=EnAttente" : "");
const LOCAL = {
  dansOnglet: (f: Filtre, c: CandidatureDto) => f === "Toutes" || c.statut === "EnAttente",
};

/**
 * Dossier déplié : le logement (loyer, avance, location en cours) et le nombre de candidats retenus pour une visite
 * sont lus pour ce seul logement, quelle que soit la page affichée.
 */
function DetailCandidature({ candidature, onMiseAJour }: { candidature: CandidatureDto; onMiseAJour: () => void }) {
  const appartement = useChargement<AppartementDto>(`appartement/${candidature.appartementId}`);
  const visites = useChargement<PagedResultDto<CandidatureDto> | CandidatureDto[]>(
    avecPage(`candidature/lister?appartementId=${candidature.appartementId}&statut=Preselectionnee`, 1, TAILLE_MAX),
  );
  const mettreAJour = () => {
    appartement.recharger();
    visites.recharger();
    onMiseAJour();
  };
  return (
    <ExamenCandidature
      nbPreselectionnes={
        listeDe(visites.donnees).filter(
          (x) => x.appartementId === candidature.appartementId && x.statut === "Preselectionnee",
        ).length
      }
      candidature={candidature}
      appartement={appartement.donnees ?? undefined}
      onMiseAJour={mettreAJour}
    />
  );
}

export default function Candidatures() {
  const [filtre, setFiltre] = useState<Filtre>("EnAttente");
  const [params] = useSearchParams();
  const q = params.get("q") ?? "";
  const [ouverteChoisie, setOuverte] = useState<number | null | undefined>(undefined);
  const [saisie, setSaisie] = useState(q);
  // Recherche côté serveur (q) après une pause de saisie ; un n° absent de la page est aussi cherché directement.
  const rechercheEnvoyee = useValeurDifferee(texteRecherche(saisie));
  const liste = useListeAOnglets<CandidatureDto, Filtre>({
    base: "candidature/lister",
    onglet: filtre,
    onglets: ONGLETS,
    requeteOnglet,
    recherche: rechercheEnvoyee,
    mode: "pages",
    local: LOCAL,
  });
  const { chargement, erreur, recharger, compteurs } = liste;
  const recherche = useRechercheCandidature(liste.elements, q);
  if (recherche.texte !== saisie) setSaisie(recherche.texte);
  const nombre = (f: Filtre) => (compteurs[f] !== undefined ? ` (${compteurs[f]})` : "");
  // Lien direct (?q=référence, depuis le tableau de bord) : la page s'ouvre filtrée sur la référence,
  // et l'élément s'ouvre tout seul s'il est le seul résultat, tant que l'utilisateur n'a rien ouvert lui-même.
  const ouverte =
    ouverteChoisie !== undefined
      ? ouverteChoisie
      : q && recherche.resultats?.length === 1
        ? recherche.resultats[0].id
        : null;
  // Une recherche (par n° de dossier, nom ou adresse) porte sur tous les statuts.
  const affichees = recherche.resultats ?? liste.elements;

  // Une décision change aussi les autres dossiers du logement (liste d'attente) et l'état du logement : on recharge.
  const mettreAJour = recharger;

  return (
    <>
      <EnTete titre="Candidatures" description="Dossiers de location longue durée déposés par les locataires." />
      <BarreRecherche recherche={recherche} onTrouve={setOuverte} />
      {!recherche.resultats && (
        <Onglets
          valeur={filtre}
          onChange={setFiltre}
          options={[
            { valeur: "EnAttente", label: `En attente${nombre("EnAttente")}` },
            { valeur: "Toutes", label: `Toutes${nombre("Toutes")}` },
          ]}
        />
      )}
      <EtatListe
        chargement={chargement}
        erreur={erreur}
        vide={affichees.length === 0}
        messageVide={
          recherche.resultats
            ? "Aucune candidature ne correspond à cette recherche."
            : filtre === "EnAttente"
              ? "Aucune candidature en attente."
              : "Aucune candidature."
        }
      >
        <Tableau entetes={["", "N°", "Locataire", "Appartement", "Apport", "Situation", "Déposée le", "Statut"]}>
          {affichees.map((c) => (
            <Fragment key={c.id}>
              <tr
                onClick={() => setOuverte(ouverte === c.id ? null : c.id)}
                className="border-b border-black/5 cursor-pointer hover:bg-immo-cream/50"
              >
                <td className="pl-5 py-3 text-gray-400 w-4">
                  {ouverte === c.id ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                </td>
                <td className="px-5 py-3 text-gray-500 whitespace-nowrap">n° {referenceDossier(c.id)}</td>
                <td className="px-5 py-3 font-semibold whitespace-nowrap">{c.locataireNom}</td>
                <td className="px-5 py-3 text-gray-600">{c.appartementAdresse}</td>
                <td className="px-5 py-3 whitespace-nowrap">{formaterMontant(c.apportInitial)}</td>
                <td className="px-5 py-3 text-gray-600">{c.situationProfessionnelle}</td>
                <td className="px-5 py-3 text-gray-600 whitespace-nowrap">{formaterDate(c.dateSoumission)}</td>
                <td className="px-5 py-3">
                  <Badge ton={statuts[c.statut].ton}>{statuts[c.statut].label}</Badge>
                </td>
              </tr>
              {ouverte === c.id && (
                <tr className="border-b border-black/5 bg-immo-cream/40">
                  <td colSpan={8} className="px-5 py-5">
                    <DetailCandidature candidature={c} onMiseAJour={mettreAJour} />
                  </td>
                </tr>
              )}
            </Fragment>
          ))}
        </Tableau>
        <Pagination page={liste.page} totalPages={liste.totalPages} onChange={liste.setPage} total={liste.total} />
      </EtatListe>
    </>
  );
}
