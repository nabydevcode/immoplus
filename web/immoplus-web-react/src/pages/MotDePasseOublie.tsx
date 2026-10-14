import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { AlertCircle, ArrowLeft, Building2, MailCheck } from "lucide-react";
import { apiClient, ApiError } from "../api/client";

export default function MotDePasseOublie() {
  const [email, setEmail] = useState("");
  const [messageErreur, setMessageErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);
  const [envoye, setEnvoye] = useState(false);

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setMessageErreur(null);
    if (!email.trim()) {
      setMessageErreur("Veuillez saisir votre adresse email.");
      return;
    }

    setEnCours(true);
    try {
      await apiClient.post("utilisateur/mot-de-passe-oublie", { email: email.trim() });
      setEnvoye(true);
    } catch (err) {
      setMessageErreur(err instanceof ApiError ? err.message : "Impossible d'envoyer la demande. Réessayez plus tard.");
    } finally {
      setEnCours(false);
    }
  };

  return (
    <div className="min-h-screen bg-immo-cream flex flex-col">
      <div className="px-6 py-4">
        <Link
          to="/login"
          className="inline-flex items-center gap-2 text-gray-600 text-sm font-medium hover:text-immo-dark transition-colors"
        >
          <ArrowLeft size={16} />
          Retour à la connexion
        </Link>
      </div>

      <div className="flex-1 flex items-center justify-center px-6 py-8">
        <div className="max-w-md w-full bg-white rounded-2xl shadow-xl p-10">
          <span className="flex items-center justify-center w-11 h-11 rounded-lg bg-immo-gold/15 text-immo-gold mb-6">
            <Building2 size={20} />
          </span>

          {envoye ? (
            <div className="flex flex-col gap-4">
              <span className="flex items-center gap-2 text-green-600 font-semibold">
                <MailCheck size={20} />
                Demande envoyée
              </span>
              <p className="text-gray-500 text-sm">
                Si un compte existe pour <span className="font-semibold text-gray-700">{email.trim()}</span>, un email
                contenant un lien de réinitialisation vient de lui être envoyé. Ce lien est valable 1 heure ; pensez à
                vérifier vos courriers indésirables.
              </p>
              <Link
                to="/login"
                className="block w-full text-center bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors mt-2"
              >
                Retour à la connexion
              </Link>
            </div>
          ) : (
            <>
              <h2 className="text-2xl font-extrabold mb-1">Mot de passe oublié</h2>
              <p className="text-gray-500 text-sm mb-6">
                Saisissez votre email, nous vous enverrons un lien pour choisir un nouveau mot de passe.
              </p>

              <form onSubmit={envoyer} className="flex flex-col gap-4">
                <div>
                  <label htmlFor="oubli-email" className="text-sm font-medium text-gray-700">
                    Adresse email
                  </label>
                  <input
                    id="oubli-email"
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="jean@example.com"
                    autoComplete="email"
                    className="w-full border border-gray-300 rounded-lg px-4 py-3 mt-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-immo-gold/40 focus:border-immo-gold transition-shadow"
                  />
                </div>

                <button
                  type="submit"
                  disabled={enCours}
                  className="w-full bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors mt-2 disabled:opacity-60"
                >
                  {enCours ? "Envoi..." : "Envoyer le lien"}
                </button>
              </form>

              {messageErreur && (
                <p className="flex items-center gap-2 text-red-600 text-sm mt-4">
                  <AlertCircle size={16} className="shrink-0" />
                  {messageErreur}
                </p>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
