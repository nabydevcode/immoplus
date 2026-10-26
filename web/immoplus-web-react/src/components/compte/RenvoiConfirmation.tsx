import { useEffect, useState } from "react";
import { AlertCircle, MailCheck, Send } from "lucide-react";
import { apiClient, ApiError } from "../../api/client";

/** Délai avant un nouveau renvoi (anti double clic ; l'API limite aussi à 3 envois par heure). */
const DELAI_SECONDES = 60;

interface Props {
  /** Adresse à laquelle renvoyer l'email ; vide : le bouton reste inactif. */
  email: string;
  libelle?: string;
}

/** Bouton « Renvoyer l'email de confirmation » : réponse neutre de l'API, puis attente avant un nouvel envoi. */
export default function RenvoiConfirmation({ email, libelle = "Renvoyer l'email" }: Props) {
  const [enCours, setEnCours] = useState(false);
  const [attente, setAttente] = useState(0);
  const [resultat, setResultat] = useState<{ ok: boolean; texte: string } | null>(null);

  useEffect(() => {
    if (attente <= 0) return;
    const minuterie = window.setTimeout(() => setAttente((s) => s - 1), 1000);
    return () => window.clearTimeout(minuterie);
  }, [attente]);

  const renvoyer = async () => {
    const adresse = email.trim();
    if (!adresse || enCours || attente > 0) return;
    setEnCours(true);
    setResultat(null);
    try {
      await apiClient.post("utilisateur/renvoyer-confirmation", { email: adresse });
      setResultat({
        ok: true,
        texte: `Si un compte en attente de confirmation existe pour ${adresse}, un nouvel email vient de lui être envoyé.`,
      });
      setAttente(DELAI_SECONDES);
    } catch (err) {
      setResultat({
        ok: false,
        texte:
          err instanceof ApiError && err.statut === 429
            ? "Trop d'envois en peu de temps. Patientez avant de redemander un email."
            : err instanceof ApiError && !err.message.startsWith("Erreur ")
              ? err.message
              : "Impossible d'envoyer l'email pour le moment. Réessayez plus tard.",
      });
    } finally {
      setEnCours(false);
    }
  };

  return (
    <div className="flex flex-col gap-2 w-full">
      <button
        type="button"
        onClick={renvoyer}
        disabled={!email.trim() || enCours || attente > 0}
        className="w-full inline-flex items-center justify-center gap-2 bg-white border border-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold/10 transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
      >
        <Send size={16} />
        {enCours ? "Envoi..." : attente > 0 ? `${libelle} (${attente} s)` : libelle}
      </button>
      {resultat && (
        <p
          role="status"
          className={`flex items-start gap-2 text-sm ${resultat.ok ? "text-green-700" : "text-red-600"}`}
        >
          {resultat.ok ? (
            <MailCheck size={16} className="shrink-0 mt-0.5" />
          ) : (
            <AlertCircle size={16} className="shrink-0 mt-0.5" />
          )}
          {resultat.texte}
        </p>
      )}
    </div>
  );
}
