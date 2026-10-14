import { Fragment, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Ban, Banknote, FileText, HandCoins, MessageCircle, MessageSquareReply } from "lucide-react";
import FormulairePaiement from "../../components/admin/FormulairePaiement";
import FormulaireRemboursement from "../../components/admin/FormulaireRemboursement";
import { ChampRecherche } from "../../components/candidatures/RechercheCandidature";
import { filtrerRecherche } from "../../components/candidatures/recherche";
import { Badge, EnTete, EtatListe, Onglets, Tableau } from "../../components/admin/ui";
import {
  classeBoutonDanger,
  classeBoutonSecondaire,
  formaterDate,
  formaterMontant,
  formaterReference,
  useChargement,
} from "../../components/admin/outils";
import type { ReservationDto } from "../../types/models";
import ReponseDemande from "../../components/sejour/ReponseDemande";
import {
  echeancePaiement,
  estActive,
  limiteReponse,
  nombreNuits,
  resteARembourser,
  statutsReservation,
} from "../../components/sejour/reservations";
import AnnulationReservation from "../../components/sejour/AnnulationReservation";
import InfoAnnulation from "../../components/sejour/InfoAnnulation";
import FilMessages from "../../components/messages/FilMessages";
import HistoriquePaiements from "../../components/paiement/HistoriquePaiements";
import BoutonPdf from "../../components/documents/BoutonPdf";
import { etatDesLieuxPdf } from "../../components/documents/pdf";

type Volet = "paiement" | "reponse" | "messages" | "remboursement" | "annulation" | "documents";

