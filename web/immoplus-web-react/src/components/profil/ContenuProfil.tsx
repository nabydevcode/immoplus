import { useState, type FormEvent, type ReactNode } from "react";
import { CheckCircle2, KeyRound, ShieldCheck, UserRound } from "lucide-react";
import { apiClient } from "../../api/client";
import { useAuth } from "../../context/auth";
import { MessageErreur } from "../admin/ui";
import { classeBoutonPrincipal, classeChamp, messageErreur, useChargement } from "../admin/outils";
import type { ProfilDto } from "../../types/models";
import VerificationIdentite from "../identite/VerificationIdentite";
import ChampMotDePasse from "../compte/ChampMotDePasse";

const libellesRole: Record<string, string> = {
  Admin: "Administrateur",
  Bailleur: "Bailleur",
  Locataire: "Locataire",
};

function Carte({ titre, icone, children }: { titre: string; icone: ReactNode; children: ReactNode }) {
  return (
    <section className="bg-white rounded-2xl border border-black/5 shadow-sm p-5 sm:p-6 min-w-0">
      <h2 className="flex items-center gap-2 font-bold text-lg mb-5">
        <span className="text-immo-gold">{icone}</span>
        {titre}
      </h2>
      {children}
    </section>
  );
}

function MessageSucces({ children }: { children: ReactNode }) {
  return (
    <p className="flex items-center gap-2 text-green-700 text-sm" role="status">
      <CheckCircle2 size={16} className="shrink-0" />
      {children}
    </p>
  );
}

function FormulaireInformations({ profil, onEnregistre }: { profil: ProfilDto; onEnregistre: (p: ProfilDto) => void }) {
  const { mettreAJourNom } = useAuth();
  const [nom, setNom] = useState(profil.nom);
  const [telephone, setTelephone] = useState(profil.telephone);
  const [email, setEmail] = useState(profil.email);
  const [notificationsSms, setNotificationsSms] = useState(profil.notificationsSms ?? true);
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);
  const [succes, setSucces] = useState(false);
  // Toute nouvelle saisie efface le message précédent.
  const saisir = (maj: (v: string) => void) => (v: string) => {
    maj(v);
    setErreur(null);
    setSucces(false);
  };

  const modifie =
    nom !== profil.nom ||
    telephone !== profil.telephone ||
    email !== profil.email ||
    notificationsSms !== (profil.notificationsSms ?? true);

  const enregistrer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    setSucces(false);
    if (!nom.trim() || !email.trim()) {
      setErreur("Le nom et l'email sont obligatoires.");
      return;
    }
    setEnCours(true);
    try {
      const misAJour = await apiClient.put<ProfilDto>("utilisateur/moi", {
        nom: nom.trim(),
        telephone: telephone.trim(),
        email: email.trim(),
        notificationsSms,
      });
      onEnregistre(misAJour);
      setNom(misAJour.nom);
      setTelephone(misAJour.telephone);
      setEmail(misAJour.email);
      setNotificationsSms(misAJour.notificationsSms ?? true);
      mettreAJourNom(misAJour.nom);
      setSucces(true);
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'enregistrer vos informations."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <form onSubmit={enregistrer} className="flex flex-col gap-4">
      <label className="text-sm font-medium text-gray-700">
        Nom complet
        <input
          className={classeChamp}
          value={nom}
          onChange={(e) => saisir(setNom)(e.target.value)}
          autoComplete="name"
        />
      </label>
      <label className="text-sm font-medium text-gray-700">
        Téléphone
        <input
          className={classeChamp}
          type="tel"
          value={telephone}
          onChange={(e) => saisir(setTelephone)(e.target.value)}
          autoComplete="tel"
        />
      </label>
      <label className="text-sm font-medium text-gray-700">
        Email
        <input
          className={classeChamp}
          type="email"
          value={email}
          onChange={(e) => saisir(setEmail)(e.target.value)}
          autoComplete="email"
        />
      </label>
      <label className="flex items-start gap-3 text-sm text-gray-700 bg-immo-cream/60 rounded-lg px-3 py-2.5 cursor-pointer">
        <input
          type="checkbox"
          checked={notificationsSms}
          onChange={(e) => {
            setNotificationsSms(e.target.checked);
            setErreur(null);
            setSucces(false);
          }}
          className="mt-0.5 h-4 w-4 accent-immo-gold shrink-0"
        />
        <span>
          <span className="font-medium">Recevoir les notifications par SMS/WhatsApp</span>
          <span className="block text-xs text-gray-500 mt-0.5">
            Messages courts pour les événements importants (réservation, visite, paiement, rappel d'échéance), en plus
            de l'email, au numéro ci-dessus.
          </span>
        </span>
      </label>
      <div className="text-sm font-medium text-gray-700">
        Rôle
        <p className="mt-1 px-3 py-2 rounded-lg bg-immo-cream text-gray-600 text-sm">
          {libellesRole[profil.role] ?? profil.role}
        </p>
      </div>
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
      {succes && <MessageSucces>Vos informations ont été enregistrées.</MessageSucces>}
      <div>
        <button type="submit" className={classeBoutonPrincipal()} disabled={enCours || !modifie}>
          {enCours ? "Enregistrement..." : "Enregistrer"}
        </button>
      </div>
    </form>
  );
}

