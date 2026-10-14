import { useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { AlertCircle, ArrowLeft, Building2, CheckCircle2 } from "lucide-react";
import { apiClient, ApiError } from "../api/client";

const classeChamp =
  "w-full border border-gray-300 rounded-lg px-4 py-3 mt-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-immo-gold/40 focus:border-immo-gold transition-shadow";

export default function ReinitialiserMotDePasse() {
  const [params] = useSearchParams();
  const jeton = params.get("jeton");

  const [motDePasse, setMotDePasse] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [messageErreur, setMessageErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);
  const [termine, setTermine] = useState(false);

  const reinitialiser = async (e: FormEvent) => {
    e.preventDefault();
    setMessageErreur(null);
    if (motDePasse.length < 6) {
      setMessageErreur("Le mot de passe doit contenir au moins 6 caractères.");
      return;
    }
    if (motDePasse !== confirmation) {
      setMessageErreur("Les mots de passe ne correspondent pas.");
      return;
    }

    setEnCours(true);
    try {
      await apiClient.post("utilisateur/reinitialiser-mot-de-passe", { jeton, nouveauMotDePasse: motDePasse });
      setTermine(true);
    } catch (err) {
      setMessageErreur(err instanceof ApiError ? err.message : "Impossible de modifier le mot de passe.");
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

          {!jeton ? (
            <div className="flex flex-col gap-4">
              <h2 className="text-2xl font-extrabold">Lien invalide</h2>
              <p className="text-gray-500 text-sm">
                Ce lien de réinitialisation est incomplet. Faites une nouvelle demande.
              </p>
              <Link to="/mot-de-passe-oublie" className="text-immo-gold font-semibold text-sm">
                Demander un nouveau lien
              </Link>
            </div>
          ) : termine ? (
            <div className="flex flex-col gap-4">
              <span className="flex items-center gap-2 text-green-600 font-semibold">
                <CheckCircle2 size={20} />
                Mot de passe modifié
              </span>
              <p className="text-gray-500 text-sm">
                Vous pouvez maintenant vous connecter avec votre nouveau mot de passe.
              </p>
              <Link
                to="/login"
                className="block w-full text-center bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors mt-2"
              >
                Se connecter
              </Link>
            </div>
          ) : (
            <>
              <h2 className="text-2xl font-extrabold mb-1">Nouveau mot de passe</h2>
              <p className="text-gray-500 text-sm mb-6">Choisissez un mot de passe d'au moins 6 caractères.</p>

              <form onSubmit={reinitialiser} className="flex flex-col gap-4">
                <div>
                  <label htmlFor="reinit-mot-de-passe" className="text-sm font-medium text-gray-700">
                    Nouveau mot de passe
                  </label>
                  <input
                    id="reinit-mot-de-passe"
                    type="password"
                    value={motDePasse}
                    onChange={(e) => setMotDePasse(e.target.value)}
                    autoComplete="new-password"
                    className={classeChamp}
                  />
                </div>
                <div>
                  <label htmlFor="reinit-confirmation" className="text-sm font-medium text-gray-700">
                    Confirmation
                  </label>
                  <input
                    id="reinit-confirmation"
                    type="password"
                    value={confirmation}
                    onChange={(e) => setConfirmation(e.target.value)}
                    autoComplete="new-password"
                    className={classeChamp}
                  />
                </div>

                <button
                  type="submit"
                  disabled={enCours}
                  className="w-full bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors mt-2 disabled:opacity-60"
                >
                  {enCours ? "Enregistrement..." : "Changer le mot de passe"}
                </button>
              </form>

              {messageErreur && (
                <div className="flex flex-col gap-2 mt-4">
                  <p className="flex items-center gap-2 text-red-600 text-sm">
                    <AlertCircle size={16} className="shrink-0" />
                    {messageErreur}
                  </p>
                  <Link to="/mot-de-passe-oublie" className="text-immo-gold font-semibold text-sm">
                    Demander un nouveau lien
                  </Link>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
