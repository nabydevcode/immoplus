import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { AlertCircle, ArrowLeft, Building2, CheckCircle2, Clock, Home, KeyRound, UserCheck } from "lucide-react";
import { apiClient, ApiError } from "../api/client";

type Role = "Locataire" | "Bailleur";

const roles: { valeur: Role; label: string; description: string; icone: typeof Home }[] = [
  { valeur: "Locataire", label: "Locataire", description: "Je cherche un logement", icone: KeyRound },
  { valeur: "Bailleur", label: "Bailleur", description: "Je propose des biens", icone: Home },
];

const classeChamp =
  "w-full border border-gray-300 rounded-lg px-4 py-3 mt-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-immo-gold/40 focus:border-immo-gold transition-shadow";

export default function Inscription() {
  const [nom, setNom] = useState("");
  const [telephone, setTelephone] = useState("");
  const [email, setEmail] = useState("");
  const [motDePasse, setMotDePasse] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [role, setRole] = useState<Role | null>(null);
  const [messageErreur, setMessageErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);
  const [inscrit, setInscrit] = useState(false);

  const sInscrire = async (e: FormEvent) => {
    e.preventDefault();
    setMessageErreur(null);

    if (!nom.trim() || !telephone.trim() || !email.trim() || !motDePasse) {
      setMessageErreur("Veuillez remplir tous les champs.");
      return;
    }
    if (!role) {
      setMessageErreur("Veuillez choisir un profil.");
      return;
    }
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
      await apiClient.post("utilisateur/inscription", {
        nom: nom.trim(),
        telephone: telephone.trim(),
        email: email.trim(),
        motDePasse,
        role,
      });
      setInscrit(true);
    } catch (err) {
      setMessageErreur(err instanceof ApiError ? err.message : "Impossible de créer le compte. Réessayez plus tard.");
    } finally {
      setEnCours(false);
    }
  };

  return (
    <div className="min-h-screen bg-immo-cream flex flex-col">
      <div className="px-6 py-4">
        <Link
          to="/"
          className="inline-flex items-center gap-2 text-gray-600 text-sm font-medium hover:text-immo-dark transition-colors"
        >
          <ArrowLeft size={16} />
          Retour à l'accueil
        </Link>
      </div>

      <div className="flex-1 flex items-center justify-center px-6 py-8">
        <div className="flex flex-col md:flex-row max-w-4xl w-full rounded-2xl overflow-hidden shadow-xl">
          <div className="bg-immo-dark text-immo-cream px-6 py-5 md:p-10 md:w-1/2 flex flex-col justify-center gap-8">
            <div className="flex items-center gap-2">
              <span className="flex items-center justify-center w-9 h-9 rounded-lg bg-immo-gold/15 text-immo-gold">
                <Building2 size={18} />
              </span>
              <div>
                <h2 className="text-immo-gold text-xl font-extrabold leading-none">ImmoPlus</h2>
                <p className="text-gray-500 text-xs mt-1">La plateforme immobilière de référence</p>
              </div>
            </div>

            <div className="hidden md:flex gap-3">
              <div className="w-9 h-9 rounded-lg bg-white/5 flex items-center justify-center flex-shrink-0">
                <UserCheck size={16} className="text-immo-gold" />
              </div>
              <div>
                <p className="font-semibold text-sm">Un compte, deux profils</p>
                <p className="text-gray-500 text-sm">
                  Locataire pour trouver un logement, Bailleur pour publier vos biens.
                </p>
              </div>
            </div>

            <div className="hidden md:flex gap-3">
              <div className="w-9 h-9 rounded-lg bg-white/5 flex items-center justify-center flex-shrink-0">
                <Clock size={16} className="text-immo-gold" />
              </div>
              <div>
                <p className="font-semibold text-sm">Validation rapide</p>
                <p className="text-gray-500 text-sm">
                  Chaque compte est vérifié par un administrateur avant la première connexion.
                </p>
              </div>
            </div>
          </div>

          <div className="p-6 sm:p-10 md:w-1/2 flex flex-col justify-center bg-white">
            {inscrit ? (
              <div className="flex flex-col items-center text-center gap-4">
                <span className="flex items-center justify-center w-14 h-14 rounded-full bg-green-50 text-green-600">
                  <CheckCircle2 size={28} />
                </span>
                <h2 className="text-2xl font-extrabold">Inscription envoyée</h2>
                <p className="text-gray-500 text-sm">
                  Votre compte a bien été créé. Il doit être validé par un administrateur avant que vous puissiez vous
                  connecter.
                </p>
                <Link
                  to="/login"
                  className="block w-full text-center bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors mt-2"
                >
                  Aller à la connexion
                </Link>
              </div>
            ) : (
              <>
                <h2 className="text-2xl font-extrabold mb-1">Créer un compte</h2>
                <p className="text-gray-500 text-sm mb-6">Rejoignez ImmoPlus en quelques secondes</p>

                <form onSubmit={sInscrire} className="flex flex-col gap-4">
                  <div>
                    <p id="inscription-role" className="text-sm font-medium text-gray-700">
                      Je suis
                    </p>
                    <div role="group" aria-labelledby="inscription-role" className="grid grid-cols-2 gap-3 mt-1.5">
                      {roles.map(({ valeur, label, description, icone: Icone }) => (
                        <button
                          key={valeur}
                          type="button"
                          onClick={() => setRole(valeur)}
                          aria-pressed={role === valeur}
                          className={`flex flex-col items-start gap-1 rounded-lg border px-4 py-3 text-left transition-colors ${
                            role === valeur
                              ? "border-immo-gold bg-immo-gold/10"
                              : "border-gray-300 hover:border-immo-gold/60"
                          }`}
                        >
                          <Icone size={18} className="text-immo-gold" />
                          <span className="text-sm font-semibold">{label}</span>
                          <span className="text-xs text-gray-500">{description}</span>
                        </button>
                      ))}
                    </div>
                  </div>

                  <div>
                    <label htmlFor="inscription-nom" className="text-sm font-medium text-gray-700">
                      Nom complet
                    </label>
                    <input
                      id="inscription-nom"
                      value={nom}
                      onChange={(e) => setNom(e.target.value)}
                      placeholder="Mamadou Diallo"
                      autoComplete="name"
                      className={classeChamp}
                    />
                  </div>

                  <div>
                    <label htmlFor="inscription-telephone" className="text-sm font-medium text-gray-700">
                      Téléphone
                    </label>
                    <input
                      id="inscription-telephone"
                      type="tel"
                      value={telephone}
                      onChange={(e) => setTelephone(e.target.value)}
                      placeholder="+224 6 00 00 00 00"
                      autoComplete="tel"
                      className={classeChamp}
                    />
                  </div>

                  <div>
                    <label htmlFor="inscription-email" className="text-sm font-medium text-gray-700">
                      Adresse email
                    </label>
                    <input
                      id="inscription-email"
                      type="email"
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      placeholder="jean@example.com"
                      autoComplete="email"
                      className={classeChamp}
                    />
                  </div>

                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div>
                      <label htmlFor="inscription-mot-de-passe" className="text-sm font-medium text-gray-700">
                        Mot de passe
                      </label>
                      <input
                        id="inscription-mot-de-passe"
                        type="password"
                        value={motDePasse}
                        onChange={(e) => setMotDePasse(e.target.value)}
                        autoComplete="new-password"
                        className={classeChamp}
                      />
                    </div>
                    <div>
                      <label htmlFor="inscription-confirmation" className="text-sm font-medium text-gray-700">
                        Confirmation
                      </label>
                      <input
                        id="inscription-confirmation"
                        type="password"
                        value={confirmation}
                        onChange={(e) => setConfirmation(e.target.value)}
                        autoComplete="new-password"
                        className={classeChamp}
                      />
                    </div>
                  </div>

                  <button
                    type="submit"
                    disabled={enCours}
                    className="w-full bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors mt-2 disabled:opacity-60"
                  >
                    {enCours ? "Création..." : "Créer mon compte"}
                  </button>
                </form>

                {messageErreur && (
                  <p className="flex items-center gap-2 text-red-600 text-sm mt-4">
                    <AlertCircle size={16} className="shrink-0" />
                    {messageErreur}
                  </p>
                )}

                <div className="border-t mt-6 pt-4 text-center text-sm text-gray-600">
                  Déjà un compte ?{" "}
                  <Link to="/login" className="text-immo-gold font-semibold">
                    Se connecter
                  </Link>
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
