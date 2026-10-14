import { useState, type FormEvent } from "react";
import { ShieldPlus } from "lucide-react";
import { apiClient } from "../../api/client";
import type { UtilisateurDto } from "../../types/models";
import { MessageErreur } from "./ui";
import { classeBoutonPrincipal, classeBoutonSecondaire, classeChamp, messageErreur } from "./outils";

/** Formulaire en ligne : un admin crée un autre compte administrateur, actif immédiatement. */
export default function CreationAdministrateur({
  onCree,
  onFermer,
}: {
  onCree: (admin: UtilisateurDto) => void;
  onFermer: () => void;
}) {
  const [nom, setNom] = useState("");
  const [telephone, setTelephone] = useState("");
  const [email, setEmail] = useState("");
  const [motDePasse, setMotDePasse] = useState("");
  const [envoi, setEnvoi] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    setEnvoi(true);
    try {
      const admin = await apiClient.post<UtilisateurDto>("utilisateur/admin", {
        nom: nom.trim(),
        telephone: telephone.trim(),
        email: email.trim(),
        motDePasse,
      });
      onCree(admin);
    } catch (err) {
      setErreur(messageErreur(err, "Le compte administrateur n'a pas pu être créé."));
    } finally {
      setEnvoi(false);
    }
  };

  return (
    <form
      onSubmit={envoyer}
      className="bg-white rounded-2xl shadow-sm border border-black/5 p-5 mb-6 flex flex-col gap-4 max-w-2xl"
    >
      <div>
        <h2 className="font-bold flex items-center gap-2">
          <ShieldPlus size={18} className="text-immo-gold" />
          Créer un administrateur
        </h2>
        <p className="text-xs text-gray-500 mt-0.5">
          Le compte est actif immédiatement ; un email de bienvenue est envoyé à l'adresse indiquée.
        </p>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <label className="text-sm font-medium text-gray-700">
          Nom complet
          <input value={nom} onChange={(e) => setNom(e.target.value)} required autoFocus className={classeChamp} />
        </label>
        <label className="text-sm font-medium text-gray-700">
          Téléphone
          <input
            type="tel"
            value={telephone}
            onChange={(e) => setTelephone(e.target.value)}
            required
            className={classeChamp}
          />
        </label>
        <label className="text-sm font-medium text-gray-700">
          Email
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoComplete="off"
            className={classeChamp}
          />
        </label>
        <label className="text-sm font-medium text-gray-700">
          Mot de passe provisoire
          <input
            type="password"
            value={motDePasse}
            onChange={(e) => setMotDePasse(e.target.value)}
            required
            minLength={6}
            autoComplete="new-password"
            className={classeChamp}
          />
        </label>
      </div>
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
      <div className="flex flex-wrap gap-2 justify-end">
        <button type="button" onClick={onFermer} className={classeBoutonSecondaire()}>
          Annuler
        </button>
        <button type="submit" disabled={envoi} className={classeBoutonPrincipal()}>
          {envoi ? "Création..." : "Créer le compte"}
        </button>
      </div>
    </form>
  );
}
