import { useState } from "react";
import { Check, Link2, MessageCircle, Share2 } from "lucide-react";

const classeBouton =
  "inline-flex items-center gap-1.5 text-sm font-semibold px-3 py-2 rounded-lg border border-gray-300 text-gray-700 hover:bg-gray-50 transition-colors";

/** Partage d'une annonce : WhatsApp, copie du lien et partage natif du téléphone s'il existe. */
export default function Partage({ titre }: { titre: string }) {
  const [copie, setCopie] = useState(false);
  const [partageNatif] = useState(() => typeof navigator.share === "function");
  const lien = window.location.href;
  const texte = `${titre} — sur ImmoPlus : ${lien}`;

  const copier = async () => {
    try {
      await navigator.clipboard.writeText(lien);
    } catch {
      // Navigateur sans presse-papiers : sélection manuelle.
      window.prompt("Copiez le lien de l'annonce :", lien);
      return;
    }
    setCopie(true);
    window.setTimeout(() => setCopie(false), 2500);
  };

  const partager = async () => {
    try {
      await navigator.share({ title: titre, text: titre, url: lien });
    } catch {
      // Partage annulé par l'utilisateur.
    }
  };

  return (
    <div className="flex flex-wrap gap-2">
      <a
        href={`https://wa.me/?text=${encodeURIComponent(texte)}`}
        target="_blank"
        rel="noopener noreferrer"
        className="inline-flex items-center gap-1.5 text-sm font-semibold px-3 py-2 rounded-lg bg-[#25D366] text-white hover:bg-[#1ebe5b] transition-colors"
      >
        <MessageCircle size={16} />
        WhatsApp
      </a>
      <button type="button" onClick={copier} className={classeBouton}>
        {copie ? <Check size={16} className="text-green-600" /> : <Link2 size={16} />}
        {copie ? "Lien copié" : "Copier le lien"}
      </button>
      {partageNatif && (
        <button type="button" onClick={partager} className={classeBouton}>
          <Share2 size={16} />
          Partager
        </button>
      )}
    </div>
  );
}
