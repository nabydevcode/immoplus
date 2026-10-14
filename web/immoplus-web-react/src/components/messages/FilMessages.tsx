import { useEffect, useRef, useState, type FormEvent } from "react";
import { Lock, Send } from "lucide-react";
import { apiClient } from "../../api/client";
import type { MessageDto } from "../../types/models";
import { MessageErreur } from "../admin/ui";
import { classeBoutonPrincipal, classeChamp, messageErreur, useChargement } from "../admin/outils";
import { contientCoordonnees, signalerMessages } from "./evenements";
import { signalerEcriture, useTempsReel } from "./tempsReel";
import Signaler from "../signalement/Signaler";

const CONTENU_MAX = 2000;
// Les messages arrivent en temps réel ; cette relecture ne sert que de filet si la connexion temps réel tombe.
const ACTUALISATION_MS = 60_000;
const DUREE_INDICATEUR_ECRITURE_MS = 4_000;
const INTERVALLE_SIGNAL_ECRITURE_MS = 2_000;

const heure = (iso: string) => {
  const d = new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(iso) ? iso : `${iso}Z`);
  return d.toLocaleString("fr-FR", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
};

/**
 * Fil de discussion d'une réservation entre le voyageur et l'hôte, en temps réel (nouveaux messages, « Lu », « écrit… »).
 * L'admin le lit sans pouvoir écrire ; les coordonnées ne passent qu'après confirmation.
 */
