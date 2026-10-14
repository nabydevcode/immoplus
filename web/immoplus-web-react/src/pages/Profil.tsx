import { Link } from "react-router-dom";
import { useAuth } from "../context/auth";
import ContenuProfil from "../components/profil/ContenuProfil";
import { useLienConnexion } from "../hooks/useLienConnexion";

export default function Profil() {
  const { estConnecte } = useAuth();
  const lienConnexion = useLienConnexion();

  if (!estConnecte) {
    return (
      <div className="px-6 py-16 max-w-xl mx-auto text-center text-gray-600">
        <Link to={lienConnexion} className="text-immo-gold font-semibold">
          Connectez-vous
        </Link>{" "}
        pour consulter votre profil.
      </div>
    );
  }

  return (
    <div className="px-4 sm:px-6 py-10">
      <div className="max-w-5xl mx-auto">
        <h1 className="text-2xl md:text-3xl font-extrabold mb-1">Mon profil</h1>
        <p className="text-gray-500 text-sm mb-8">Vos coordonnées et votre mot de passe.</p>
        <ContenuProfil />
      </div>
    </div>
  );
}
