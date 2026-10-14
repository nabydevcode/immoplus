import { useState } from "react";
import { Link } from "react-router-dom";
import { ChevronDown, ChevronRight, FileText, ListX, MapPin, ShieldCheck, UserCheck } from "lucide-react";
import { apiClient } from "../../api/client";
import { useAuth } from "../../context/auth";
import LocatairesAEvaluer from "../../components/avis/LocatairesAEvaluer";
import ExamenCandidature from "../../components/candidatures/ExamenCandidature";
import { BarreRecherche } from "../../components/candidatures/RechercheCandidature";
import { useRechercheCandidature } from "../../components/candidatures/recherche";
import { libellesSituation, referenceDossier, simulerFinancement } from "../../components/candidatures/analyse";
import {
  classeBoutonSecondaire,
  formaterDate,
  formaterMontant,
  messageErreur,
  useChargement,
} from "../../components/admin/outils";
import type { AppartementDto, CandidatureDto, PagedResultDto } from "../../types/models";
import { useLienConnexion } from "../../hooks/useLienConnexion";

const statuts = {
  EnAttente: { label: "À étudier", classe: "bg-amber-100 text-amber-700" },
  Acceptee: { label: "Acceptée", classe: "bg-green-100 text-green-700" },
  Refusee: { label: "Refusée", classe: "bg-red-100 text-red-700" },
  ListeAttente: { label: "Liste d'attente", classe: "bg-blue-100 text-blue-700" },
  Preselectionnee: { label: "Visite", classe: "bg-purple-100 text-purple-700" },
  Annulee: { label: "Annulée", classe: "bg-gray-100 text-gray-600" },
} as const;

type Filtre = "EnAttente" | "Preselectionnee" | "ListeAttente" | "Toutes";

