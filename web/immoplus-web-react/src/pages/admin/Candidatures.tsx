import { Fragment, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { ChevronDown, ChevronRight } from "lucide-react";
import ExamenCandidature from "../../components/candidatures/ExamenCandidature";
import { BarreRecherche } from "../../components/candidatures/RechercheCandidature";
import { useRechercheCandidature } from "../../components/candidatures/recherche";
import { referenceDossier } from "../../components/candidatures/analyse";
import { Badge, EnTete, EtatListe, Onglets, Tableau } from "../../components/admin/ui";
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

export default function Candidatures() {
  const { donnees, chargement, erreur, recharger } = useChargement<PagedResultDto<CandidatureDto>>(
    "candidature/lister?page=1&taille=100",
  );
  const { donnees: appartements, recharger: rechargerAppartements } =
    useChargement<AppartementDto[]>("appartement/lister");
  const [filtre, setFiltre] = useState<"EnAttente" | "Toutes">("EnAttente");
  const [params] = useSearchParams();
  const q = params.get("q") ?? "";
  const [ouverteChoisie, setOuverte] = useState<number | null | undefined>(undefined);

  const candidatures = donnees?.donnees ?? [];
  const enAttente = candidatures.filter((c) => c.statut === "EnAttente");
  const recherche = useRechercheCandidature(candidatures, q);
  // Lien direct (?q=référence, depuis le tableau de bord) : la page s'ouvre filtrée sur la référence,
  // et l'élément s'ouvre tout seul s'il est le seul résultat, tant que l'utilisateur n'a rien ouvert lui-même.
  const ouverte =
    ouverteChoisie !== undefined
      ? ouverteChoisie
      : q && recherche.resultats?.length === 1
        ? recherche.resultats[0].id
        : null;
  // Une recherche (par n° de dossier, nom ou adresse) porte sur tous les statuts.
  const affichees = recherche.resultats ?? (filtre === "EnAttente" ? enAttente : candidatures);

  // Une décision change aussi les autres dossiers du logement (liste d'attente) et l'état du logement : on recharge.
  const mettreAJour = () => {
    recharger();
    rechargerAppartements();
  };

  return (
    <>
      <EnTete titre="Candidatures" description="Dossiers de location longue durée déposés par les locataires." />
      <BarreRecherche recherche={recherche} onTrouve={setOuverte} />
      {!recherche.resultats && (
        <Onglets
          valeur={filtre}
          onChange={setFiltre}
          options={[
            { valeur: "EnAttente", label: `En attente (${enAttente.length})` },
            { valeur: "Toutes", label: `Toutes (${candidatures.length})` },
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
                    <ExamenCandidature
                      nbPreselectionnes={
                        candidatures.filter(
                          (x) => x.appartementId === c.appartementId && x.statut === "Preselectionnee",
                        ).length
                      }
                      candidature={c}
                      appartement={appartements?.find((a) => a.id === c.appartementId)}
                      onMiseAJour={mettreAJour}
                    />
                  </td>
                </tr>
              )}
            </Fragment>
          ))}
        </Tableau>
      </EtatListe>
    </>
  );
}
