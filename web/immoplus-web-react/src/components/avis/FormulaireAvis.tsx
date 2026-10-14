import { useState, type FormEvent } from "react";
import { Send } from "lucide-react";
import { apiClient } from "../../api/client";
import type { AvisDto } from "../../types/models";
import { MessageErreur } from "../admin/ui";
import { classeBoutonPrincipal, classeBoutonSecondaire, classeChamp, messageErreur } from "../admin/outils";
import { formaterJour } from "../sejour/dates";
import { SaisieEtoiles } from "./Etoiles";
import { COMMENTAIRE_MAX, COMMENTAIRE_MIN, CRITERES_AVIS } from "./notes";

type SousNotes = Partial<Record<(typeof CRITERES_AVIS)[number]["cle"], number>>;

/**
 * Avis après un séjour terminé (ou une location longue durée) : le voyageur / locataire note le logement
 * (avec des sous-notes facultatives), l'hôte / bailleur note le voyageur / locataire. Un seul avis par sens,
 * publié tel quel.
 */
export default function FormulaireAvis({
  reservationId,
  locationId,
  pourLogement,
  cible,
  dateLimite,
  onEnvoye,
  onAnnuler,
}: {
  /** Séjour court évalué… */
  reservationId?: number;
  /** …ou location longue durée évaluée. */
  locationId?: number;
  /** true : avis du voyageur sur le logement ; false : avis de l'hôte sur le voyageur. */
  pourLogement: boolean;
  cible: string;
  dateLimite?: string | null;
  onEnvoye: (avis: AvisDto | undefined) => void;
  onAnnuler: () => void;
}) {
  const [note, setNote] = useState<number | null>(null);
  const [sousNotes, setSousNotes] = useState<SousNotes>({});
  const [commentaire, setCommentaire] = useState("");
  const [erreur, setErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);
  const longueur = commentaire.trim().length;
  const longueDuree = locationId != null;

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    if (!note) {
      setErreur("Choisissez une note globale de 1 à 5 étoiles.");
      return;
    }
    if (longueur < COMMENTAIRE_MIN) {
      setErreur(`Votre commentaire doit faire au moins ${COMMENTAIRE_MIN} caractères.`);
      return;
    }
    setEnCours(true);
    try {
      const chemin = locationId != null ? `location/${locationId}/avis` : `reservation/${reservationId}/avis`;
      const avis = await apiClient.post<AvisDto>(chemin, {
        note,
        ...(pourLogement ? sousNotes : {}),
        commentaire: commentaire.trim(),
      });
      onEnvoye(avis);
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'envoyer votre avis."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <form onSubmit={envoyer} className="flex flex-col gap-4 bg-immo-cream/60 rounded-xl p-4">
      <div>
        <p className="font-bold">
          {pourLogement
            ? longueDuree
              ? "Votre avis sur le logement"
              : "Votre avis sur le séjour"
            : `Évaluer ${cible}`}
        </p>
        <p className="text-xs text-gray-500">
          {pourLogement
            ? `Il sera publié sur l'annonce avec votre prénom ; ${longueDuree ? "le bailleur" : "l'hôte"} pourra y répondre.`
            : longueDuree
              ? "Votre avis aide les autres bailleurs et hôtes ; il est visible par ceux à qui ce locataire fera une demande."
              : "Votre avis aide les autres hôtes ; il est visible par les hôtes à qui ce voyageur fera une demande."}
          {dateLimite ? ` Possible jusqu'au ${formaterJour(dateLimite)}.` : ""}
        </p>
      </div>

      <div>
        <p className="text-sm font-medium text-gray-700 mb-1">Note globale</p>
        <SaisieEtoiles valeur={note} onChange={setNote} libelle="Note globale" />
      </div>

      {pourLogement && (
        <div>
          <p className="text-sm font-medium text-gray-700">
            Détail <span className="text-gray-400 font-normal">(facultatif)</span>
          </p>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-1 mt-1">
            {CRITERES_AVIS.map(({ cle, libelle }) => (
              <div key={cle} className="flex items-center justify-between gap-3">
                <span className="text-sm text-gray-600">{libelle}</span>
                <SaisieEtoiles
                  valeur={sousNotes[cle] ?? null}
                  onChange={(n) => setSousNotes((s) => ({ ...s, [cle]: n }))}
                  libelle={libelle}
                  taille={18}
                />
              </div>
            ))}
          </div>
        </div>
      )}

      <label className="text-sm font-medium text-gray-700">
        Commentaire
        <textarea
          value={commentaire}
          onChange={(e) => {
            setCommentaire(e.target.value);
            setErreur(null);
          }}
          rows={4}
          maxLength={COMMENTAIRE_MAX}
          placeholder={
            pourLogement
              ? "Ce que vous avez aimé, ce qui pourrait être amélioré..."
              : "Respect du logement et des règles, communication, ponctualité..."
          }
          className={classeChamp}
        />
        <span
          className={`block text-xs font-normal mt-1 ${longueur < COMMENTAIRE_MIN ? "text-gray-400" : "text-gray-500"}`}
        >
          {longueur} / {COMMENTAIRE_MAX} caractères (minimum {COMMENTAIRE_MIN})
        </span>
      </label>

      {erreur && <MessageErreur>{erreur}</MessageErreur>}

      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={enCours} className={classeBoutonPrincipal(true)}>
          <Send size={14} />
          {enCours ? "Envoi..." : "Publier l'avis"}
        </button>
        <button type="button" onClick={onAnnuler} className={classeBoutonSecondaire(true)}>
          Plus tard
        </button>
      </div>
    </form>
  );
}
