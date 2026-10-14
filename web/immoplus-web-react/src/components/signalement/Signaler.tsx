import { useState, type FormEvent } from "react";
import { CheckCircle2, Flag } from "lucide-react";
import { apiClient } from "../../api/client";
import { useAuth } from "../../context/auth";
import type { CibleSignalement, MotifSignalement } from "../../types/models";
import { MessageErreur } from "../admin/ui";
import { classeBoutonPrincipal, classeBoutonSecondaire, classeChamp, messageErreur } from "../admin/outils";
import { MOTIFS, TEXTE_SIGNALEMENT_MAX } from "./signalements";

const QUOI: Record<CibleSignalement, string> = {
  Appartement: "cette annonce",
  Utilisateur: "cet utilisateur",
  Avis: "cet avis",
  Message: "ce message",
};

/**
 * Lien discret « Signaler » qui ouvre, en ligne, un court formulaire (motif, explication, email si non connecté).
 * Le signalement part à l'équipe ImmoPlus (POST signalements).
 */
export default function Signaler({
  cible,
  cibleId,
  className = "",
}: {
  cible: CibleSignalement;
  cibleId: number;
  className?: string;
}) {
  const { estConnecte } = useAuth();
  const [ouvert, setOuvert] = useState(false);
  const [motif, setMotif] = useState<MotifSignalement | "">("");
  const [texte, setTexte] = useState("");
  const [email, setEmail] = useState("");
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);
  const [envoye, setEnvoye] = useState(false);

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    if (!motif) return setErreur("Choisissez un motif.");
    if (!texte.trim()) return setErreur("Expliquez en quelques mots ce qui pose problème.");
    if (!estConnecte && !/^\S+@\S+\.\S+$/.test(email.trim()))
      return setErreur("Indiquez votre email pour que l'équipe puisse vous répondre.");
    setEnCours(true);
    try {
      await apiClient.post("signalements", {
        cible,
        cibleId,
        motif,
        texte: texte.trim(),
        ...(estConnecte ? {} : { email: email.trim() }),
      });
      setEnvoye(true);
      setOuvert(false);
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'envoyer le signalement."));
    } finally {
      setEnCours(false);
    }
  };

  if (envoye) {
    return (
      <p className={`flex items-center gap-1.5 text-xs text-green-700 ${className}`} role="status">
        <CheckCircle2 size={13} className="shrink-0" />
        Merci : votre signalement a été transmis à l'équipe ImmoPlus.
      </p>
    );
  }

  if (!ouvert) {
    return (
      <button
        type="button"
        onClick={() => setOuvert(true)}
        className={`inline-flex items-center gap-1 text-xs text-gray-400 hover:text-red-600 underline-offset-2 hover:underline ${className}`}
      >
        <Flag size={12} />
        Signaler
      </button>
    );
  }

  return (
    <form
      onSubmit={envoyer}
      className={`flex flex-col gap-3 w-full max-w-md bg-white border border-red-100 rounded-xl p-4 text-left ${className}`}
    >
      <p className="text-sm font-semibold text-gray-800 flex items-center gap-1.5">
        <Flag size={14} className="text-red-600" />
        Signaler {QUOI[cible]}
      </p>
      <label className="text-xs font-semibold text-gray-600">
        Motif
        <select
          value={motif}
          onChange={(e) => setMotif(e.target.value as MotifSignalement)}
          className={classeChamp}
          required
        >
          <option value="">Choisir…</option>
          {MOTIFS.map((m) => (
            <option key={m.valeur} value={m.valeur}>
              {m.libelle}
            </option>
          ))}
        </select>
      </label>
      <label className="text-xs font-semibold text-gray-600">
        Explication
        <textarea
          value={texte}
          onChange={(e) => setTexte(e.target.value)}
          rows={3}
          maxLength={TEXTE_SIGNALEMENT_MAX}
          placeholder="Ce qui vous semble anormal..."
          className={classeChamp}
        />
      </label>
      {!estConnecte && (
        <label className="text-xs font-semibold text-gray-600">
          Votre email
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="email"
            className={classeChamp}
          />
        </label>
      )}
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={enCours} className={classeBoutonPrincipal(true)}>
          {enCours ? "Envoi..." : "Envoyer le signalement"}
        </button>
        <button
          type="button"
          onClick={() => {
            setOuvert(false);
            setErreur(null);
          }}
          className={classeBoutonSecondaire(true)}
        >
          Annuler
        </button>
      </div>
    </form>
  );
}