function FormulaireMotDePasse() {
  const [actuel, setActuel] = useState("");
  const [nouveau, setNouveau] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);
  const [succes, setSucces] = useState(false);
  // Toute nouvelle saisie efface le message précédent.
  const saisir = (maj: (v: string) => void) => (v: string) => {
    maj(v);
    setErreur(null);
    setSucces(false);
  };

  const changer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    setSucces(false);
    if (!actuel) return setErreur("Saisissez votre mot de passe actuel.");
    if (nouveau.length < 6) return setErreur("Le nouveau mot de passe doit contenir au moins 6 caractères.");
    if (nouveau !== confirmation) return setErreur("La confirmation ne correspond pas au nouveau mot de passe.");
    setEnCours(true);
    try {
      await apiClient.post("utilisateur/moi/mot-de-passe", { motDePasseActuel: actuel, nouveauMotDePasse: nouveau });
      setActuel("");
      setNouveau("");
      setConfirmation("");
      setSucces(true);
    } catch (err) {
      setErreur(messageErreur(err, "Impossible de changer le mot de passe."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <form onSubmit={changer} className="flex flex-col gap-4">
      <label className="text-sm font-medium text-gray-700">
        Mot de passe actuel
        <ChampMotDePasse
          className={classeChamp}
          value={actuel}
          onChange={(e) => saisir(setActuel)(e.target.value)}
          autoComplete="current-password"
        />
      </label>
      <label className="text-sm font-medium text-gray-700">
        Nouveau mot de passe
        <ChampMotDePasse
          className={classeChamp}
          value={nouveau}
          onChange={(e) => saisir(setNouveau)(e.target.value)}
          autoComplete="new-password"
        />
        <span className="block text-xs text-gray-400 font-normal mt-1">6 caractères minimum.</span>
      </label>
      <label className="text-sm font-medium text-gray-700">
        Confirmer le nouveau mot de passe
        <ChampMotDePasse
          className={classeChamp}
          value={confirmation}
          onChange={(e) => saisir(setConfirmation)(e.target.value)}
          autoComplete="new-password"
        />
      </label>
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
      {succes && <MessageSucces>Votre mot de passe a été modifié.</MessageSucces>}
      <div>
        <button type="submit" className={classeBoutonPrincipal()} disabled={enCours}>
          {enCours ? "Modification..." : "Changer mon mot de passe"}
        </button>
      </div>
    </form>
  );
}

/** Contenu de la page « Mon profil », partagé entre l'espace public (/profil) et l'espace admin (/admin/profil). */
export default function ContenuProfil() {
  const { donnees: profil, setDonnees, chargement, erreur } = useChargement<ProfilDto>("utilisateur/moi");

  if (chargement) return <p className="text-gray-500 text-sm">Chargement...</p>;
  if (erreur || !profil) return <MessageErreur>{erreur ?? "Impossible de charger votre profil."}</MessageErreur>;

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
      <Carte titre="Mes informations" icone={<UserRound size={20} />}>
        <FormulaireInformations profil={profil} onEnregistre={setDonnees} />
      </Carte>
      <Carte titre="Changer mon mot de passe" icone={<KeyRound size={20} />}>
        <FormulaireMotDePasse />
      </Carte>
      {profil.role !== "Admin" && (
        <Carte titre="Vérifier mon identité" icone={<ShieldCheck size={20} />}>
          <VerificationIdentite />
        </Carte>
      )}
    </div>
  );
}
