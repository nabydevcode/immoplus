import { Fragment, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { ChevronDown, ChevronRight } from "lucide-react";
import Echeancier from "../../components/admin/Echeancier";
import { ChampRecherche } from "../../components/candidatures/RechercheCandidature";
import { filtrerRecherche } from "../../components/candidatures/recherche";
import { Badge, EnTete, EtatListe, Pagination, Tableau } from "../../components/admin/ui";
import { formaterMontant, formaterReference } from "../../components/admin/outils";
import { texteRecherche, useListeAOnglets, useValeurDifferee } from "../../components/admin/pagination";
import type { FinancementDto } from "../../types/models";

const statutsFinancement = {
  EnAttenteValidation: { label: "Sans échéancier", ton: "attente" },
  EnCours: { label: "En cours", ton: "info" },
  Solde: { label: "Soldé", ton: "succes" },
} as const;

const ONGLETS = ["Tous"] as const;
const requeteOnglet = () => "";
const LOCAL = {
  dansOnglet: () => true,
  // Un numéro retrouve le financement, sa location ou le dossier d'origine ; un texte, le locataire ou l'adresse.
  rechercher: (liste: FinancementDto[], texte: string) =>
    filtrerRecherche(
      liste,
      texte,
      (f) => [f.id, f.locationId, f.candidatureId],
      (f) => [f.locataireNom, f.appartementAdresse],
    ) ?? liste,
};

export default function Financements() {
  const [params] = useSearchParams();
  const q = params.get("q") ?? "";
  const [ouvertChoisi, setOuvert] = useState<number | null | undefined>(undefined);
  const [recherche, setRecherche] = useState(q);
  // Recherche côté serveur (q), après une courte pause de saisie.
  const rechercheEnvoyee = useValeurDifferee(texteRecherche(recherche));
  const liste = useListeAOnglets<FinancementDto, "Tous">({
    base: "financement/lister",
    onglet: "Tous",
    onglets: ONGLETS,
    requeteOnglet,
    recherche: rechercheEnvoyee,
    mode: "pages",
    local: LOCAL,
  });
  const { chargement, erreur, recharger } = liste;
  const affiches = liste.elements;
  const resultats = rechercheEnvoyee ? affiches : null;
  // Lien direct (?q=référence, depuis le tableau de bord) : la page s'ouvre filtrée sur la référence,
  // et l'élément s'ouvre tout seul s'il est le seul résultat, tant que l'utilisateur n'a rien ouvert lui-même.
  const ouvert = ouvertChoisi !== undefined ? ouvertChoisi : q && resultats?.length === 1 ? resultats[0].id : null;

  return (
    <>
      <EnTete
        titre="Financements"
        description="Avances de loyer remboursées en mensualités. Cliquez sur une ligne pour voir l'échéancier."
      />
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (resultats?.length === 1) setOuvert(resultats[0].id);
        }}
        className="mb-6"
      >
        <ChampRecherche
          valeur={recherche}
          onChange={setRecherche}
          onEffacer={() => setRecherche("")}
          placeholder="N° de financement, de location ou de dossier, nom du locataire, adresse"
          libelle="Rechercher un financement"
        />
      </form>
      <EtatListe
        chargement={chargement}
        erreur={erreur}
        vide={affiches.length === 0}
        messageVide={resultats ? "Aucun financement ne correspond à cette recherche." : "Aucun financement."}
      >
        <Tableau
          entetes={["", "N°", "Locataire", "Références", "Mensualités", "À rembourser", "Statut", "Ponctualité"]}
        >
          {affiches.map((f) => (
            <Fragment key={f.id}>
              <tr
                onClick={() => setOuvert(ouvert === f.id ? null : f.id)}
                className="border-b border-black/5 cursor-pointer hover:bg-immo-cream/50"
              >
                <td className="pl-5 py-3 text-gray-400 w-4">
                  {ouvert === f.id ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                </td>
                <td className="px-5 py-3 text-gray-500 whitespace-nowrap">n° {formaterReference(f.id)}</td>
                <td className="px-5 py-3">
                  <p className="font-semibold whitespace-nowrap">{f.locataireNom}</p>
                  <p className="text-xs text-gray-500">{f.appartementAdresse}</p>
                </td>
                <td className="px-5 py-3 text-xs text-gray-600 whitespace-nowrap">
                  {f.candidatureId && <p>Dossier n° {formaterReference(f.candidatureId)}</p>}
                  <p>Location n° {formaterReference(f.locationId)}</p>
                </td>
                <td className="px-5 py-3 whitespace-nowrap">
                  {f.nombreMensualites} × {formaterMontant(f.montantMensualite)}
                </td>
                <td className="px-5 py-3 whitespace-nowrap">{formaterMontant(f.montantARembourser)}</td>
                <td className="px-5 py-3">
                  <Badge ton={statutsFinancement[f.statut].ton}>{statutsFinancement[f.statut].label}</Badge>
                </td>
                <td className="px-5 py-3">
                  {f.mensualitesEnRetard > 0 ? (
                    <Badge ton="danger">{f.mensualitesEnRetard} en retard</Badge>
                  ) : f.mensualitesPayeesEnRetard > 0 ? (
                    <Badge ton="attente">
                      {f.mensualitesPayeesEnRetard} payée{f.mensualitesPayeesEnRetard > 1 ? "s" : ""} en retard
                    </Badge>
                  ) : f.mensualitesPayeesATemps > 0 ? (
                    <Badge ton="succes">À jour</Badge>
                  ) : (
                    <span className="text-gray-400">-</span>
                  )}
                </td>
              </tr>
              {ouvert === f.id && (
                <tr className="border-b border-black/5 bg-immo-cream/40">
                  <td colSpan={8} className="px-5 py-5">
                    <Echeancier financement={f} onModifie={recharger} />
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
