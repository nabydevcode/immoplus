import { useState } from "react";
import { Link } from "react-router-dom";
import {
  Ban,
  CalendarDays,
  CalendarX,
  ChevronDown,
  ClipboardCheck,
  KeyRound,
  MapPin,
  MessageCircle,
  Receipt,
  Smartphone,
  Star,
  Users,
} from "lucide-react";
import { useAuth } from "../context/auth";
import { Badge, MessageErreur, Onglets, VoirPlus } from "../components/admin/ui";
import { useListeAOnglets } from "../components/admin/pagination";
import {
  classeBoutonDanger,
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  formaterDate,
  formaterMontant,
  formaterReference,
} from "../components/admin/outils";
import type { ReservationDto } from "../types/models";
import DetailPrix from "../components/sejour/DetailPrix";
import { localisation } from "../components/sejour/equipements";
import { formaterJour, jour, jourLocal } from "../components/sejour/dates";
import {
  avisVoyageurPossible,
  echeancePaiement,
  estActive,
  limiteReponse,
  MENTION_ADRESSE_MASQUEE,
  messagerieOuverte,
  nombreNuits,
  statutsReservation,
} from "../components/sejour/reservations";
import AnnulationReservation from "../components/sejour/AnnulationReservation";
import InfoAnnulation from "../components/sejour/InfoAnnulation";
import FilMessages from "../components/messages/FilMessages";
import FormulaireAvis from "../components/avis/FormulaireAvis";
import { AvisDeLaReservation } from "../components/avis/AffichageAvis";
import PaiementMobile from "../components/paiement/PaiementMobile";
import HistoriquePaiements from "../components/paiement/HistoriquePaiements";
import VotreArrivee from "../components/arrivee/VotreArrivee";
import EtatDesLieuxPhotos from "../components/arrivee/EtatDesLieuxPhotos";
import { etatDesLieuxPossible } from "../components/arrivee/etatDesLieux";
import { useNonLus } from "../components/messages/useMessagesNonLus";
import { useLienConnexion } from "../hooks/useLienConnexion";

type Filtre = "AVenir" | "AEvaluer" | "Toutes";
type Volet = "messages" | "avis" | "monAvis" | "annulation" | "paiement" | "paiements" | "arrivee" | "etatDesLieux";

/** Onglets demandés à l'API (reservation/lister?filtre=…), avec le même calcul local tant qu'elle n'est pas paginée. */
const ONGLETS = ["AVenir", "AEvaluer", "Toutes"] as const;
const requeteOnglet = (f: Filtre) => (f === "Toutes" ? "" : `filtre=${f}`);
const aujourdhuiLocal = () => jourLocal();
const dansOnglet = (f: Filtre, r: ReservationDto) => {
  const aujourdhui = aujourdhuiLocal();
  if (f === "AVenir") return estActive(r) && jour(r.dateDepart) >= aujourdhui;
  if (f === "AEvaluer") return avisVoyageurPossible(r, aujourdhui);
  return true;
};
// À venir : par date d'arrivée ; à évaluer et toutes : les plus récentes en premier.
const trier = (f: Filtre, a: ReservationDto, b: ReservationDto) =>
  f === "AVenir" ? a.dateArrivee.localeCompare(b.dateArrivee) : b.dateArrivee.localeCompare(a.dateArrivee);
const LOCAL = { dansOnglet, trier };

