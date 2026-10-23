import { useState } from "react";
import { Link } from "react-router-dom";
import {
  Ban,
  CalendarDays,
  CalendarX,
  ClipboardCheck,
  Clock,
  KeyRound,
  MapPin,
  MessageCircle,
  Receipt,
  Star,
  UserRound,
  Users,
} from "lucide-react";
import { useAuth } from "../../context/auth";
import { Badge, MessageErreur, Onglets, VoirPlus } from "../../components/admin/ui";
import { useListeAOnglets } from "../../components/admin/pagination";
import {
  classeBoutonDanger,
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  formaterDate,
  formaterMontant,
  formaterReference,
} from "../../components/admin/outils";
import ReponseDemande from "../../components/sejour/ReponseDemande";
import { localisation } from "../../components/sejour/equipements";
import { formaterJour, jour, jourLocal } from "../../components/sejour/dates";
import {
  avisHotePossible,
  echeancePaiement,
  limiteReponse,
  messagerieOuverte,
  nombreNuits,
  statutsReservation,
} from "../../components/sejour/reservations";
import AnnulationReservation from "../../components/sejour/AnnulationReservation";
import InfoAnnulation from "../../components/sejour/InfoAnnulation";
import FilMessages from "../../components/messages/FilMessages";
import FormulaireAvis from "../../components/avis/FormulaireAvis";
import { AvisDeLaReservation, ReputationVoyageur } from "../../components/avis/AffichageAvis";
import type { ReservationDto } from "../../types/models";
import HistoriquePaiements from "../../components/paiement/HistoriquePaiements";
import VotreArrivee from "../../components/arrivee/VotreArrivee";
import EtatDesLieuxPhotos from "../../components/arrivee/EtatDesLieuxPhotos";
import { etatDesLieuxPossible } from "../../components/arrivee/etatDesLieux";
import { useNonLus } from "../../components/messages/useMessagesNonLus";
import { useLienConnexion } from "../../hooks/useLienConnexion";

type Filtre = "Demandes" | "AVenir" | "AEvaluer" | "Passees";
type Volet = "messages" | "evaluer" | "avisVoyageur" | "annulation" | "paiements" | "arrivee" | "etatDesLieux";

const messagesVides: Record<Filtre, string> = {
  Demandes: "Aucune demande en attente de votre réponse.",
  AVenir: "Aucun séjour à venir.",
  AEvaluer: "Aucun voyageur à évaluer.",
  Passees: "Aucune réservation passée.",
};

/** Onglets demandés à l'API (reservation/lister?filtre=…), avec le même calcul local tant qu'elle n'est pas paginée. */
const ONGLETS = ["Demandes", "AVenir", "AEvaluer", "Passees"] as const;
const requeteOnglet = (f: Filtre) => `filtre=${f}`;
const estDemande = (r: ReservationDto) => r.statut === "DemandeEnvoyee";
const estAVenir = (r: ReservationDto, aujourdhui: string) =>
  (r.statut === "EnAttente" || r.statut === "Confirmee") && jour(r.dateDepart) >= aujourdhui;
const dansOnglet = (f: Filtre, r: ReservationDto) => {
  const aujourdhui = jourLocal();
  if (f === "Demandes") return estDemande(r);
  if (f === "AVenir") return estAVenir(r, aujourdhui);
  const passee = !estDemande(r) && !estAVenir(r, aujourdhui);
  return f === "AEvaluer" ? passee && avisHotePossible(r, aujourdhui) : passee;
};
// Demandes : les plus anciennes d'abord (délai de réponse) ; à venir : par arrivée ; passées : les plus récentes d'abord.
const trier = (f: Filtre, a: ReservationDto, b: ReservationDto) =>
  f === "Demandes"
    ? a.dateCreation.localeCompare(b.dateCreation)
    : f === "AVenir"
      ? a.dateArrivee.localeCompare(b.dateArrivee)
      : b.dateArrivee.localeCompare(a.dateArrivee);
const LOCAL = { dansOnglet, trier };

