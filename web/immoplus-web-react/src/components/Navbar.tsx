import { useEffect, useRef, useState } from "react";
import { Link, NavLink } from "react-router-dom";
import { Menu, X, Building2, UserRound, ChevronDown, Heart, LogOut } from "lucide-react";
import { useAuth } from "../context/auth";
import { useMessagesNonLus } from "./messages/useMessagesNonLus";

/** Pastille du nombre de messages non lus, à côté du lien des réservations. */
function PastilleNonLus({ nombre }: { nombre: number }) {
  if (nombre <= 0) return null;
  return (
    <span
      className="ml-1.5 inline-flex items-center justify-center min-w-[18px] h-[18px] px-1 rounded-full bg-red-600 text-white text-[10px] font-bold leading-none"
      title={`${nombre} message${nombre > 1 ? "s" : ""} non lu${nombre > 1 ? "s" : ""}`}
    >
      {nombre > 9 ? "9+" : nombre}
      <span className="sr-only">
        {" "}
        message{nombre > 1 ? "s" : ""} non lu{nombre > 1 ? "s" : ""}
      </span>
    </span>
  );
}

/** Menu du compte (bureau) : profil, favoris et déconnexion, pour garder la barre compacte. */
function MenuCompte({
  nom,
  lienProfil,
  avecFavoris,
  onDeconnexion,
}: {
  nom: string | null;
  lienProfil: string;
  avecFavoris: boolean;
  onDeconnexion: () => void;
}) {
  const [ouvert, setOuvert] = useState(false);
  const conteneur = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!ouvert) return;
    const clic = (e: MouseEvent) => {
      if (!conteneur.current?.contains(e.target as Node)) setOuvert(false);
    };
    const touche = (e: KeyboardEvent) => e.key === "Escape" && setOuvert(false);
    document.addEventListener("mousedown", clic);
    document.addEventListener("keydown", touche);
    return () => {
      document.removeEventListener("mousedown", clic);
      document.removeEventListener("keydown", touche);
    };
  }, [ouvert]);

  const classeElement =
    "flex items-center gap-2.5 w-full px-4 py-2.5 text-sm text-gray-700 hover:bg-immo-cream hover:text-immo-dark text-left";

  return (
    <div ref={conteneur} className="relative">
      <button
        type="button"
        onClick={() => setOuvert((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={ouvert}
        title="Mon compte"
        aria-label={`Mon compte : ${nom ?? ""}`}
        className="flex items-center gap-1.5 text-sm text-gray-300 hover:text-white transition-colors px-2.5 py-2 rounded-lg border border-white/15 hover:bg-white/5"
      >
        <UserRound size={16} className="shrink-0 text-immo-gold" />
        <span className="truncate max-w-40 hidden xl:inline text-immo-cream font-semibold">{nom}</span>
        <ChevronDown size={14} className={`shrink-0 transition-transform ${ouvert ? "rotate-180" : ""}`} />
      </button>
      {ouvert && (
        <div
          role="menu"
          className="absolute right-0 mt-2 w-56 bg-white rounded-xl shadow-xl border border-black/5 py-1.5 overflow-hidden"
        >
          <p className="px-4 py-2 text-xs text-gray-500 border-b border-black/5 mb-1 truncate">
            Connecté : <span className="font-semibold text-gray-800">{nom}</span>
          </p>
          <Link to={lienProfil} role="menuitem" onClick={() => setOuvert(false)} className={classeElement}>
            <UserRound size={16} className="text-gray-400" />
            Mon profil
          </Link>
          {avecFavoris && (
            <Link to="/favoris" role="menuitem" onClick={() => setOuvert(false)} className={classeElement}>
              <Heart size={16} className="text-gray-400" />
              Mes favoris
            </Link>
          )}
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOuvert(false);
              onDeconnexion();
            }}
            className={`${classeElement} border-t border-black/5 mt-1`}
          >
            <LogOut size={16} className="text-gray-400" />
            Déconnexion
          </button>
        </div>
      )}
    </div>
  );
}

const liens = [
  { to: "/", label: "Accueil", end: true },
  { to: "/annonces/longue-duree", label: "Longue durée" },
  { to: "/annonces/sejour-court", label: "Séjour court" },
];

