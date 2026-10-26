import { useState, type ReactNode } from "react";
import { apiClient, ApiError } from "../api/client";
import { AuthContext, type AuthState } from "./auth";

interface ReponseConnexion {
  id: number;
  token: string;
  nom: string;
  role: string;
}

function lireEtatInitial(): AuthState {
  const token = localStorage.getItem("authToken");
  if (!token) {
    return { estConnecte: false, utilisateurId: null, nomUtilisateur: null, roleUtilisateur: null };
  }
  return {
    estConnecte: true,
    utilisateurId: Number(localStorage.getItem("authId")) || null,
    nomUtilisateur: localStorage.getItem("authNom"),
    roleUtilisateur: localStorage.getItem("authRole"),
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [etat, setEtat] = useState<AuthState>(lireEtatInitial);

  const connexion = async (email: string, motDePasse: string) => {
    try {
      const resultat = await apiClient.post<ReponseConnexion>("utilisateur/connexion", { email, motDePasse });
      localStorage.setItem("authToken", resultat.token);
      localStorage.setItem("authId", String(resultat.id));
      localStorage.setItem("authNom", resultat.nom ?? "");
      localStorage.setItem("authRole", resultat.role ?? "");

      setEtat({
        estConnecte: true,
        utilisateurId: resultat.id,
        nomUtilisateur: resultat.nom,
        roleUtilisateur: resultat.role,
      });
      return { succes: true, role: resultat.role };
    } catch (err) {
      // 429 : trop d'échecs pour cet email ou cette adresse, le message de l'API indique le délai à attendre.
      const message =
        err instanceof ApiError
          ? err.statut === 429 && err.message === "Erreur 429"
            ? "Trop de tentatives de connexion. Réessayez dans quelques minutes."
            : err.message
          : "Email ou mot de passe incorrect.";
      return { succes: false, erreur: message, code: err instanceof ApiError ? err.code : null };
    }
  };

  const deconnexion = () => {
    localStorage.removeItem("authToken");
    localStorage.removeItem("authId");
    localStorage.removeItem("authNom");
    localStorage.removeItem("authRole");
    setEtat({ estConnecte: false, utilisateurId: null, nomUtilisateur: null, roleUtilisateur: null });
  };

  const mettreAJourNom = (nom: string) => {
    localStorage.setItem("authNom", nom);
    setEtat((e) => ({ ...e, nomUtilisateur: nom }));
  };

  return (
    <AuthContext.Provider value={{ ...etat, connexion, deconnexion, mettreAJourNom }}>{children}</AuthContext.Provider>
  );
}