export default function Reservations() {
  const { donnees, chargement, erreur, recharger } = useChargement<ReservationDto[]>("reservation/lister");
  const [filtre, setFiltre] = useState<"Demandes" | "Actives" | "ARembourser" | "Toutes">("Actives");
  // Ligne dépliée : paiement, réponse à une demande, messages, remboursement ou annulation.
  const [ouvert, setOuvert] = useState<{ id: number; volet: Volet } | null>(null);
  const basculer = (id: number, volet: Volet) =>
    setOuvert(ouvert?.id === id && ouvert.volet === volet ? null : { id, volet });

  const reservations = donnees ?? [];
  const actives = reservations.filter(estActive);
  const demandes = reservations.filter((r) => r.statut === "DemandeEnvoyee");
  const aRembourser = reservations.filter((r) => r.statut === "Annulee" && resteARembourser(r) > 0);
  // Lien direct (?q=référence, depuis le tableau de bord) : la page s'ouvre filtrée sur la réservation.
  const [params] = useSearchParams();
  const [recherche, setRecherche] = useState(params.get("q") ?? "");
  // Recherche (tous statuts) : par n° de réservation, nom du locataire ou adresse.
  const resultats = filtrerRecherche(
    reservations,
    recherche,
    (r) => [r.id],
    (r) => [r.locataireNom, r.appartementAdresse],
  );
  const affichees =
    resultats ??
    (filtre === "Demandes"
      ? demandes
      : filtre === "Actives"
        ? actives
        : filtre === "ARembourser"
          ? aRembourser
          : reservations);
  const fermerEtRecharger = () => {
    setOuvert(null);
    recharger();
  };

  return (
    <>
      <EnTete
        titre="Réservations"
        description="Séjours courts : demandes à valider (si l'hôte ne répond pas), paiements et annulations."
      />
      <form onSubmit={(e) => e.preventDefault()} className="mb-6">
        <ChampRecherche
          valeur={recherche}
          onChange={setRecherche}
          onEffacer={() => setRecherche("")}
          placeholder="N° de réservation (ex. 000012), nom du locataire ou adresse"
          libelle="Rechercher une réservation"
        />
      </form>
      {!resultats && (
        <Onglets
          valeur={filtre}
          onChange={setFiltre}
          options={[
            { valeur: "Demandes", label: `Demandes (${demandes.length})` },
            { valeur: "Actives", label: `Actives (${actives.length})` },
            { valeur: "ARembourser", label: `À rembourser (${aRembourser.length})` },
            { valeur: "Toutes", label: `Toutes (${reservations.length})` },
          ]}
        />
      )}
      <EtatListe
        chargement={chargement}
        erreur={erreur}
        vide={affichees.length === 0}
        messageVide={resultats ? "Aucune réservation ne correspond à cette recherche." : "Aucune réservation."}
      >
        <Tableau entetes={["N°", "Locataire", "Appartement", "Séjour", "Montant", "Payé", "Statut", ""]}>
          {affichees.map((r) => {
            const resteAPayer = r.montantTotal - r.montantPaye;
            const active = estActive(r);
            const paiementPossible = r.statut === "EnAttente" || r.statut === "Confirmee";
            const nuits = nombreNuits(r);
            const reste = resteARembourser(r);
            const volet = ouvert?.id === r.id ? ouvert.volet : null;
            return (
              <Fragment key={r.id}>
                <tr className="border-b border-black/5">
                  <td className="px-5 py-3 text-gray-500 whitespace-nowrap">n° {formaterReference(r.id)}</td>
                  <td className="px-5 py-3 font-semibold whitespace-nowrap">{r.locataireNom}</td>
                  <td className="px-5 py-3 text-gray-600">{r.appartementAdresse}</td>
                  <td className="px-5 py-3 text-gray-600 whitespace-nowrap">
                    {formaterDate(r.dateArrivee)} → {formaterDate(r.dateDepart)}
                    <p className="text-xs text-gray-400">
                      {nuits} nuit{nuits > 1 ? "s" : ""}
                      {r.nombreVoyageurs != null &&
                        ` · ${r.nombreVoyageurs} voyageur${r.nombreVoyageurs > 1 ? "s" : ""}`}
                    </p>
                  </td>
                  <td className="px-5 py-3 whitespace-nowrap">{formaterMontant(r.montantTotal)}</td>
                  <td className="px-5 py-3 whitespace-nowrap">{formaterMontant(r.montantPaye)}</td>
                  <td className="px-5 py-3">
                    <Badge ton={statutsReservation[r.statut].ton}>{statutsReservation[r.statut].label}</Badge>
                    {r.statut === "DemandeEnvoyee" && (
                      <p className="text-xs text-gray-400 mt-1 whitespace-nowrap">
                        Réponse avant le {limiteReponse(r)}
                      </p>
                    )}
                    {echeancePaiement(r) && (
                      <p className="text-xs text-amber-700 mt-1 whitespace-nowrap">
                        Paiement avant le {echeancePaiement(r)}
                      </p>
                    )}
                    {r.statut === "Refusee" && r.motifRefus && (
                      <p className="text-xs text-gray-500 mt-1 max-w-48">{r.motifRefus}</p>
                    )}
                    {r.statut === "Annulee" && r.montantARembourser != null && (
                      <p className="text-xs mt-1 whitespace-nowrap">
                        {reste > 0 ? (
                          <span className="text-amber-700 font-semibold">À rembourser : {formaterMontant(reste)}</span>
                        ) : r.montantRembourse ? (
                          <span className="text-green-700">Remboursé {formaterMontant(r.montantRembourse)}</span>
                        ) : (
                          <span className="text-gray-400">Rien à rembourser</span>
                        )}
                      </p>
                    )}
                  </td>
                  <td className="px-5 py-3">
                    <div className="flex gap-2 justify-end">
                      <button
                        onClick={() => basculer(r.id, "messages")}
                        className={classeBoutonSecondaire(true)}
                        title="Lire les messages"
                      >
                        <MessageCircle size={14} />
                        Messages
                      </button>
                      {(r.montantPaye > 0 || r.statut === "Confirmee" || r.statut === "Terminee") && (
                        <button
                          onClick={() => basculer(r.id, "documents")}
                          className={classeBoutonSecondaire(true)}
                          title="Paiements, reçus et état des lieux (PDF)"
                        >
                          <FileText size={14} />
                          Documents
                        </button>
                      )}
                      {r.statut === "DemandeEnvoyee" && (
                        <button onClick={() => basculer(r.id, "reponse")} className={classeBoutonSecondaire(true)}>
                          <MessageSquareReply size={14} />
                          Répondre
                        </button>
                      )}
                      {paiementPossible && resteAPayer > 0 && (
                        <button onClick={() => basculer(r.id, "paiement")} className={classeBoutonSecondaire(true)}>
                          <Banknote size={14} />
                          Paiement
                        </button>
                      )}
                      {r.statut === "Annulee" && reste > 0 && (
                        <button
                          onClick={() => basculer(r.id, "remboursement")}
                          className={classeBoutonSecondaire(true)}
                        >
                          <HandCoins size={14} />
                          Remboursement
                        </button>
                      )}
                      {active && (
                        <button onClick={() => basculer(r.id, "annulation")} className={classeBoutonDanger(true)}>
                          <Ban size={14} />
                          Annuler
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
                {volet === "reponse" && (
                  <tr className="border-b border-black/5 bg-immo-cream/40">
                    <td colSpan={8} className="px-5 py-4">
                      <p className="text-sm text-gray-600 mb-3">
                        Demande de {r.locataireNom} pour {r.appartementAdresse}
                        {r.bailleurNom ? ` (hôte : ${r.bailleurNom})` : ""}.
                      </p>
                      <ReponseDemande reservation={r} onTraitee={fermerEtRecharger} />
                    </td>
                  </tr>
                )}
                {volet === "paiement" && (
                  <tr className="border-b border-black/5 bg-immo-cream/40">
                    <td colSpan={8} className="px-5 py-4">
                      <FormulairePaiement
                        cible={{ reservationId: r.id }}
                        montantSuggere={resteAPayer}
                        onAnnuler={() => setOuvert(null)}
                        onEnregistre={fermerEtRecharger}
                      />
                    </td>
                  </tr>
                )}
                {volet === "remboursement" && (
                  <tr className="border-b border-black/5 bg-immo-cream/40">
                    <td colSpan={8} className="px-5 py-4">
                      <div className="flex flex-col gap-3 max-w-3xl">
                        <InfoAnnulation reservation={r} />
                        <FormulaireRemboursement
                          reservation={r}
                          onAnnuler={() => setOuvert(null)}
                          onEnregistre={fermerEtRecharger}
                        />
                      </div>
                    </td>
                  </tr>
                )}
                {volet === "documents" && (
                  <tr className="border-b border-black/5 bg-immo-cream/40">
                    <td colSpan={8} className="px-5 py-4">
                      <div className="flex flex-col gap-3 max-w-2xl">
                        {(r.statut === "Confirmee" || r.statut === "Terminee") && (
                          <div>
                            <BoutonPdf fichier={etatDesLieuxPdf(r.id)} libelle="État des lieux (PDF)" />
                          </div>
                        )}
                        <HistoriquePaiements key={r.montantPaye} reservationId={r.id} />
                      </div>
                    </td>
                  </tr>
                )}
                {volet === "messages" && (
                  <tr className="border-b border-black/5 bg-immo-cream/40">
                    <td colSpan={8} className="px-5 py-4">
                      <p className="text-sm text-gray-600 mb-3">
                        Conversation entre {r.locataireNom} (voyageur) et {r.bailleurNom ?? "l'hôte"} (hôte).
                      </p>
                      <div className="max-w-2xl">
                        <FilMessages
                          reservationId={r.id}
                          interlocuteur={r.locataireNom}
                          lectureSeule
                          auteurADroite={r.locataireId}
                          confirmee={r.statut === "Confirmee" || r.statut === "Terminee"}
                        />
                      </div>
                    </td>
                  </tr>
                )}
                {volet === "annulation" && (
                  <tr className="border-b border-black/5 bg-immo-cream/40">
                    <td colSpan={8} className="px-5 py-4">
                      <div className="max-w-2xl">
                        <AnnulationReservation
                          reservation={r}
                          par="Admin"
                          onFermer={() => setOuvert(null)}
                          onAnnulee={fermerEtRecharger}
                        />
                      </div>
                    </td>
                  </tr>
                )}
              </Fragment>
            );
          })}
        </Tableau>
      </EtatListe>
    </>
  );
}
