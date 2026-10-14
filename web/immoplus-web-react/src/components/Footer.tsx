import { useState } from "react";
import { Building2 } from "lucide-react";

export default function Footer() {
  // Année lue une fois (pas de Date impure pendant le rendu).
  const [annee] = useState(() => new Date().getFullYear());
  return (
    <footer className="bg-immo-dark border-t border-white/10 px-6 py-10">
      <div className="max-w-6xl mx-auto flex flex-col md:flex-row items-center justify-between gap-4">
        <div className="flex items-center gap-2 text-immo-cream font-bold">
          <Building2 size={16} className="text-immo-gold" />
          ImmoPlus
        </div>
        <p className="text-gray-500 text-sm text-center">
          © {annee} ImmoPlus — La plateforme immobilière de référence.
        </p>
      </div>
    </footer>
  );
}