export default function CandidaturesRecues() {
  const { estConnecte, roleUtilisateur, utilisateurId } = useAuth();
  const lienConnexion = useLienConnexion();
  const estBailleur = estConnecte && roleUtilisateur === "Bailleur";
  // L'API ne renvoie au bailleur que les candidatures portant sur ses propres appartements.
  const candidatures = useChargement<PagedResultDto<CandidatureDto>>(
    estBailleur ? "candidature/lister?page=1&taille=100" : null,
  );
  const appartements = useChargement<AppartementDto[]>(
    estBailleur ? `appartement/lister?bailleurId=${utilisateurId}` : null,
  );
  const [filtre, setFiltre] = useState<Filtre>("EnAttente");
  const [ouverte, setOuverte] = useState<number | null>(null);
  const [erreurAction, setErreurAction] = useState<string | null>(null);

  const toutes = candidatures.donnees?.donnees ?? [];
  const recherche = useRechercheCandidature(toutes);
  const [clotureEnCours, setClotureEnCours] = useState<number | null>(null);

  if (!estBailleur) {
    return (
      <div className="px-6 py-16 max-w-xl mx-auto text-center text-gray-600">
        <Link to={lienConnexion} className="text-immo-gold font-semibold">
          Connectez-vous
        </Link>{" "}
        avec un compte Bailleur pour voir les candidatures sur vos logements.
      </div>
    );
  }

  const aEtudier = toutes.filter((c) => c.statut === "EnAttente");
  const enListeAttente = toutes.filter((c) => c.statut === "ListeAttente");
  const enVisite = toutes.filter((c) => c.statut === "Preselectionnee");
  // Une recherche (par n° de dossier, nom ou adresse) porte sur tous les statuts.
  const affichees =
    recherche.resultats ??
    (filtre === "EnAttente"
      ? aEtudier
      : filtre === "Preselectionnee"
        ? enVisite
        : filtre === "ListeAttente"
          ? enListeAttente
          : toutes);
  const nbPreselectionnes = (appartementId: number) => enVisite.filter((c) => c.appartementId === appartementId).length;
  const mesAppartements = (appartements.donnees ?? []).filter((a) => a.bailleurId === utilisateurId);
  const appartementDe = (id: number) => appartements.donnees?.find((a) => a.id === id);

  // Regroupement par appartement pour comparer les candidats d'un même logement.
  const groupes = new Map<number, CandidatureDto[]>();
  for (const c of affichees) groupes.set(c.appartementId, [...(groupes.get(c.appartementId) ?? []), c]);

  // Une décision change aussi les autres dossiers du logement (liste d'attente) et l'état du logement : on recharge tout.
  const mettreAJour = () => {
    candidatures.recharger();
    appartements.recharger();
  };

  const cloreListeAttente = async (appartementId: number, nombre: number) => {
    if (
      !window.confirm(
        `Clore la liste d'attente ? Les ${nombre} candidat(s) en attente seront refusés et prévenus par email.`,
      )
    )
      return;
    setErreurAction(null);
    setClotureEnCours(appartementId);
    try {
      await apiClient.post(`appartement/${appartementId}/clore-liste-attente`);
      mettreAJour();
    } catch (err) {
      setErreurAction(messageErreur(err, "Impossible de clore la liste d'attente."));
    } finally {
      setClotureEnCours(null);
    }
  };

  return (
    <div className="px-6 py-10">
      <div className="max-w-6xl mx-auto">
        <h1 className="text-2xl md:text-3xl font-extrabold mb-1">Candidatures reçues</h1>
        <p className="text-gray-500 text-sm mb-6">
          Étudiez les dossiers déposés sur vos {mesAppartements.length} logement(s) et décidez de les accepter ou non.
        </p>

        <LocatairesAEvaluer />

        <BarreRecherche recherche={recherche} onTrouve={setOuverte} />

        <div
          className={`inline-flex gap-1 mb-6 bg-white border border-black/5 rounded-xl p-1 shadow-sm ${
            recherche.resultats ? "hidden" : ""
          }`}
        >
          {(
            [
              ["EnAttente", `À étudier (${aEtudier.length})`],
              ["Preselectionnee", `Visites (${enVisite.length})`],
              ["ListeAttente", `Liste d'attente (${enListeAttente.length})`],
              ["Toutes", `Toutes (${toutes.length})`],
            ] as const
          ).map(([valeur, label]) => (
            <button
              key={valeur}
              onClick={() => setFiltre(valeur)}
              className={`px-4 py-2 rounded-lg text-sm font-semibold transition-colors ${
                filtre === valeur ? "bg-immo-dark text-white" : "text-gray-600 hover:text-immo-dark"
              }`}
            >
              {label}
            </button>
          ))}
        </div>

        {erreurAction && <p className="text-red-600 text-sm mb-4">{erreurAction}</p>}

        {candidatures.chargement || appartements.chargement ? (
          <p className="text-gray-500 text-sm">Chargement...</p>
        ) : candidatures.erreur ? (
          <p className="text-red-600 text-sm">{candidatures.erreur}</p>
        ) : groupes.size === 0 ? (
          <div className="bg-white rounded-2xl shadow-sm border border-black/5 py-14 px-6 text-center text-gray-500">
            {recherche.resultats
              ? "Aucune candidature ne correspond à cette recherche."
              : filtre === "EnAttente"
                ? "Aucun dossier à étudier pour le moment."
                : filtre === "Preselectionnee"
                  ? "Aucun candidat retenu pour une visite. Ouvrez un dossier « À étudier » et cliquez sur « Retenir pour une visite »."
                  : filtre === "ListeAttente"
                    ? "Aucun candidat en liste d'attente."
                    : "Aucune candidature reçue sur vos logements."}
          </div>
        ) : (
          <div className="flex flex-col gap-6">
            {[...groupes.entries()].map(([appartementId, liste]) => {
              const appartement = appartementDe(appartementId);
              return (
                <section
                  key={appartementId}
                  className="bg-white rounded-2xl shadow-sm border border-black/5 overflow-hidden"
                >
                  <header className="px-6 py-4 border-b border-black/5 flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <h2 className="font-bold flex items-center gap-1.5">
                        <MapPin size={16} className="text-immo-gold shrink-0" />
                        {liste[0].appartementAdresse}
                      </h2>
                      {appartement && (
                        <p className="text-xs text-gray-500 mt-0.5">
                          Loyer {formaterMontant(appartement.loyerMensuel)} · avance de {appartement.dureeAvanceExigee}{" "}
                          mois (
                          {formaterMontant((appartement.loyerMensuel ?? 0) * (appartement.dureeAvanceExigee ?? 0))})
                        </p>
                      )}
                    </div>
                    <span className="text-xs font-semibold text-gray-600 bg-immo-dark/5 px-3 py-1.5 rounded-full">
                      {liste.length} candidat(s)
                    </span>
                  </header>
                  {(() => {
                    // État du logement et de sa liste d'attente (calculé sur toutes les candidatures, pas seulement l'onglet affiché).
                    const attente = toutes.filter(
                      (c) => c.appartementId === appartementId && c.statut === "ListeAttente",
                    );
                    if (attente.length === 0) return null;
                    const libere = !appartement?.enLocation;
                    return (
                      <div
                        className={`px-6 py-3 border-b border-black/5 text-sm flex flex-wrap items-center justify-between gap-3 ${
                          libere ? "bg-amber-50 text-amber-900" : "bg-blue-50 text-blue-900"
                        }`}
                      >
                        <span className="flex items-center gap-2">
                          <UserCheck size={16} />
                          {libere
                            ? `Logement libéré : choisissez un remplaçant parmi les ${attente.length} candidat(s) en liste d'attente.`
                            : `${attente.length} candidat(s) en liste d'attente si le candidat retenu se désiste.`}
                        </span>
                        <button
                          onClick={() => cloreListeAttente(appartementId, attente.length)}
                          disabled={clotureEnCours === appartementId}
                          className={classeBoutonSecondaire(true)}
                        >
                          <ListX size={14} />
                          Clore la liste d'attente
                        </button>
                      </div>
                    );
                  })()}

                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead>
                        <tr className="text-left text-gray-500 border-b border-black/5">
                          <th className="pl-6 py-3 w-4" />
                          <th className="px-4 py-3 font-medium">N°</th>
                          <th className="px-4 py-3 font-medium">Candidat</th>
                          <th className="px-4 py-3 font-medium">Apport</th>
                          <th className="px-4 py-3 font-medium">Situation</th>
                          <th className="px-4 py-3 font-medium">Garant</th>
                          <th className="px-4 py-3 font-medium">Pièces</th>
                          <th className="px-4 py-3 font-medium">Déposé le</th>
                          <th className="px-4 py-3 font-medium">Statut</th>
                        </tr>
                      </thead>
                      <tbody>
                        {liste.map((c) => {
                          const { couverture } = simulerFinancement(c, appartement, 0.03);
                          return (
                            <tr
                              key={c.id}
                              onClick={() => setOuverte(ouverte === c.id ? null : c.id)}
                              className={`border-b border-black/5 last:border-0 cursor-pointer ${
                                ouverte === c.id ? "bg-immo-gold/10" : "hover:bg-immo-cream/50"
                              }`}
                            >
                              <td className="pl-6 py-3 text-gray-400">
                                {ouverte === c.id ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                              </td>
                              <td className="px-4 py-3 text-gray-500 whitespace-nowrap">n° {referenceDossier(c.id)}</td>
                              <td className="px-4 py-3 font-semibold whitespace-nowrap">{c.locataireNom}</td>
                              <td className="px-4 py-3 whitespace-nowrap">
                                {formaterMontant(c.apportInitial)}
                                <span className="block text-xs text-gray-500">
                                  {Math.round(couverture * 100)} % de l'avance
                                </span>
                              </td>
                              <td className="px-4 py-3 whitespace-nowrap">
                                {libellesSituation[c.situationProfessionnelle]?.label ?? c.situationProfessionnelle}
                              </td>
                              <td className="px-4 py-3">
                                {c.garant ? (
                                  <ShieldCheck size={18} className="text-green-600" aria-label="Avec garant" />
                                ) : (
                                  <span className="text-gray-400">-</span>
                                )}
                              </td>
                              <td className="px-4 py-3 whitespace-nowrap">
                                <span className="inline-flex items-center gap-1 text-gray-600">
                                  <FileText size={14} />
                                  {c.documents.length}
                                </span>
                              </td>
                              <td className="px-4 py-3 text-gray-600 whitespace-nowrap">
                                {formaterDate(c.dateSoumission)}
                              </td>
                              <td className="px-4 py-3">
                                <span
                                  className={`text-xs font-medium px-2.5 py-1 rounded-full whitespace-nowrap ${statuts[c.statut].classe}`}
                                >
                                  {statuts[c.statut].label}
                                </span>
                                {c.statut === "Preselectionnee" &&
                                  (() => {
                                    const v = c.visites?.find((x) =>
                                      ["Proposee", "Confirmee", "Indisponible"].includes(x.statut),
                                    );
                                    const faite = c.visites?.some((x) => x.statut === "Effectuee");
                                    return (
                                      <span
                                        className={`block mt-1 text-xs ${v?.statut === "Indisponible" ? "text-red-600" : "text-purple-700"}`}
                                      >
                                        {faite
                                          ? "Visite faite : à décider"
                                          : v
                                            ? `${new Date(v.dateHeure).toLocaleDateString("fr-FR")} ${new Date(v.dateHeure).toLocaleTimeString("fr-FR", { hour: "2-digit", minute: "2-digit" })}${v.statut === "Indisponible" ? " · indisponible" : v.statut === "Confirmee" ? " · confirmée" : ""}`
                                            : "À reprogrammer"}
                                      </span>
                                    );
                                  })()}
                                {c.statut === "EnAttente" && c.demandesComplement?.some((d) => !d.complete) && (
                                  <span
                                    className={`block mt-1 text-xs ${c.demandesComplement.some((d) => d.depassee) ? "text-red-600" : "text-amber-700"}`}
                                  >
                                    {c.demandesComplement.some((d) => d.depassee)
                                      ? "Délai dépassé"
                                      : "Documents demandés"}
                                  </span>
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>

                  {/* Le détail s'affiche sous le tableau pour rester lisible sur mobile. */}
                  {liste.some((c) => c.id === ouverte) && (
                    <div className="border-t border-black/5 bg-immo-cream/40 p-4 sm:p-6">
                      <p className="font-bold mb-4">
                        Dossier de {liste.find((c) => c.id === ouverte)!.locataireNom} · n° {referenceDossier(ouverte!)}
                      </p>
                      <ExamenCandidature
                        nbPreselectionnes={nbPreselectionnes(appartementId)}
                        candidature={liste.find((c) => c.id === ouverte)!}
                        appartement={appartement}
                        onMiseAJour={mettreAJour}
                      />
                    </div>
                  )}
                </section>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