/** Réservations des séjours courts du bailleur : demandes à accepter ou refuser, séjours à venir et passés. */
export default function ReservationsRecues() {
  const { estConnecte, roleUtilisateur, utilisateurId } = useAuth();
  const lienConnexion = useLienConnexion();
  const estBailleur = estConnecte && roleUtilisateur === "Bailleur";
  const [aujourdhui] = useState(jourLocal);
  const [filtreChoisi, setFiltre] = useState<Filtre | null>(null);
  const [ongletDemande, setOngletDemande] = useState<Filtre>("Demandes");
  // L'API ne renvoie au bailleur que les réservations de ses logements ; seul l'onglet affiché est chargé.
  const liste = useListeAOnglets<ReservationDto, Filtre>({
    base: estBailleur ? "reservation/lister" : null,
    onglet: filtreChoisi ?? ongletDemande,
    onglets: ONGLETS,
    requeteOnglet,
    mode: "progressif",
    local: LOCAL,
  });
  const { chargement, erreur, recharger, compteurs } = liste;
  const [confirmation, setConfirmation] = useState<string | null>(null);
  const [volet, setVolet] = useState<{ id: number; volet: Volet } | null>(null);
  const basculer = (id: number, v: Volet) => setVolet(volet?.id === id && volet.volet === v ? null : { id, volet: v });
  // Messages non lus en temps réel : pastilles des onglets et des boutons « Messages » de chaque réservation.
  const nonLus = useNonLus(estBailleur, ONGLETS);

  if (!estBailleur) {
    return (
      <div className="px-6 py-16 max-w-xl mx-auto text-center text-gray-600">
        <Link to={lienConnexion} className="text-immo-gold font-semibold">
          Connectez-vous
        </Link>{" "}
        avec un compte Bailleur pour voir les réservations de vos logements.
      </div>
    );
  }

  const nb = (f: Filtre) => compteurs[f] ?? 0;
  /** « Demandes (3) », ou « Demandes » si l'API ne fournit pas ce compteur. */
  const libelle = (f: Filtre, texte: string) => (compteurs[f] !== undefined ? `${texte} (${compteurs[f]})` : texte);
  // L'onglet « À évaluer » n'apparaît que s'il y a des voyageurs à évaluer (ou si leur nombre est inconnu).
  const avecAEvaluer = liste.compteursCharges && compteurs.AEvaluer !== 0;
  const nonLusDe1 = (r: ReservationDto) =>
    nonLus.charge ? (nonLus.parReservation.get(r.id) ?? 0) : (r.messagesNonLus ?? 0);
  // Sans demande en attente, la page s'ouvre sur les voyageurs à évaluer (lien de l'e-mail « Séjour terminé »).
  const filtreParDefaut: Filtre = compteurs.Demandes === 0 && nb("AEvaluer") > 0 ? "AEvaluer" : "Demandes";
  if (filtreChoisi === null && ongletDemande !== filtreParDefaut) setOngletDemande(filtreParDefaut);
  // Plus aucun voyageur à évaluer (avis envoyé) : retour sur les réservations passées.
  if (filtreChoisi === "AEvaluer" && compteurs.AEvaluer === 0) setFiltre("Passees");
  const filtre: Filtre = filtreChoisi ?? ongletDemande;
  const affichees = liste.elements;
  const aucune = ONGLETS.every((f) => compteurs[f] === 0);

  return (
    <div className="px-4 sm:px-6 py-10">
      <div className="max-w-4xl mx-auto">
        <h1 className="text-2xl md:text-3xl font-extrabold mb-1">Réservations reçues</h1>
        <p className="text-gray-500 text-sm mb-8">
          Répondez aux demandes de séjour sous 48 h : sans réponse, elles sont refusées automatiquement.
        </p>

        <Onglets
          valeur={filtre}
          onChange={(f) => {
            setFiltre(f);
            setConfirmation(null);
            setVolet(null);
          }}
          options={[
            { valeur: "Demandes", label: libelle("Demandes", "Demandes"), pastille: nonLus.parFiltre.Demandes },
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
            { valeur: "Passees", label: libelle("Passees", "Passées"), pastille: nonLus.parFiltre.Passees },
          ]}
        />

        {confirmation && <p className="bg-green-50 text-green-700 text-sm rounded-lg px-4 py-3 mb-4">{confirmation}</p>}

        {chargement ? (
          <p className="text-gray-500 text-sm">Chargement...</p>
        ) : erreur ? (
          <MessageErreur>{erreur}</MessageErreur>
        ) : affichees.length === 0 ? (
          <div className="bg-white rounded-2xl shadow-sm border border-black/5 py-14 px-6 text-center">
            <CalendarX size={32} className="mx-auto text-immo-gold mb-3" />
            <p className="text-gray-500">{messagesVides[filtre]}</p>
            {aucune && (
              <Link to="/bailleur/logements" className="inline-block mt-3 text-sm font-semibold text-immo-gold">
                Gérer mes logements et leur calendrier
              </Link>
            )}
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            {affichees.map((r) => {
              const statut = statutsReservation[r.statut];
              const nuits = nombreNuits(r);
              const lieu = localisation({ ville: r.appartementVille, quartier: r.appartementQuartier });
              const ouvert = volet?.id === r.id ? volet.volet : null;
              const nonLus = nonLusDe1(r);
              const evaluable = avisHotePossible(r, aujourdhui);
              const annulable =
                (r.statut === "EnAttente" || r.statut === "Confirmee") && jour(r.dateArrivee) >= aujourdhui;
              return (
                <article
                  key={r.id}
                  className="bg-white rounded-2xl shadow-sm border border-black/5 p-5 flex flex-col gap-3"
                >
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-xs font-semibold text-gray-500">Réservation n° {formaterReference(r.id)}</p>
                      <h2 className="font-bold flex items-center gap-1.5 mt-1">
                        <UserRound size={16} className="text-immo-gold shrink-0" />
                        {r.locataireNom}
                      </h2>
                      {(r.statut === "DemandeEnvoyee" || r.statut === "EnAttente" || r.statut === "Confirmee") && (
                        <div className="mt-1 ml-6">
                          <ReputationVoyageur voyageurId={r.locataireId} />
                        </div>
                      )}
                    </div>
                    <Badge ton={statut.ton}>{statut.label}</Badge>
                  </div>

                  <p className="text-sm text-gray-700 flex items-start gap-1.5 wrap-break-word">
                    <MapPin size={16} className="text-gray-400 shrink-0 mt-0.5" />
                    <span className="min-w-0">
                      {r.appartementAdresse}
                      {lieu && <span className="text-gray-500"> · {lieu}</span>}
                    </span>
                  </p>
                  <p className="text-sm text-gray-700 flex flex-wrap items-center gap-x-3 gap-y-1">
                    <span className="inline-flex items-center gap-1.5">
                      <CalendarDays size={16} className="text-gray-400 shrink-0" />
                      Du <strong>{formaterDate(r.dateArrivee)}</strong> au <strong>{formaterDate(r.dateDepart)}</strong>
                      · {nuits} nuit{nuits > 1 ? "s" : ""}
                    </span>
                    {r.nombreVoyageurs != null && (
                      <span className="inline-flex items-center gap-1 text-gray-500">
                        <Users size={14} />
                        {r.nombreVoyageurs} voyageur{r.nombreVoyageurs > 1 ? "s" : ""}
                      </span>
                    )}
                  </p>

                  <div className="flex flex-wrap justify-between gap-2 text-sm bg-immo-dark/[0.03] rounded-xl px-4 py-3">
                    <span className="text-gray-500">Montant du séjour</span>
                    <strong>{formaterMontant(r.montantTotal)}</strong>
                  </div>

                  {r.statut === "DemandeEnvoyee" && (
                    <>
                      <p className="text-xs text-amber-800 bg-amber-50 rounded-lg px-3 py-2 flex items-center gap-1.5">
                        <Clock size={14} className="shrink-0" />
                        Répondez avant le {limiteReponse(r)}. Le voyageur paiera après votre accord.
                      </p>
                      <ReponseDemande
                        reservation={r}
                        onTraitee={() => {
                          setConfirmation(`Votre réponse à ${r.locataireNom} a été envoyée.`);
                          recharger();
                        }}
                      />
                    </>
                  )}
                  {r.statut === "EnAttente" && (
                    <p className="text-xs text-gray-500">
                      En attente du paiement du voyageur
                      {echeancePaiement(r)
                        ? ` : sans paiement avant le ${echeancePaiement(r)}, la réservation sera annulée automatiquement.`
                        : "."}
                    </p>
                  )}
                  {r.statut === "Refusee" && (
                    <p className="text-xs text-red-800 bg-red-50 rounded-lg px-3 py-2">
                      Refusée{r.motifRefus ? ` : ${r.motifRefus}` : "."}
                    </p>
                  )}
                  {r.statut === "Annulee" && <InfoAnnulation reservation={r} />}
                  {evaluable && r.avisPossibleJusquau && (
                    <p className="text-xs text-gray-600">
                      Séjour terminé : évaluez {r.locataireNom} jusqu'au {formaterJour(r.avisPossibleJusquau)}.
                    </p>
                  )}

                  <div className="flex flex-wrap items-center gap-2">
                    {r.statut === "Confirmee" && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "arrivee")}
                        aria-expanded={ouvert === "arrivee"}
                        className={classeBoutonSecondaire(true)}
                      >
                        <KeyRound size={14} />
                        Arrivée
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
                    {evaluable && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "evaluer")}
                        className={classeBoutonPrincipal(true)}
                      >
                        <Star size={14} />
                        Évaluer le voyageur
                      </button>
                    )}
                    {r.avisVoyageurDonne && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "avisVoyageur")}
                        aria-expanded={ouvert === "avisVoyageur"}
                        className={classeBoutonSecondaire(true)}
                      >
                        <Star size={14} />
                        Avis du voyageur
                      </button>
                    )}
                    {annulable && (
                      <button
                        type="button"
                        onClick={() => basculer(r.id, "annulation")}
                        className={`${classeBoutonDanger(true)} sm:ml-auto`}
                      >
                        <Ban size={14} />
                        Annuler la réservation
                      </button>
                    )}
                  </div>

                  {ouvert === "arrivee" && <VotreArrivee reservation={r} pourHote />}
                  {ouvert === "etatDesLieux" && <EtatDesLieuxPhotos reservation={r} utilisateurId={utilisateurId} />}
                  {ouvert === "paiements" && <HistoriquePaiements reservationId={r.id} />}
                  {ouvert === "messages" && (
                    <FilMessages
                      reservationId={r.id}
                      interlocuteur={r.locataireNom}
                      confirmee={r.statut === "Confirmee" || r.statut === "Terminee"}
                      fermee={!messagerieOuverte(r, aujourdhui)}
                    />
                  )}
                  {ouvert === "evaluer" && (
                    <FormulaireAvis
                      reservationId={r.id}
                      pourLogement={false}
                      cible={r.locataireNom}
                      dateLimite={r.avisPossibleJusquau}
                      onAnnuler={() => setVolet(null)}
                      onEnvoye={() => {
                        setVolet(null);
                        setFiltre(filtre);
                        setConfirmation(`Votre avis sur ${r.locataireNom} est enregistré.`);
                        recharger();
                      }}
                    />
                  )}
                  {ouvert === "avisVoyageur" && (
                    <AvisDeLaReservation appartementId={r.appartementId} reservationId={r.id} peutRepondre />
                  )}
                  {ouvert === "annulation" && (
                    <AnnulationReservation
                      reservation={r}
                      par="Hote"
                      onFermer={() => setVolet(null)}
                      onAnnulee={() => {
                        setVolet(null);
                        setConfirmation(`La réservation de ${r.locataireNom} est annulée ; le voyageur est prévenu.`);
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