export default function Navbar() {
  const { estConnecte, nomUtilisateur, roleUtilisateur, deconnexion } = useAuth();
  const [ouvert, setOuvert] = useState(false);
  const nonLus = useMessagesNonLus(estConnecte && (roleUtilisateur === "Locataire" || roleUtilisateur === "Bailleur"));
  /** Le lien des réservations porte la pastille des messages non lus. */
  const lienMessages = roleUtilisateur === "Bailleur" ? "/bailleur/reservations" : "/mes-reservations";
  const liensVisibles =
    roleUtilisateur === "Admin"
      ? [...liens, { to: "/admin", label: "Administration", end: false }]
      : roleUtilisateur === "Locataire"
        ? [
            ...liens,
            { to: "/mes-candidatures", label: "Mes candidatures", end: false },
            { to: "/mes-reservations", label: "Mes réservations", end: false },
          ]
        : roleUtilisateur === "Bailleur"
          ? [
              ...liens,
              { to: "/bailleur/tableau-de-bord", label: "Tableau de bord", end: false },
              { to: "/bailleur/logements", label: "Mes logements", end: false },
              { to: "/bailleur/candidatures", label: "Candidatures", end: false },
              { to: "/bailleur/reservations", label: "Réservations", end: false },
            ]
          : liens;

  const lienProfil = roleUtilisateur === "Admin" ? "/admin/profil" : "/profil";

  const classeLien = (actif: boolean) =>
    `inline-flex items-center text-sm font-medium whitespace-nowrap transition-colors ${actif ? "text-immo-gold" : "text-gray-300 hover:text-white"}`;

  return (
    <header className="sticky top-0 z-50 bg-immo-dark/95 backdrop-blur border-b border-white/10">
      <div className="max-w-6xl mx-auto px-6 h-16 flex items-center justify-between gap-6">
        <Link to="/" className="flex items-center gap-2 text-immo-cream font-extrabold text-lg shrink-0">
          <span className="flex items-center justify-center w-8 h-8 rounded-lg bg-immo-gold/15 text-immo-gold">
            <Building2 size={18} />
          </span>
          ImmoPlus
        </Link>

        <nav className="hidden lg:flex items-center gap-5 xl:gap-7">
          {liensVisibles.map((lien) => (
            <NavLink key={lien.to} to={lien.to} end={lien.end} className={({ isActive }) => classeLien(isActive)}>
              {lien.label}
              {lien.to === lienMessages && <PastilleNonLus nombre={nonLus} />}
            </NavLink>
          ))}
        </nav>

        <div className="hidden lg:flex items-center gap-3 xl:gap-4 shrink-0">
          {estConnecte ? (
            <MenuCompte
              nom={nomUtilisateur}
              lienProfil={lienProfil}
              avecFavoris={roleUtilisateur !== "Admin"}
              onDeconnexion={deconnexion}
            />
          ) : (
            <>
              <Link
                to="/inscription"
                className="text-sm font-semibold px-4 py-2 rounded-lg border border-white/15 text-immo-cream hover:bg-white/5 transition-colors"
              >
                S'inscrire
              </Link>
              <Link
                to="/login"
                className="text-sm font-semibold px-5 py-2 rounded-lg bg-immo-gold text-immo-dark hover:bg-immo-gold-light transition-colors"
              >
                Se connecter
              </Link>
            </>
          )}
        </div>

        <button
          onClick={() => setOuvert((v) => !v)}
          className="lg:hidden relative text-immo-cream p-2 -mr-2"
          aria-label={
            nonLus > 0 ? `Menu (${nonLus} message${nonLus > 1 ? "s" : ""} non lu${nonLus > 1 ? "s" : ""})` : "Menu"
          }
        >
          {ouvert ? <X size={22} /> : <Menu size={22} />}
          {nonLus > 0 && !ouvert && <span className="absolute top-1.5 right-1 w-2.5 h-2.5 rounded-full bg-red-600" />}
        </button>
      </div>

      {ouvert && (
        <div className="lg:hidden border-t border-white/10 bg-immo-dark px-6 py-4 flex flex-col gap-4">
          {liensVisibles.map((lien) => (
            <NavLink
              key={lien.to}
              to={lien.to}
              end={lien.end}
              onClick={() => setOuvert(false)}
              className={({ isActive }) => classeLien(isActive)}
            >
              {lien.label}
              {lien.to === lienMessages && <PastilleNonLus nombre={nonLus} />}
            </NavLink>
          ))}
          <div className="border-t border-white/10 pt-4">
            {estConnecte ? (
              <div className="flex flex-col gap-4">
                <Link
                  to={lienProfil}
                  onClick={() => setOuvert(false)}
                  className="flex items-center gap-2 text-sm text-gray-300 hover:text-white min-w-0"
                >
                  <UserRound size={16} className="shrink-0 text-immo-gold" />
                  <span className="truncate">
                    Mon profil · <span className="text-immo-cream font-semibold">{nomUtilisateur}</span>
                  </span>
                </Link>
                {roleUtilisateur !== "Admin" && (
                  <Link
                    to="/favoris"
                    onClick={() => setOuvert(false)}
                    className="flex items-center gap-2 text-sm text-gray-300 hover:text-white"
                  >
                    <Heart size={16} className="shrink-0 text-immo-gold" />
                    Mes favoris
                  </Link>
                )}
                <button
                  onClick={() => {
                    deconnexion();
                    setOuvert(false);
                  }}
                  className="text-sm font-semibold text-immo-cream text-left"
                >
                  Déconnexion
                </button>
              </div>
            ) : (
              <div className="flex gap-3">
                <Link
                  to="/login"
                  onClick={() => setOuvert(false)}
                  className="inline-block text-sm font-semibold px-5 py-2 rounded-lg bg-immo-gold text-immo-dark"
                >
                  Se connecter
                </Link>
                <Link
                  to="/inscription"
                  onClick={() => setOuvert(false)}
                  className="inline-block text-sm font-semibold px-5 py-2 rounded-lg border border-white/15 text-immo-cream"
                >
                  S'inscrire
                </Link>
              </div>
            )}
          </div>
        </div>
      )}
    </header>
  );
}
