import { useState, type FormEvent } from "react";
import { MessageSquareReply, Star, UserRound } from "lucide-react";
import { apiClient } from "../../api/client";
import type { AvisDto, ListeAvisDto } from "../../types/models";
import { MessageErreur, VoirPlus } from "../admin/ui";
import { useListeProgressive } from "../admin/pagination";
import {
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  messageErreur,
  useChargement,
} from "../admin/outils";
import { NoteEtoiles } from "./Etoiles";
import Signaler from "../signalement/Signaler";
import {
  avisDeLaReponse,
  CRITERES_AVIS,
  formaterNote,
  listeAvis,
  moisAnnee,
  REPONSE_MAX,
  type ReponseAvis,
} from "./notes";

/** « ★ 4,8 · 12 avis » ou « Nouveau » quand le logement n'a pas encore d'avis. */
export function ResumeNote({
  note,
  nombre,
  petit,
  lien,
}: {
  note: number | null | undefined;
  nombre: number | null | undefined;
  petit?: boolean;
  lien?: string;
}) {
  const taille = petit ? "text-xs" : "text-sm";
  if (note == null || !nombre) {
    return <span className={`${taille} font-semibold text-immo-dark`}>Nouveau</span>;
  }
  const contenu = (
    <>
      <Star size={petit ? 12 : 15} className="text-immo-gold" fill="currentColor" />
      <span className="font-bold">{formaterNote(note)}</span>
      <span className={lien ? "underline" : "text-gray-500"}>{petit ? `(${nombre})` : `· ${nombre} avis`}</span>
    </>
  );
  return lien ? (
    <a href={lien} className={`inline-flex items-center gap-1 ${taille} hover:text-immo-dark`}>
      {contenu}
    </a>
  ) : (
    <span className={`inline-flex items-center gap-1 ${taille}`}>{contenu}</span>
  );
}

