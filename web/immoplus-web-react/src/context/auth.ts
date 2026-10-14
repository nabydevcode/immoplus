import { createContext, useContext } from "react";

export interface AuthState {
  estConnecte: boolean;
  utilisateurId: number | null;
  nomUtilisateur: string | null;
  roleUtilisateur: string | null;
}

export interface AuthContextValue extends AuthState {
  connexion: (email: string, motDePasse: string) => Promise<{ succes: boolean; erreur?: string; role?: string }>;
  deconnexion: () => void;
  /** Met à jour le nom affiché (navbar, menu admin) après une modification du profil. */
  mettreAJourNom: (nom: string) => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth doit être utilisé dans un AuthProvider");
  return ctx;
}
