import { createContext, useContext } from "react";

export interface FavorisValeur {
  /** Favoris disponibles pour ce compte (connecté, hors administrateur). */
  actif: boolean;
  estFavori: (appartementId: number) => boolean;
  /** Ajoute ou retire le favori (mise à jour immédiate, annulée si l'API refuse). */
  basculer: (appartementId: number) => Promise<void>;
}

export const FavorisContext = createContext<FavorisValeur | null>(null);

export function useFavoris() {
  const ctx = useContext(FavorisContext);
  if (!ctx) throw new Error("useFavoris doit être utilisé dans un FavorisProvider");
  return ctx;
}