/** Réponse publique de l'hôte à un avis (une seule fois). */
function FormulaireReponse({ avis, onRepondu }: { avis: AvisDto; onRepondu: () => void }) {
  const [ouvert, setOuvert] = useState(false);
  const [reponse, setReponse] = useState("");
  const [erreur, setErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    if (!reponse.trim()) return;
    setErreur(null);
    setEnCours(true);
    try {
      await apiClient.post(`avis/${avis.id}/reponse`, { reponse: reponse.trim() });
      onRepondu();
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'enregistrer votre réponse."));
    } finally {
      setEnCours(false);
    }
  };

  if (!ouvert) {
    return (
      <button type="button" onClick={() => setOuvert(true)} className={`${classeBoutonSecondaire(true)} self-start`}>
        <MessageSquareReply size={14} />
        Répondre publiquement
      </button>
    );
  }
  return (
    <form onSubmit={envoyer} className="flex flex-col gap-2">
      <label className="text-sm font-medium text-gray-700">
        Votre réponse <span className="text-gray-400 font-normal">(publiée sous l'avis, une seule fois)</span>
        <textarea
          value={reponse}
          onChange={(e) => setReponse(e.target.value)}
          rows={3}
          maxLength={REPONSE_MAX}
          className={classeChamp}
        />
        <span className="block text-xs text-gray-400 font-normal mt-1">
          {reponse.trim().length} / {REPONSE_MAX} caractères
        </span>
      </label>
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={enCours || !reponse.trim()} className={classeBoutonPrincipal(true)}>
          {enCours ? "Envoi..." : "Publier la réponse"}
        </button>
        <button type="button" onClick={() => setOuvert(false)} className={classeBoutonSecondaire(true)}>
          Annuler
        </button>
      </div>
    </form>
  );
}

/** Un avis : auteur, date, note, commentaire et éventuelle réponse de l'hôte. */
export function CarteAvis({
  avis,
  peutRepondre,
  onRepondu,
  signalable,
}: {
  avis: AvisDto;
  peutRepondre?: boolean;
  onRepondu?: () => void;
  /** Affiche le lien « Signaler » (avis publics de la fiche). */
  signalable?: boolean;
}) {
  return (
    <article className="flex flex-col gap-2 min-w-0">
      <div className="flex items-center gap-3">
        <span className="flex items-center justify-center w-9 h-9 rounded-full bg-immo-dark/5 text-gray-500 shrink-0">
          <UserRound size={18} />
        </span>
        <div className="min-w-0">
          <p className="font-semibold text-sm truncate">{avis.auteurNom}</p>
          <p className="text-xs text-gray-500 flex items-center gap-2">
            <NoteEtoiles note={avis.note} taille={12} />
            <span>{moisAnnee(avis.dateCreation)}</span>
          </p>
        </div>
      </div>
      <p className="text-sm text-gray-700 whitespace-pre-line wrap-break-word">{avis.commentaire}</p>
      {avis.reponseHote ? (
        <div className="ml-4 border-l-2 border-immo-gold/50 pl-3">
          <p className="text-xs font-semibold text-gray-700">
            Réponse de l'hôte{avis.dateReponse ? ` · ${moisAnnee(avis.dateReponse)}` : ""}
          </p>
          <p className="text-sm text-gray-600 whitespace-pre-line wrap-break-word">{avis.reponseHote}</p>
        </div>
      ) : (
        peutRepondre && onRepondu && <FormulaireReponse avis={avis} onRepondu={onRepondu} />
      )}
      {signalable && <Signaler cible="Avis" cibleId={avis.id} className="self-start" />}
    </article>
  );
}

/** Moyennes par critère, en barres. */
function MoyennesCriteres({ liste }: { liste: Pick<ListeAvisDto, "moyennes"> }) {
  const criteres = CRITERES_AVIS.map((c) => ({ ...c, valeur: liste.moyennes?.[c.cle] ?? null })).filter(
    (c) => c.valeur != null,
  );
  if (criteres.length === 0) return null;
  return (
    <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-3 mb-6">
      {criteres.map(({ cle, libelle, valeur }) => (
        <div key={cle} className="flex items-center gap-3 text-sm">
          <dt className="w-28 shrink-0 text-gray-600">{libelle}</dt>
          <dd className="flex-1 flex items-center gap-2 min-w-0">
            <span className="flex-1 h-1.5 rounded-full bg-gray-200 overflow-hidden">
              <span className="block h-full bg-immo-dark rounded-full" style={{ width: `${(valeur! / 5) * 100}%` }} />
            </span>
            <span className="font-semibold w-7 text-right">{formaterNote(valeur!)}</span>
          </dd>
        </div>
      ))}
    </dl>
  );
}

const AVIS_PAR_PAGE = 6;

/** Section « Avis » de la fiche d'une annonce ; le propriétaire peut répondre aux avis. */
export function SectionAvis({
  appartementId,
  estProprietaire,
  longueDuree,
}: {
  appartementId: number;
  estProprietaire: boolean;
  longueDuree?: boolean;
}) {
  // Moyennes et nombre d'avis : globaux (toutes pages) ; les avis eux-mêmes arrivent 6 par 6.
  const liste = useListeProgressive<AvisDto, ReponseAvis>(
    `appartement/${appartementId}/avis`,
    AVIS_PAR_PAGE,
    avisDeLaReponse,
  );
  const donnees = liste.brut;
  const { chargement, erreur, recharger } = liste;
  const avis = liste.elements;

  return (
    <section id="avis" className="scroll-mt-24">
      <h2 className="font-extrabold text-lg mb-1 flex items-center gap-2">
        {donnees?.moyenne != null && donnees.nombre > 0 ? (
          <>
            <Star size={18} className="text-immo-gold" fill="currentColor" />
            {formaterNote(donnees.moyenne)} · {donnees.nombre} avis
          </>
        ) : (
          "Avis"
        )}
      </h2>
      {chargement ? (
        <p className="text-sm text-gray-500">Chargement des avis...</p>
      ) : erreur && avis.length === 0 ? (
        <MessageErreur>{erreur}</MessageErreur>
      ) : !donnees || avis.length === 0 ? (
        <p className="text-sm text-gray-500">
          {longueDuree
            ? "Aucun avis pour le moment. Les locataires peuvent laisser un avis après leur location (ou après 6 mois sur place)."
            : "Aucun avis pour le moment : ce logement est nouveau sur ImmoPlus. Les voyageurs peuvent laisser un avis après leur séjour."}
        </p>
      ) : (
        <>
          <p className="text-sm text-gray-500 mb-4">
            {longueDuree
              ? "Avis laissés par les locataires du logement."
              : "Avis laissés par les voyageurs après leur séjour."}
          </p>
          <MoyennesCriteres liste={donnees} />
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-6">
            {avis.map((a) => (
              <CarteAvis
                key={a.id}
                avis={a}
                peutRepondre={estProprietaire}
                onRepondu={recharger}
                signalable={!estProprietaire}
              />
            ))}
          </div>
          <VoirPlus
            aPlus={liste.aPlus}
            chargement={liste.chargementPlus}
            onClick={liste.chargerPlus}
            affiches={avis.length}
            total={liste.total}
            libelle="Voir plus d'avis"
          />
          {erreur && <p className="text-sm text-red-600 mt-3">{erreur}</p>}
        </>
      )}
    </section>
  );
}

/** Avis du voyageur sur une réservation donnée (vu par l'hôte, qui peut y répondre, ou par le voyageur). */
export function AvisDeLaReservation({
  appartementId,
  reservationId,
  peutRepondre,
}: {
  appartementId: number;
  reservationId: number;
  peutRepondre?: boolean;
}) {
  // reservationId : filtre demandé à l'API ; sans lui, l'avis est cherché dans les 100 plus récents.
  const { donnees, chargement, erreur, recharger } = useChargement<ReponseAvis>(
    `appartement/${appartementId}/avis?reservationId=${reservationId}&page=1&taille=100`,
  );
  const avis = listeAvis(donnees).find((a) => a.reservationId === reservationId);
  if (chargement) return <p className="text-sm text-gray-500">Chargement de l'avis...</p>;
  if (erreur) return <MessageErreur>{erreur}</MessageErreur>;
  if (!avis) return <p className="text-sm text-gray-500">Avis introuvable.</p>;
  return (
    <div className="bg-immo-cream/60 rounded-xl p-4">
      <CarteAvis avis={avis} peutRepondre={peutRepondre} onRepondu={recharger} />
    </div>
  );
}

/** Ce que les autres hôtes disent d'un voyageur (avis HoteVersVoyageur), pour l'hôte qui reçoit une demande. */
export function ReputationVoyageur({ voyageurId }: { voyageurId: number }) {
  const { donnees, erreur } = useChargement<ReponseAvis>(`utilisateur/${voyageurId}/avis`);
  const [ouvert, setOuvert] = useState(false);
  if (erreur || !donnees) return null;
  if (donnees.nombre === 0 || donnees.moyenne == null) {
    return <p className="text-xs text-gray-500">Aucun avis d'hôte sur ce voyageur pour l'instant.</p>;
  }
  return (
    <div className="flex flex-col gap-3">
      <button
        type="button"
        onClick={() => setOuvert((v) => !v)}
        aria-expanded={ouvert}
        className="self-start inline-flex items-center gap-1 text-xs font-semibold text-gray-700 hover:text-immo-dark"
      >
        <Star size={12} className="text-immo-gold" fill="currentColor" />
        {formaterNote(donnees.moyenne)} · {donnees.nombre} avis d'hôtes
        <span className="underline font-normal ml-1">{ouvert ? "masquer" : "voir"}</span>
      </button>
      {ouvert && (
        <div className="flex flex-col gap-4 bg-immo-cream/60 rounded-xl p-4">
          {listeAvis(donnees).map((a) => (
            <CarteAvis key={a.id} avis={a} />
          ))}
        </div>
      )}
    </div>
  );
}
