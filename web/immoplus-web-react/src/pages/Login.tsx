import { useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { AlertCircle, ArrowLeft, Building2, MailWarning, ShieldCheck, Wallet } from "lucide-react";
import { useAuth } from "../context/auth";
import RenvoiConfirmation from "../components/compte/RenvoiConfirmation";
import ChampMotDePasse from "../components/compte/ChampMotDePasse";

export default function Login() {
  const { connexion } = useAuth();
  const navigate = useNavigate();
  // Page d'origine (ex. cœur « favori » cliqué sans être connecté) : chemin interne uniquement.
  const [parametres] = useSearchParams();
  const retourDemande = parametres.get("retour");
  const retour = retourDemande?.startsWith("/") && !retourDemande.startsWith("//") ? retourDemande : null;

  const [email, setEmail] = useState("");
  const [motDePasse, setMotDePasse] = useState("");
  const raison = parametres.get("raison");
  const [messageErreur, setMessageErreur] = useState<string | null>(
    raison === "desactive"
      ? "Votre compte a été désactivé par un administrateur. Contactez ImmoPlus pour plus d'informations."
      : raison === "session"
        ? "Votre session a expiré. Reconnectez-vous."
        : null,
  );
  const [enCours, setEnCours] = useState(false);
  // Adresse email pas encore confirmée (code « EmailNonConfirme ») : message dédié et renvoi du lien.
  const [emailNonConfirme, setEmailNonConfirme] = useState<string | null>(null);

  const seConnecter = async (e: FormEvent) => {
    e.preventDefault();
    setMessageErreur(null);
    setEmailNonConfirme(null);
    setEnCours(true);
    const { succes, erreur, role, code } = await connexion(email, motDePasse);
    setEnCours(false);
    if (succes) {
      navigate(role === "Admin" ? "/admin" : (retour ?? "/"));
    } else if (code === "EmailNonConfirme") {
      setEmailNonConfirme(erreur ?? "Confirmez d'abord votre adresse email : un lien vous a été envoyé.");
    } else {
      setMessageErreur(erreur ?? "Email ou mot de passe incorrect.");
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
                <ShieldCheck size={16} className="text-immo-gold" />
              </div>
              <div>
                <p className="font-semibold text-sm">Deux parcours</p>
                <p className="text-gray-500 text-sm">
                  Location longue durée avec dossier, ou séjour court sans dossier.
                </p>
              </div>
            </div>

            <div className="hidden md:flex gap-3">
              <div className="w-9 h-9 rounded-lg bg-white/5 flex items-center justify-center flex-shrink-0">
                <Wallet size={16} className="text-immo-gold" />
              </div>
              <div>
                <p className="font-semibold text-sm">Financement disponible</p>
                <p className="text-gray-500 text-sm">Avance de loyer remboursable en mensualités.</p>
              </div>
            </div>
          </div>

          <div className="p-6 sm:p-10 md:w-1/2 flex flex-col justify-center bg-white">
            <h2 className="text-2xl font-extrabold mb-1">Bon retour</h2>
            <p className="text-gray-500 text-sm mb-6">Connectez-vous à votre espace personnel</p>

            <form onSubmit={seConnecter} className="flex flex-col gap-4">
              <div>
                <label htmlFor="login-email" className="text-sm font-medium text-gray-700">
                  Adresse email
                </label>
                <input
                  id="login-email"
                  type="email"
                  autoComplete="email"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="jean@example.com"
                  className="w-full border border-gray-300 rounded-lg px-4 py-3 mt-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-immo-gold/40 focus:border-immo-gold transition-shadow"
                />
              </div>

              <div>
                <div className="flex items-center justify-between">
                  <label htmlFor="login-mot-de-passe" className="text-sm font-medium text-gray-700">
                    Mot de passe
                  </label>
                  <Link to="/mot-de-passe-oublie" className="text-xs font-medium text-immo-gold hover:underline">
                    Mot de passe oublié ?
                  </Link>
                </div>
                <ChampMotDePasse
                  id="login-mot-de-passe"
                  autoComplete="current-password"
                  required
                  value={motDePasse}
                  onChange={(e) => setMotDePasse(e.target.value)}
                  className="w-full border border-gray-300 rounded-lg px-4 py-3 mt-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-immo-gold/40 focus:border-immo-gold transition-shadow"
                />
              </div>

              <button
                type="submit"
                disabled={enCours}
                className="w-full bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors mt-2 disabled:opacity-60"
              >
                {enCours ? "Connexion..." : "Se connecter"}
              </button>
            </form>

            {emailNonConfirme && (
              <div role="alert" className="flex flex-col gap-3 rounded-lg bg-amber-50 px-4 py-3 mt-4">
                <p className="flex items-start gap-2 text-amber-900 text-sm">
                  <MailWarning size={16} className="shrink-0 mt-0.5" />
                  <span>
                    {emailNonConfirme} Pensez à vérifier vos courriers indésirables ; le lien est valable 48 heures.
                  </span>
                </p>
                <RenvoiConfirmation email={email} libelle="Renvoyer l'email de confirmation" />
              </div>
            )}

            {messageErreur && (
              <p className="flex items-center gap-2 text-red-600 text-sm mt-4">
                <AlertCircle size={16} className="shrink-0" />
                {messageErreur}
              </p>
            )}

            <div className="border-t mt-6 pt-4 text-center text-sm text-gray-600">
              Pas encore de compte ?{" "}
              <Link to="/inscription" className="text-immo-gold font-semibold">
                Créer un compte
              </Link>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
