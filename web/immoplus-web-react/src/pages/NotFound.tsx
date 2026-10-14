import { Link } from "react-router-dom";
import { ArrowLeft, CompassIcon } from "lucide-react";

export default function NotFound() {
  return (
    <div className="min-h-[70vh] flex flex-col items-center justify-center gap-4 px-6 text-center">
      <div className="w-16 h-16 rounded-2xl bg-immo-dark/5 flex items-center justify-center mb-2">
        <CompassIcon className="text-immo-dark" size={28} />
      </div>
      <h1 className="text-2xl font-extrabold">Page introuvable</h1>
      <p className="text-gray-500 text-sm max-w-sm">La page que vous cherchez n'existe pas ou a été déplacée.</p>
      <Link
        to="/"
        className="inline-flex items-center gap-2 text-immo-dark font-semibold text-sm mt-2 hover:text-immo-gold transition-colors"
      >
        <ArrowLeft size={16} />
        Retour à l'accueil
      </Link>
    </div>
  );
}
