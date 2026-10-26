import { useState, type InputHTMLAttributes } from "react";
import { Eye, EyeOff } from "lucide-react";

/**
 * Champ de mot de passe avec un bouton « œil » pour afficher ou masquer la saisie
 * (vérifier ce que l'on a tapé, surtout sur téléphone). Accepte tous les attributs d'un <input>.
 */
export default function ChampMotDePasse({
  className = "",
  ...attributs
}: Omit<InputHTMLAttributes<HTMLInputElement>, "type">) {
  const [visible, setVisible] = useState(false);
  return (
    <span className="relative block">
      <input {...attributs} type={visible ? "text" : "password"} className={`${className} pr-11`} />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        aria-label={visible ? "Masquer le mot de passe" : "Afficher le mot de passe"}
        aria-pressed={visible}
        title={visible ? "Masquer le mot de passe" : "Afficher le mot de passe"}
        className="absolute right-1.5 top-1/2 -translate-y-1/2 mt-0.5 p-1.5 rounded-md text-gray-400 hover:text-immo-dark focus:outline-none focus-visible:ring-2 focus-visible:ring-immo-gold/50"
      >
        {visible ? <EyeOff size={18} /> : <Eye size={18} />}
      </button>
    </span>
  );
}
