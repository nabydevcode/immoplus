import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { AlertCircle, ArrowLeft, Building2, CheckCircle2, Loader2 } from "lucide-react";
import { ApiError } from "../api/client";
import RenvoiConfirmation from "../components/compte/RenvoiConfirmation";
import { confirmerEmailUneFois } from "../components/compte/confirmation";
import { publierCompte } from "../components/compte/diffusion";

type Etat = { type: "chargement" } | { type: "succes"; message: string } | { type: "erreur"; message: string };

export default function ConfirmerEmail() {
  const [parametres] = useSearchParams();
  const jeton = parametres.get("jeton")?.trim() ?? "";
  const [etat, setEtat] = useState<Etat>({ type: "chargement" });
  const [email, setEmail] = useState("");

  useEffect(() => {
    if (!jeton) return;
    let annule = false;
    confirmerEmailUneFois(jeton)
      .then((r) => {
        // Confirmée (ou déjà confirmée) : l'écran « Vérifiez votre boîte email » d'un autre onglet se met à jour.
        if (!annule) publierCompte({ type: "emailConfirme", email: r?.email, statut: r?.statut });
        if (!annule)
          setEtat({
            type: "succes",
            message:
              r?.message ??
              "Adresse confirmée. Votre compte doit maintenant être validé par un administrateur ; vous recevrez un email.",
          });
      })
      .catch((err) => {
        if (!annule)
          setEtat({
            type: "erreur",
            message:
              err instanceof ApiError && !err.message.startsWith("Erreur ")
                ? err.message
                : "Impossible de confirmer votre adresse pour le moment. Réessayez plus tard.",
          });
      });
    return () => {
      annule = true;
    };
  }, [jeton]);

  // Lien incomplet : pas d'appel à l'API.
  const affiche: Etat = jeton
    ? etat
    : { type: "erreur", message: "Ce lien de confirmation est incomplet. Demandez un nouvel email ci-dessous." };

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

      <div className="flex-1 flex items-center justify-center px-4 sm:px-6 py-8">
        <div className="max-w-md w-full bg-white rounded-2xl shadow-xl p-6 sm:p-10">
          <span className="flex items-center justify-center w-11 h-11 rounded-lg bg-immo-gold/15 text-immo-gold mb-6">
            <Building2 size={20} />
          </span>
          <h1 className="text-2xl font-extrabold mb-4">Confirmation de votre adresse email</h1>

          {affiche.type === "chargement" && (
            <p role="status" className="flex items-center gap-2 text-gray-600 text-sm">
              <Loader2 size={16} className="animate-spin" />
              Confirmation en cours...
            </p>
          )}

          {affiche.type === "succes" && (
            <div className="flex flex-col gap-4">
              <p role="status" className="flex items-start gap-2 text-green-700 font-semibold">
                <CheckCircle2 size={20} className="shrink-0 mt-0.5" />
                Adresse confirmée
              </p>
              <p className="text-gray-600 text-sm">{affiche.message}</p>
              <Link
                to="/login"
                className="block w-full text-center bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors mt-2"
              >
                Aller à la connexion
              </Link>
            </div>
          )}

          {affiche.type === "erreur" && (
            <div className="flex flex-col gap-4">
              <p role="alert" className="flex items-start gap-2 text-red-600 text-sm">
                <AlertCircle size={16} className="shrink-0 mt-0.5" />
                {affiche.message}
              </p>
              <p className="text-gray-600 text-sm">
                Saisissez l'adresse utilisée à l'inscription pour recevoir un nouveau lien de confirmation.
              </p>
              <div>
                <label htmlFor="confirmation-email" className="text-sm font-medium text-gray-700">
                  Adresse email
                </label>
                <input
                  id="confirmation-email"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="jean@example.com"
                  autoComplete="email"
                  className="w-full border border-gray-300 rounded-lg px-4 py-3 mt-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-immo-gold/40 focus:border-immo-gold transition-shadow"
                />
              </div>
              <RenvoiConfirmation email={email} />
              <Link to="/login" className="text-center text-sm text-immo-gold font-semibold">
                Aller à la connexion
              </Link>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