export default function FilMessages({
  reservationId,
  interlocuteur,
  lectureSeule,
  auteurADroite,
  fermee,
  confirmee,
  onLu,
}: {
  reservationId: number;
  /** Nom de l'autre partie, pour les libellés. */
  interlocuteur: string;
  /** Admin : lecture seule. */
  lectureSeule?: boolean;
  /** En lecture seule : auteur dont les messages s'affichent à droite (le voyageur). */
  auteurADroite?: number;
  /** Messagerie close (30 jours après la fin, l'annulation ou le refus). */
  fermee?: boolean;
  confirmee: boolean;
  /** Appelé une fois les messages reçus marqués comme lus. */
  onLu?: () => void;
}) {
  const { donnees, setDonnees, chargement, erreur, recharger } = useChargement<MessageDto[]>(
    `reservation/${reservationId}/messages`,
  );
  const [contenu, setContenu] = useState("");
  const [erreurEnvoi, setErreurEnvoi] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);
  const liste = useRef<HTMLDivElement>(null);
  const premierChargement = useRef(true);
  // Dernière version du rappel du parent, sans relancer l'effet de chargement à chaque rendu.
  const rappelLu = useRef(onLu);
  useEffect(() => {
    rappelLu.current = onLu;
  });

  useEffect(() => {
    const minuterie = window.setInterval(recharger, ACTUALISATION_MS);
    return () => window.clearInterval(minuterie);
  }, [recharger]);

  // L'autre participant est en train d'écrire (masqué après quelques secondes sans nouveau signal).
  const [ecrit, setEcrit] = useState(false);
  const minuterieEcrit = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(minuterieEcrit.current), []);
  const dernierSignal = useRef(0);

  useTempsReel("nouveauMessage", (m) => {
    if (m.reservationId !== reservationId) return;
    if (!m.deMoi) {
      setEcrit(false);
      // Relecture du fil : le message reçu est aussitôt marqué comme lu (accusé « Lu » chez l'auteur).
      if (!lectureSeule) recharger();
    }
    setDonnees((l) => (l && !l.some((x) => x.id === m.id) ? [...l, m] : l));
  });
  // Lecture par un participant : ses messages reçus passent à « Lu ». L'admin (lecture seule) reçoit aussi ces
  // événements via le groupe « admins » du hub : le fil se met à jour en direct.
  useTempsReel("messagesLus", (lecture) => {
    if (lecture.reservationId !== reservationId) return;
    setDonnees(
      (l) =>
        l &&
        l.map((m) =>
          !m.dateLecture && (lectureSeule ? m.auteurId !== lecture.lecteurId : m.deMoi)
            ? { ...m, dateLecture: lecture.dateLecture }
            : m,
        ),
    );
  });
  useTempsReel(
    "ecrit",
    (e) => {
      if (e.reservationId !== reservationId) return;
      setEcrit(true);
      window.clearTimeout(minuterieEcrit.current);
      minuterieEcrit.current = window.setTimeout(() => setEcrit(false), DUREE_INDICATEUR_ECRITURE_MS);
    },
    !lectureSeule,
  );

  // À chaque nouveau message : défilement vers le bas.
  const nombre = donnees?.length ?? 0;
  const charge = donnees !== null;
  useEffect(() => {
    if (!charge) return;
    liste.current?.scrollTo({ top: liste.current.scrollHeight });
    if (lectureSeule) return;
    // Les messages reçus viennent d'être marqués comme lus : la pastille de la barre de navigation se met à jour.
    signalerMessages();
    if (premierChargement.current) {
      premierChargement.current = false;
      rappelLu.current?.();
    }
  }, [nombre, charge, lectureSeule]);

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    const texte = contenu.trim();
    if (!texte) return;
    setErreurEnvoi(null);
    if (!confirmee && contientCoordonnees(texte)) {
      setErreurEnvoi("Les coordonnées sont partagées après confirmation de la réservation.");
      return;
    }
    setEnCours(true);
    try {
      const message = await apiClient.post<MessageDto>(`reservation/${reservationId}/messages`, { contenu: texte });
      setDonnees((l) => (l && !l.some((m) => m.id === message.id) ? [...l, message] : l));
      setContenu("");
    } catch (err) {
      setErreurEnvoi(messageErreur(err, "Impossible d'envoyer le message."));
    } finally {
      setEnCours(false);
    }
  };

  const messages = donnees ?? [];

  return (
    <div className="flex flex-col gap-3 bg-immo-cream/60 rounded-xl p-3 sm:p-4">
      <div
        ref={liste}
        className="flex flex-col gap-2 max-h-80 overflow-y-auto pr-1"
        aria-live="polite"
        aria-label={`Messages avec ${interlocuteur}`}
      >
        {chargement ? (
          <p className="text-sm text-gray-500">Chargement des messages...</p>
        ) : erreur ? (
          <MessageErreur>{erreur}</MessageErreur>
        ) : messages.length === 0 ? (
          <p className="text-sm text-gray-500 py-4 text-center">
            {lectureSeule ? "Aucun message échangé." : `Aucun message. Écrivez à ${interlocuteur} pour toute question.`}
          </p>
        ) : (
          messages.map((m) => {
            const aDroite = lectureSeule ? m.auteurId === auteurADroite : m.deMoi;
            return (
              <div key={m.id} className={`flex flex-col max-w-[85%] ${aDroite ? "self-end items-end" : "self-start"}`}>
                <p
                  className={`text-sm rounded-2xl px-3 py-2 whitespace-pre-line wrap-break-word ${
                    aDroite ? "bg-immo-dark text-white rounded-br-sm" : "bg-white border border-black/5 rounded-bl-sm"
                  }`}
                >
                  {m.contenu}
                </p>
                <p className="text-[11px] text-gray-400 mt-0.5 px-1">
                  {m.deMoi && !lectureSeule ? "Vous" : m.auteurNom} · {heure(m.dateEnvoi)}
                  {(lectureSeule || m.deMoi) && m.dateLecture ? " · Lu" : ""}
                </p>
                {!m.deMoi && !lectureSeule && <Signaler cible="Message" cibleId={m.id} className="px-1" />}
              </div>
            );
          })
        )}
      </div>

      {ecrit && !lectureSeule && (
        <p className="text-xs text-gray-500 italic -mt-1 px-1" aria-live="polite">
          {interlocuteur} est en train d'écrire…
        </p>
      )}

      {lectureSeule ? (
        <p className="text-xs text-gray-500 flex items-center gap-1.5">
          <Lock size={12} className="shrink-0" />
          Lecture seule, mise à jour en direct : seuls le voyageur et l'hôte peuvent écrire.
        </p>
      ) : fermee ? (
        <p className="text-xs text-gray-500 flex items-center gap-1.5">
          <Lock size={12} className="shrink-0" />
          Cette conversation est close.
        </p>
      ) : (
        <form onSubmit={envoyer} className="flex flex-col gap-2">
          <div className="flex items-end gap-2">
            <label className="flex-1 min-w-0">
              <span className="sr-only">Votre message à {interlocuteur}</span>
              <textarea
                value={contenu}
                onChange={(e) => {
                  setContenu(e.target.value);
                  const maintenant = Date.now();
                  if (maintenant - dernierSignal.current > INTERVALLE_SIGNAL_ECRITURE_MS) {
                    dernierSignal.current = maintenant;
                    signalerEcriture(reservationId);
                  }
                }}
                onKeyDown={(e) => {
                  if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) e.currentTarget.form?.requestSubmit();
                }}
                rows={2}
                maxLength={CONTENU_MAX}
                placeholder={`Votre message à ${interlocuteur}`}
                className={`${classeChamp} resize-y`}
              />
            </label>
            <button
              type="submit"
              disabled={enCours || !contenu.trim()}
              className={`${classeBoutonPrincipal()} shrink-0`}
              aria-label="Envoyer"
            >
              <Send size={15} />
              <span className="hidden sm:inline">{enCours ? "Envoi..." : "Envoyer"}</span>
            </button>
          </div>
          {erreurEnvoi && <MessageErreur>{erreurEnvoi}</MessageErreur>}
          {!confirmee && (
            <p className="text-xs text-gray-500">
              Pour votre sécurité, numéros de téléphone et adresses e-mail ne peuvent être échangés qu'après
              confirmation de la réservation.
            </p>
          )}
        </form>
      )}
    </div>
  );
}
