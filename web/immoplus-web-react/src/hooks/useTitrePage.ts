import { useEffect } from "react";

/** Titre (et description) de l'onglet repris de la page affichée, rétablis en la quittant. */
export function useTitrePage(titre: string | null, description: string | null = null) {
  useEffect(() => {
    if (!titre) return;
    const titrePrecedent = document.title;
    document.title = `${titre} · ImmoPlus`;
    let meta = document.querySelector<HTMLMetaElement>('meta[name="description"]');
    const cree = !meta;
    const descriptionPrecedente = meta?.content ?? "";
    if (description) {
      if (!meta) {
        meta = document.createElement("meta");
        meta.name = "description";
        document.head.appendChild(meta);
      }
      meta.content = description;
    }
    return () => {
      document.title = titrePrecedent;
      if (!description || !meta) return;
      if (cree) meta.remove();
      else meta.content = descriptionPrecedente;
    };
  }, [titre, description]);
}
