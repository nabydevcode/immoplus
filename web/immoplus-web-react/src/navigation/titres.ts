import { matchPath } from "react-router-dom";

/** Titre de l'onglet par route (la fiche d'une annonce le remplace par le nom du logement une fois chargée). */
const TITRES: [string, string][] = [
  ["/", "Location et séjours en Guinée"],
  ["/login", "Connexion"],
  ["/inscription", "Inscription"],
  ["/mot-de-passe-oublie", "Mot de passe oublié"],
  ["/reinitialiser-mot-de-passe", "Nouveau mot de passe"],
  ["/confirmer-email", "Confirmation de l'adresse email"],
  ["/annonces/longue-duree", "Location longue durée"],
  ["/annonces/sejour-court", "Séjours courts"],
  ["/annonces/:type/:id/candidater", "Candidater"],
  ["/annonces/:type/:id/reserver", "Réserver"],
  ["/annonces/:type/:id", "Annonce"],
  ["/mes-candidatures", "Mes candidatures"],
  ["/mes-reservations", "Mes réservations"],
  ["/profil", "Mon profil"],
  ["/favoris", "Mes favoris"],
  ["/bailleur/tableau-de-bord", "Tableau de bord hôte"],
  ["/bailleur/logements", "Mes logements"],
  ["/bailleur/candidatures", "Candidatures reçues"],
  ["/bailleur/reservations", "Réservations reçues"],
  ["/admin", "Administration"],
  ["/admin/utilisateurs", "Utilisateurs · Admin"],
  ["/admin/appartements", "Appartements · Admin"],
  ["/admin/candidatures", "Candidatures · Admin"],
  ["/admin/locations", "Locations · Admin"],
  ["/admin/financements", "Financements · Admin"],
  ["/admin/reservations", "Réservations · Admin"],
  ["/admin/sms", "SMS envoyés · Admin"],
  ["/admin/messages-sortants", "Messages sortants · Admin"],
  ["/admin/sauvegardes", "Sauvegardes · Admin"],
  ["/admin/identites", "Identités · Admin"],
  ["/admin/signalements", "Signalements · Admin"],
  ["/admin/journal", "Journal · Admin"],
  ["/admin/exports", "Exports · Admin"],
  ["/admin/profil", "Profil · Admin"],
];

export function titreDeRoute(chemin: string) {
  const titre = TITRES.find(([motif]) => matchPath(motif, chemin))?.[1] ?? "Page introuvable";
  return `${titre} · ImmoPlus`;
}