export default function MesReservations() {
  const { estConnecte, roleUtilisateur, utilisateurId } = useAuth();
  const lienConnexion = useLienConnexion();
  const estLocataire = estConnecte && roleUtilisateur === "Locataire";
  const [filtreChoisi, setFiltre] = useState<Filtre | null>(null);
  const [detailOuvert, setDetailOuvert] = useState<number | null>(null);
  const [volet, setVolet] = useState<{ id: number; volet: Volet } | null>(null);
  const [confirmation, setConfirmation] = useState<string | null>(null);
  const basculer = (id: number, v: Volet) => setVolet(volet?.id === id && volet.volet === v ? null : { id, volet: v });
  // Messages non lus en temps réel : pastilles des onglets et des boutons « Messages » de chaque réservation.
  const nonLus = useNonLus(estLocataire, ONGLETS);
  const [aujourdhui] = useState(jourLocal);
  // Compteurs des onglets (requêtes séparées) : l'onglet par défaut en dépend.
  const [ongletDemande, setOngletDemande] = useState<Filtre>("AVenir");
  const liste = useListeAOnglets<ReservationDto, Filtre>({
    base: estLocataire ? "reservation/lister" : null,
    onglet: filtreChoisi ?? ongletDemande,
    onglets: ONGLETS,
    requeteOnglet,
    mode: "progressif",
    local: LOCAL,
  });
  const { chargement, erreur, recharger, compteurs } = liste;

  if (!estLocataire) {
    return (
      <div className="px-6 py-16 max-w-xl mx-auto text-center text-gray-600">
        <Link to={lienConnexion} className="text-immo-gold font-semibold">
          Connectez-vous
        </Link>{" "}
        avec un compte Locataire pour suivre vos réservations.
      </div>
    );
  }

  const nbAEvaluer = compteurs.AEvaluer ?? 0;
  const nbToutes = compteurs.Toutes ?? 0;
  /** « À venir (3) », ou « À venir » si l'API ne fournit pas ce compteur. */
  const libelle = (f: Filtre, texte: string) => (compteurs[f] !== undefined ? `${texte} (${compteurs[f]})` : texte);
  // L'onglet « À évaluer » n'apparaît que s'il y a des séjours à évaluer (ou si leur nombre est inconnu).
  const avecAEvaluer = liste.compteursCharges && compteurs.AEvaluer !== 0;
  const nonLusDe1 = (r: ReservationDto) =>
    nonLus.charge ? (nonLus.parReservation.get(r.id) ?? 0) : (r.messagesNonLus ?? 0);
  // Sans séjour à venir, la page s'ouvre sur les séjours à évaluer (lien de l'e-mail « Séjour terminé »).
  const filtreParDefaut: Filtre = compteurs.AVenir === 0 && nbAEvaluer > 0 ? "AEvaluer" : "AVenir";
  if (filtreChoisi === null && ongletDemande !== filtreParDefaut) setOngletDemande(filtreParDefaut);
  // Plus aucun séjour à évaluer (avis envoyé) : retour sur toutes les réservations.
  if (filtreChoisi === "AEvaluer" && compteurs.AEvaluer === 0) setFiltre("Toutes");
  const filtre: Filtre = filtreChoisi ?? ongletDemande;
  const affichees = liste.elements;
  const changerFiltre = (f: Filtre) => {
    setFiltre(f);
    setVolet(null);
    setConfirmation(null);
  };

  return (
    <div className="px-4 sm:px-6 py-10">
      <div className="max-w-4xl mx-auto">
        <h1 className="text-2xl md:text-3xl font-extrabold mb-1">Mes réservations</h1>
        <p className="text-gray-500 text-sm mb-8">Suivez vos séjours courts : demandes, dates, paiement et statut.</p>

        <Onglets
          valeur={filtre}
          onChange={changerFiltre}
          options={[
            { valeur: "AVenir", label: libelle("AVenir", "À venir"), pastille: nonLus.parFiltre.AVenir },
            ...(avecAEvaluer || filtre === "AEvaluer"
              ? [
                  {
                    valeur: "AEvaluer" as const,
                    label: libelle("AEvaluer", "À évaluer"),
                    pastille: nonLus.parFiltre.AEvaluer,
                  },
                ]
              : []),
            // Messages non lus par onglet (GET messages/non-lus?filtre=…), quelles que soient les pages chargées.
            { valeur: "Toutes", label: libelle("Toutes", "Toutes"), pastille: nonLus.total },
          ]}
        />

        {filtre === "AVenir" && nbAEvaluer > 0 && (
          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm bg-immo-gold/15 rounded-xl px-4 py-3 mb-4">
            <Star size={16} className="text-immo-gold shrink-0" fill="currentColor" />
            {nbAEvaluer > 1 ? `${nbAEvaluer} séjours terminés attendent` : "Un séjour terminé attend"} votre avis.
            <button type="button" onClick={() => changerFiltre("AEvaluer")} className="font-semibold underline">
              Laisser un avis
            </button>
          </p>
        )}
        {confirmation && <p className="bg-green-50 text-green-700 text-sm rounded-lg px-4 py-3 mb-4">{confirmation}</p>}

        {chargement ? (
          <p className="text-gray-500 text-sm">Chargement...</p>
        ) : erreur ? (
          <MessageErreur>{erreur}</MessageErreur>
        ) : affichees.length === 0 ? (
          <div className="bg-white rounded-2xl shadow-sm border border-black/5 py-14 px-6 text-center">
            <CalendarX size={32} className="mx-auto text-immo-gold mb-3" />
            <p className="text-gray-500 mb-4">
              {filtre === "AVenir" && nbToutes > 0
                ? "Aucune réservation à venir."
                : "Vous n'avez encore réservé aucun séjour."}
            </p>
            <Link to="/annonces/sejour-court" className={classeBoutonPrincipal()}>
              Découvrir les séjours courts
            </Link>
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            {affichees.map((r) => {
              const statut = statutsReservation[r.statut];
              const lieu = localisation({ ville: r.appartementVille, quartier: r.appartementQuartier });
              const paiementAttendu = r.statut === "EnAttente" || r.statut === "Confirmee" || r.statut === "Terminee";
              const nuits = nombreNuits(r);
              const reste = Math.max(r.montantTotal - r.montantPaye, 0);
              const annulable = estActive(r) && jour(r.dateArrivee) >= aujourdhui;
              const avisPossible = avisVoyageurPossible(r, aujourdhui);
              const ouvert = volet?.id === r.id ? volet.volet : null;
              const nonLus = nonLusDe1(r);
              const payable = reste > 0 && (r.statut === "EnAttente" || r.statut === "Confirmee");
              const echeance = echeancePaiement(r);
              // Avant confirmation, l'API ne donne qu'une adresse approximative (quartier, ville).
              const adresseMasquee = r.statut === "DemandeEnvoyee" || r.statut === "EnAttente";
              return (
                <article
                  key={r.id}
                  className="bg-white rounded-2xl shadow-sm border border-black/5 p-5 flex flex-col gap-4"
                >
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-xs font-semibold text-gray-500">Réservation n° {formaterReference(r.id)}</p>
                      <h2 className="font-bold flex items-start gap-1.5 mt-1 wrap-break-word">
                        <MapPin size={16} className="text-immo-gold shrink-0 mt-0.5" />
                        <Link
                          to={`/annonces/sejour-court/${r.appartementId}`}
                          className="hover:text-immo-gold min-w-0 wrap-break-word"
                        >
                          {r.appartementAdresse}
                        </Link>
                      </h2>
                      {(lieu || r.bailleurNom) && (
                        <p className="text-xs text-gray-500 mt-0.5 ml-6">
                          {[
                            lieu !== r.appartementAdresse ? lieu : null,
                            r.bailleurNom ? `Hôte : ${r.bailleurNom}` : null,
                          ]
                            .filter(Boolean)
                            .join(" · ")}
                        </p>
                      )}
                      {adresseMasquee && <p className="text-xs text-gray-400 mt-0.5 ml-6">{MENTION_ADRESSE_MASQUEE}</p>}
                    </div>
                    <Badge ton={statut.ton}>{statut.label}</Badge>
                  </div>

                  <p className="text-sm text-gray-700 flex items-start gap-1.5">
                    <CalendarDays size={16} className="text-gray-400 shrink-0 mt-0.5" />
                    <span>
                      Du <strong>{formaterDate(r.dateArrivee)}</strong> au <strong>{formaterDate(r.dateDepart)}</strong>{" "}
                      · {nuits} nuit{nuits > 1 ? "s" : ""}
                    </span>
                    {r.nombreVoyageurs != null && (
                      <span className="inline-flex items-center gap-1 text-gray-500 ml-2 whitespace-nowrap">
                        <Users size={14} />
                        {r.nombreVoyageurs}
                      </span>
                    )}
                  </p>

                  {r.statut === "DemandeEnvoyee" && (
                    <p className="text-xs text-blue-800 bg-blue-50 rounded-lg px-3 py-2">
                      Demande envoyée à l'hôte : il a jusqu'au {limiteReponse(r)} pour l'accepter. Aucun paiement n'est
                      demandé avant son accord.
                    </p>
                  )}
                  {r.statut === "Refusee" && (
                    <p className="text-xs text-red-800 bg-red-50 rounded-lg px-3 py-2">
                      Demande refusée{r.motifRefus ? ` : ${r.motifRefus}` : "."}
                    </p>
                  )}

                  {paiementAttendu ? (
                    <dl className="grid grid-cols-1 sm:grid-cols-3 gap-2 sm:gap-4 text-sm bg-immo-dark/[0.03] rounded-xl p-4">
                      <div className="flex justify-between sm:block">
                        <dt className="text-gray-500">Montant total</dt>
                        <dd className="font-extrabold">{formaterMontant(r.montantTotal)}</dd>
                      </div>
                      <div className="flex justify-between sm:block">
                        <dt className="text-gray-500">Payé</dt>
                        <dd className="font-semibold text-green-700">{formaterMontant(r.montantPaye)}</dd>
                      </div>
                      <div className="flex justify-between sm:block">
                        <dt className="text-gray-500">Reste dû</dt>
                        <dd className={`font-semibold ${reste > 0 && estActive(r) ? "text-amber-700" : ""}`}>
                          {formaterMontant(reste)}
                        </dd>
                      </div>
                    </dl>
                  ) : (
                    <p className="text-sm flex justify-between bg-immo-dark/[0.03] rounded-xl p-4">
                      <span className="text-gray-500">Montant total</span>
                      <strong>{formaterMontant(r.montantTotal)}</strong>
                    </p>
                  )}

                  {r.prixParNuit != null && (
                    <div>
                      <button
                        type="button"
                        onClick={() => setDetailOuvert(detailOuvert === r.id ? null : r.id)}
                        aria-expanded={detailOuvert === r.id}
                        className="inline-flex items-center gap-1 text-sm font-semibold text-gray-700 hover:text-immo-dark"
                      >
                        <ChevronDown
                          size={16}
                          className={`transition-transform ${detailOuvert === r.id ? "rotate-180" : ""}`}
                        />
                        Détail du prix
                      </button>
                      {detailOuvert === r.id && (
                        <div className="mt-3 max-w-sm">
                          <DetailPrix
                            nombreNuits={nuits}
                            prixParNuit={r.prixParNuit}
                            reductionMontant={r.reductionMontant}
                            fraisMenage={r.fraisMenage}
                            montantTotal={r.montantTotal}
                            caution={r.caution}
                          />
                        </div>
                      )}
                    </div>
                  )}

                  {r.statut === "EnAttente" && (
                    <p className="text-xs text-amber-800 bg-amber-50 rounded-lg px-3 py-2">
                      {echeance && (
                        <strong className="block mb-0.5">
                          Payez avant le {echeance}, sinon la réservation sera annulée.
                        </strong>
                      )}
                      Votre réservation sera confirmée dès réception du règlement complet.
                    </p>
                  )}

                  {r.statut === "Annulee" && <InfoAnnulation reservation={r} pourVoyageur />}

                  {avisPossible && r.avisPossibleJusquau && (
                    <p className="text-xs text-gray-600">
                      Séjour terminé : vous pouvez laisser un avis jusqu'au {formaterJour(r.avisPossibleJusquau)}.
                    </p>
                  )}

                  <div className="flex flex-wrap items-center gap-2">
                    {payable && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "paiement")}
                        aria-expanded={ouvert === "paiement"}
                        className={classeBoutonPrincipal(true)}
                      >
                        <Smartphone size={14} />
                        Payer par mobile money
                      </button>
                    )}
                    {r.statut === "Confirmee" && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "arrivee")}
                        aria-expanded={ouvert === "arrivee"}
                        className={classeBoutonSecondaire(true)}
                      >
                        <KeyRound size={14} />
                        Votre arrivée
                      </button>
                    )}
                    {etatDesLieuxPossible(r) && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "etatDesLieux")}
                        aria-expanded={ouvert === "etatDesLieux"}
                        className={classeBoutonSecondaire(true)}
                      >
                        <ClipboardCheck size={14} />
                        État des lieux
                      </button>
                    )}
                    {r.montantPaye > 0 && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "paiements")}
                        aria-expanded={ouvert === "paiements"}
                        className={classeBoutonSecondaire(true)}
                      >
                        <Receipt size={14} />
                        Paiements
                      </button>
                    )}
                    <button
                      type="button"
                      onClick={() => basculer(r.id, "messages")}
                      aria-expanded={ouvert === "messages"}
                      className={classeBoutonSecondaire(true)}
                    >
                      <MessageCircle size={14} />
                      Messages
                      {nonLus > 0 && ouvert !== "messages" && (
                        <span className="min-w-5 h-5 px-1 rounded-full bg-red-600 text-white text-[11px] font-bold flex items-center justify-center">
                          {nonLus}
                        </span>
                      )}
                    </button>
                    {avisPossible && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "avis")}
                        className={classeBoutonPrincipal(true)}
                      >
                        <Star size={14} />
                        Laisser un avis
                      </button>
                    )}
                    {r.avisVoyageurDonne && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "monAvis")}
                        aria-expanded={ouvert === "monAvis"}
                        className={classeBoutonSecondaire(true)}
                      >
                        <Star size={14} />
                        Mon avis
                      </button>
                    )}
                    {annulable && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "annulation")}
                        className={`${classeBoutonDanger(true)} sm:ml-auto`}
                      >
                        <Ban size={14} />
                        Annuler ma réservation
                      </button>
                    )}
                  </div>

                  {ouvert === "paiement" && (
                    <>
                      <PaiementMobile reservation={r} onPaye={recharger} onFermer={() => setVolet(null)} />
                      {r.montantPaye > 0 && <HistoriquePaiements key={r.montantPaye} reservationId={r.id} />}
                    </>
                  )}
                  {ouvert === "paiements" && <HistoriquePaiements key={r.montantPaye} reservationId={r.id} />}
                  {ouvert === "arrivee" && <VotreArrivee reservation={r} />}
                  {ouvert === "etatDesLieux" && <EtatDesLieuxPhotos reservation={r} utilisateurId={utilisateurId} />}
                  {ouvert === "messages" && (
                    <FilMessages
                      reservationId={r.id}
                      interlocuteur={r.bailleurNom ? `l'hôte (${r.bailleurNom})` : "l'hôte"}
                      confirmee={r.statut === "Confirmee" || r.statut === "Terminee"}
                      fermee={!messagerieOuverte(r, aujourdhui)}
                    />
                  )}
                  {ouvert === "avis" && (
                    <FormulaireAvis
                      reservationId={r.id}
                      pourLogement
                      cible={r.appartementAdresse}
                      dateLimite={r.avisPossibleJusquau}
                      onAnnuler={() => setVolet(null)}
                      onEnvoye={() => {
                        setVolet(null);
                        setFiltre(filtre);
                        setConfirmation("Merci ! Votre avis est publié sur l'annonce.");
                        recharger();
                      }}
                    />
                  )}
                  {ouvert === "monAvis" && <AvisDeLaReservation appartementId={r.appartementId} reservationId={r.id} />}
                  {ouvert === "annulation" && (
                    <AnnulationReservation
                      reservation={r}
                      par="Voyageur"
                      onFermer={() => setVolet(null)}
                      onAnnulee={() => {
                        setVolet(null);
                        setConfirmation(`La réservation n° ${formaterReference(r.id)} est annulée.`);
                        recharger();
                      }}
                    />
                  )}
                </article>
              );
            })}
            <VoirPlus
              aPlus={liste.aPlus}
              chargement={liste.chargementPlus}
              onClick={liste.chargerPlus}
              affiches={affichees.length}
              total={liste.total}
              libelle="Voir plus de réservations"
            />
          </div>
        )}
      </div>
    </div>
  );
}
