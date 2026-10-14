import { Fragment, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { ChevronDown, ChevronRight } from "lucide-react";
import Loyers from "../../components/financement/Loyers";
import FinLocation from "../../components/financement/FinLocation";
import { ChampRecherche } from "../../components/candidatures/RechercheCandidature";
import { extraireNumero, filtrerRecherche } from "../../components/candidatures/recherche";
import { Badge, EnTete, EtatListe, Onglets, Tableau } from "../../components/admin/ui";
import { formaterDate, formaterReference, useChargement } from "../../components/admin/outils";
import BoutonPdf from "../../components/documents/BoutonPdf";
import { contratLocation } from "../../components/documents/pdf";
import type { LocationDto } from "../../types/models";

const statuts = {
  EnCours: { label: "En cours", ton: "succes" },
  Terminee: { label: "Terminée", ton: "neutre" },
  Resiliee: { label: "Résiliée", ton: "danger" },
} as const;

export default function Locations() {
  const { donnees, chargement, erreur, recharger } = useChargement<LocationDto[]>("location/lister");
  const [filtre, setFiltre] = useState<"EnCours" | "Toutes">("EnCours");
  const [params] = useSearchParams();
  const q = params.get("q") ?? "";
  const [ouverteChoisie, setOuverte] = useState<number | null | undefined>(undefined);

  const locations = donnees ?? [];
  const enCours = locations.filter((l) => l.statut === "EnCours");
  const [recherche, setRecherche] = useState(q);
  // Recherche (tous statuts) : par n° de location ou du dossier d'origine, nom du locataire ou adresse.
  const numero = extraireNumero(recherche);
  const resultats = filtrerRecherche(
    locations,
    recherche,
    (l) => [l.id, l.candidatureId],
    (l) => [l.locataireNom, l.appartementAdresse],
  );
  const affichees = resultats ?? (filtre === "EnCours" ? enCours : locations);
  // Lien direct (?q=référence, depuis le tableau de bord) : la page s'ouvre filtrée sur la référence,
  // et l'élément s'ouvre tout seul s'il est le seul résultat, tant que l'utilisateur n'a rien ouvert lui-même.
  const ouverte = ouverteChoisie !== undefined ? ouverteChoisie : q && resultats?.length === 1 ? resultats[0].id : null;

  return (
    <>
      <EnTete
        titre="Locations"
        description="Baux longue durée, créés automatiquement à l'acceptation d'une candidature."
      />
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (resultats?.length === 1) setOuverte(resultats[0].id);
        }}
        className="mb-6"
      >
        <ChampRecherche
          valeur={recherche}
          onChange={setRecherche}
          onEffacer={() => setRecherche("")}
          placeholder="N° de location (ex. 000023), nom du locataire ou adresse"
          libelle="Rechercher une location"
        />
      </form>
      {!resultats && (
        <Onglets
          valeur={filtre}
          onChange={setFiltre}
          options={[
            { valeur: "EnCours", label: `En cours (${enCours.length})` },
            { valeur: "Toutes", label: `Toutes (${locations.length})` },
          ]}
        />
      )}
      <EtatListe
        chargement={chargement}
        erreur={erreur}
        vide={affichees.length === 0}
        messageVide={
          resultats
            ? numero !== null
              ? `Aucune location n° ${formaterReference(numero)}.`
              : "Aucune location ne correspond à cette recherche."
            : "Aucune location."
        }
      >
        <Tableau entetes={["", "N°", "Locataire", "Appartement", "Apport", "Clés", "Début", "Fin", "Statut"]}>
          {affichees.map((l) => (
            <Fragment key={l.id}>
              <tr
                onClick={() => setOuverte(ouverte === l.id ? null : l.id)}
                className="border-b border-black/5 cursor-pointer hover:bg-immo-cream/50"
              >
                <td className="pl-5 py-3 text-gray-400 w-4">
                  {ouverte === l.id ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                </td>
                <td className="px-5 py-3 text-gray-500 whitespace-nowrap">n° {formaterReference(l.id)}</td>
                <td className="px-5 py-3 font-semibold whitespace-nowrap">{l.locataireNom}</td>
                <td className="px-5 py-3 text-gray-600">{l.appartementAdresse}</td>
                <td className="px-5 py-3 whitespace-nowrap">
                  {l.dateApportPaye ? (
                    <Badge ton="succes">Payé le {formaterDate(l.dateApportPaye)}</Badge>
                  ) : (
                    <Badge ton="attente">À payer</Badge>
                  )}
                </td>
                <td className="px-5 py-3 whitespace-nowrap">
                  {l.dateRemiseCles ? (
                    <Badge ton="succes">Remises le {formaterDate(l.dateRemiseCles)}</Badge>
                  ) : (
                    <Badge ton="neutre">Non remises</Badge>
                  )}
                </td>
                <td className="px-5 py-3 text-gray-600 whitespace-nowrap">{formaterDate(l.dateDebut)}</td>
                <td className="px-5 py-3 text-gray-600 whitespace-nowrap">{formaterDate(l.dateFin)}</td>
                <td className="px-5 py-3">
                  <Badge ton={statuts[l.statut].ton}>{statuts[l.statut].label}</Badge>
                  {l.statut === "EnCours" && l.dateFinPrevue && (
                    <p className="text-xs text-amber-700 mt-1 whitespace-nowrap">
                      Préavis : fin le {formaterDate(l.dateFinPrevue)}
                    </p>
                  )}
                </td>
              </tr>
              {ouverte === l.id && (
                <tr className="border-b border-black/5 bg-immo-cream/40">
                  <td colSpan={9} className="px-5 py-5">
                    <div className="mb-5">
                      <BoutonPdf fichier={contratLocation(l.id)} libelle="Contrat de location (PDF)" />
                    </div>
                    <p className="font-semibold mb-2">Fin de location (préavis de 3 mois)</p>
                    <div className="mb-6">
                      <FinLocation locationId={l.id} admin onChange={recharger} />
                    </div>
                    <p className="font-semibold mb-3">Loyers mensuels (sans frais)</p>
                    <Loyers locationId={l.id} gestion />
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
