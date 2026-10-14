import { useState, type FormEvent } from "react";
import { CalendarClock, CalendarPlus, Check, UserX, X } from "lucide-react";
import { apiClient } from "../../api/client";
import {
  classeBoutonDanger,
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  messageErreur,
} from "../admin/outils";
import type { CandidatureDto } from "../../types/models";
import { formaterDateHeure, libellesStatutVisite, VISITE_ACTIVE } from "./analyse";

const PRESELECTIONS_MAX = 3;

/** Valeur pour <input type="datetime-local"> : demain à 10h par défaut. */
const demainDixHeures = () => {
  const d = new Date();
  d.setDate(d.getDate() + 1);
  d.setHours(10, 0, 0, 0);
  return `${d.toISOString().slice(0, 10)}T10:00`;
};

/**
 * Côté bailleur/admin : présélection du candidat pour une visite (3 maximum par logement), suivi de la réponse,
 * puis compte rendu une fois la visite passée. La décision finale n'est possible qu'après une visite effectuée.
 */
export default function VisiteCandidature({
  candidature,
  logementAttribue,
  nbPreselectionnes,
  onMiseAJour,
}: {
  candidature: CandidatureDto;
  logementAttribue: boolean;
  nbPreselectionnes: number;
  onMiseAJour: (c: CandidatureDto) => void;
}) {
  const visites = candidature.visites ?? [];
  const active = visites.find((v) => VISITE_ACTIVE.includes(v.statut));
  const effectuee = visites.find((v) => v.statut === "Effectuee");
  const decidable = ["EnAttente", "ListeAttente", "Preselectionnee"].includes(candidature.statut);

  const [maintenant] = useState(() => Date.now());
  const [formulaire, setFormulaire] = useState(false);
  const [dateHeure, setDateHeure] = useState(active ? active.dateHeure.slice(0, 16) : demainDixHeures());
  const [message, setMessage] = useState(active?.message ?? "");
  const [notes, setNotes] = useState("");
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);

  const appeler = async (chemin: string, corps?: unknown) => {
    setErreur(null);
    setEnCours(true);
    try {
      onMiseAJour(await apiClient.post<CandidatureDto>(chemin, corps));
      setFormulaire(false);
    } catch (err) {
      setErreur(messageErreur(err, "L'action a échoué."));
    } finally {
      setEnCours(false);
    }
  };

  const programmer = (e: FormEvent) => {
    e.preventDefault();
    appeler(`candidature/${candidature.id}/visite`, { dateHeure, message: message.trim() || null });
  };

  if (!decidable && visites.length === 0) return null;

  // Une visite est « passée » dès le jour J : le bailleur peut alors la clôturer.
  const finDuJour = new Date(maintenant);
  finDuJour.setHours(23, 59, 59, 999);
  const visitePassee = !!active && new Date(active.dateHeure) <= finDuJour;
  const placesPrises = candidature.statut !== "Preselectionnee" && nbPreselectionnes >= PRESELECTIONS_MAX;

  return (
    <div className="lg:col-span-3 bg-white rounded-xl border border-black/5 p-4 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="font-semibold flex items-center gap-2">
          <CalendarClock size={16} className="text-immo-gold" />
          Visite du logement
          <span className="text-xs font-normal text-gray-500">
            ({nbPreselectionnes}/{PRESELECTIONS_MAX} candidats retenus pour ce logement)
          </span>
        </p>
        {decidable && !logementAttribue && !active && !effectuee && !formulaire && !placesPrises && (
          <button onClick={() => setFormulaire(true)} className={classeBoutonPrincipal(true)}>
            <CalendarPlus size={14} />
            {visites.some((v) => v.statut === "Absent") ? "Proposer une nouvelle visite" : "Retenir pour une visite"}
          </button>
        )}
      </div>

      {decidable && !logementAttribue && !active && placesPrises && (
        <p className="mt-3 text-gray-500">
          {PRESELECTIONS_MAX} candidats sont déjà retenus pour une visite. Refusez-en un ou annulez sa visite pour
          libérer une place.
        </p>
      )}
      {decidable && !effectuee && !active && !formulaire && !placesPrises && !logementAttribue && (
        <p className="mt-2 text-gray-500">La décision (accepter ce dossier) se prend après une visite du logement.</p>
      )}

      {active && !formulaire && (
        <div className="mt-3 border border-black/5 rounded-lg p-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p>
              Visite prévue le <strong>{formaterDateHeure(active.dateHeure)}</strong>
            </p>
            <span
              className={`text-xs font-semibold px-2.5 py-1 rounded-full ${libellesStatutVisite[active.statut].classe}`}
            >
              {libellesStatutVisite[active.statut].label}
            </span>
          </div>
          {active.message && <p className="text-gray-500 mt-1">Votre message : « {active.message} »</p>}
          {active.reponseLocataire && (
            <p className="text-gray-700 mt-1">Réponse du candidat : « {active.reponseLocataire} »</p>
          )}

          {visitePassee && active.statut !== "Indisponible" ? (
            <div className="mt-3 space-y-2">
              <label className="block font-medium text-gray-700">
                Vos notes sur la visite (privées, jamais montrées au candidat)
                <textarea
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  rows={2}
                  maxLength={1000}
                  placeholder="Impression générale, questions posées, ponctualité…"
                  className={classeChamp}
                />
              </label>
              <div className="flex flex-wrap gap-2">
                <button
                  onClick={() =>
                    appeler(`visite/${active.id}/cloturer`, { effectuee: true, notes: notes.trim() || null })
                  }
                  disabled={enCours}
                  className={classeBoutonPrincipal(true)}
                >
                  <Check size={14} />
                  Visite effectuée
                </button>
                <button
                  onClick={() =>
                    appeler(`visite/${active.id}/cloturer`, { effectuee: false, notes: notes.trim() || null })
                  }
                  disabled={enCours}
                  className={classeBoutonSecondaire(true)}
                >
                  <UserX size={14} />
                  Le candidat était absent
                </button>
              </div>
            </div>
          ) : (
            <div className="mt-3 flex flex-wrap gap-2">
              <button onClick={() => setFormulaire(true)} className={classeBoutonSecondaire(true)}>
                <CalendarClock size={14} />
                Reprogrammer
              </button>
              <button
                onClick={() =>
                  window.confirm(
                    "Annuler la visite ? Le candidat sera prévenu et son dossier repassera « à étudier ».",
                  ) && appeler(`candidature/${candidature.id}/annuler-visite`)
                }
                disabled={enCours}
                className={classeBoutonDanger(true)}
              >
                <X size={14} />
                Annuler la visite
              </button>
            </div>
          )}
        </div>
      )}

      {formulaire && (
        <form onSubmit={programmer} className="mt-3 grid grid-cols-1 md:grid-cols-2 gap-3">
          <label className="font-medium text-gray-700">
            Date et heure de la visite
            <input
              type="datetime-local"
              value={dateHeure}
              onChange={(e) => setDateHeure(e.target.value)}
              className={classeChamp}
              required
            />
          </label>
          <label className="font-medium text-gray-700">
            Message au candidat (optionnel)
            <input
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              maxLength={300}
              placeholder="Ex. : rendez-vous devant l'immeuble, appelez-moi en arrivant."
              className={classeChamp}
            />
          </label>
          <div className="md:col-span-2 flex gap-2">
            <button type="submit" disabled={enCours} className={classeBoutonPrincipal(true)}>
              <CalendarPlus size={14} />
              {enCours ? "Envoi..." : active ? "Envoyer la nouvelle date" : "Inviter à la visite"}
            </button>
            <button type="button" onClick={() => setFormulaire(false)} className={classeBoutonSecondaire(true)}>
              Annuler
            </button>
          </div>
        </form>
      )}

      {visites.filter((v) => !VISITE_ACTIVE.includes(v.statut)).length > 0 && (
        <ul className="mt-3 space-y-1">
          {visites
            .filter((v) => !VISITE_ACTIVE.includes(v.statut))
            .map((v) => (
              <li key={v.id} className="text-gray-600">
                <span
                  className={`text-xs font-semibold px-2 py-0.5 rounded-full mr-2 ${libellesStatutVisite[v.statut].classe}`}
                >
                  {libellesStatutVisite[v.statut].label}
                </span>
                {formaterDateHeure(v.dateHeure)}
                {v.notesBailleur && (
                  <span className="block text-gray-500 italic mt-0.5">Vos notes : « {v.notesBailleur} »</span>
                )}
              </li>
            ))}
        </ul>
      )}

      {erreur && <p className="text-red-600 mt-3">{erreur}</p>}
    </div>
  );
}
