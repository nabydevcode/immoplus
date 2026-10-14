import { useState } from "react";
import { FileDown } from "lucide-react";
import { telechargerFichier } from "../../api/client";
import { classeBoutonSecondaire, messageErreur } from "../admin/outils";
import type { DocumentPdf } from "./pdf";

/**
 * Bouton « Télécharger (PDF) » d'un document généré par l'API (quittance, reçu, contrat, état des lieux).
 * Le fichier est récupéré avec le jeton puis enregistré ; un refus de l'API (409…) s'affiche sous le bouton.
 * `lien` : rendu discret (texte souligné) pour une ligne de liste.
 */
export default function BoutonPdf({
  fichier,
  libelle = "Télécharger (PDF)",
  lien = false,
}: {
  fichier: DocumentPdf;
  libelle?: string;
  lien?: boolean;
}) {
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);

  const telecharger = async () => {
    setErreur(null);
    setEnCours(true);
    try {
      await telechargerFichier(fichier.chemin, fichier.nomFichier);
    } catch (err) {
      setErreur(messageErreur(err, "Téléchargement impossible."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <span className="inline-flex flex-col items-start gap-1 max-w-full">
      <button
        type="button"
        onClick={telecharger}
        disabled={enCours}
        className={
          lien
            ? "inline-flex items-center gap-1 text-xs font-semibold text-immo-dark underline underline-offset-2 hover:text-immo-gold disabled:opacity-60"
            : classeBoutonSecondaire(true)
        }
      >
        <FileDown size={14} className="shrink-0" />
        {enCours ? "Téléchargement..." : libelle}
      </button>
      {erreur && (
        <span role="alert" className="text-xs text-red-600 whitespace-normal">
          {erreur}
        </span>
      )}
    </span>
  );
}
