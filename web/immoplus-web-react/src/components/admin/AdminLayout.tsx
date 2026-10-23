import { Fragment, Suspense, useState } from "react";
import { Link, NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  Building2,
  CalendarDays,
  DatabaseBackup,
  ExternalLink,
  FileSpreadsheet,
  FileText,
  Flag,
  Home,
  IdCard,
  KeyRound,
  LayoutDashboard,
  LogOut,
  Menu,
  MessageSquareText,
  Send,
  ScrollText,
  ShieldAlert,
  UserRound,
  Users,
  Wallet,
  X,
} from "lucide-react";
import { useAuth } from "../../context/auth";
import { useLienConnexion } from "../../hooks/useLienConnexion";
import { useCompteursAdmin } from "./compteurs";

type Compteur = "signalements" | "identites";

/** Menu regroupé sous des intertitres ; `compteur` : pastille du nombre d'éléments à traiter. */
const sections: {
  to: string;
  label: string;
  icone: typeof Users;
  end?: boolean;
  groupe?: string;
  compteur?: Compteur;
}[] = [
  { to: "/admin", label: "Tableau de bord", icone: LayoutDashboard, end: true },
  { to: "/admin/utilisateurs", label: "Utilisateurs", icone: Users, groupe: "Gestion" },
  { to: "/admin/identites", label: "Identités", icone: IdCard, compteur: "identites" },
  { to: "/admin/appartements", label: "Appartements", icone: Home },
  { to: "/admin/candidatures", label: "Candidatures", icone: FileText },
  { to: "/admin/locations", label: "Locations", icone: KeyRound },
  { to: "/admin/financements", label: "Financements", icone: Wallet },
  { to: "/admin/reservations", label: "Réservations", icone: CalendarDays },
  { to: "/admin/signalements", label: "Signalements", icone: Flag, groupe: "Contrôle", compteur: "signalements" },
  { to: "/admin/journal", label: "Journal", icone: ScrollText },
  { to: "/admin/exports", label: "Exports", icone: FileSpreadsheet },
  // Outils techniques, séparés de la gestion courante par un intertitre.
  { to: "/admin/sauvegardes", label: "Sauvegardes", icone: DatabaseBackup, groupe: "Système" },
  { to: "/admin/sms", label: "SMS envoyés", icone: MessageSquareText },
  { to: "/admin/messages-sortants", label: "Messages sortants", icone: Send },
  { to: "/admin/profil", label: "Mon profil", icone: UserRound, groupe: "Compte" },
];

const titresCompteur: Record<Compteur, string> = {
  signalements: "nouveau(x) signalement(s)",
  identites: "pièce(s) d'identité à vérifier",
};

export default function AdminLayout() {
  const { estConnecte, roleUtilisateur, nomUtilisateur, deconnexion } = useAuth();
  const lienConnexion = useLienConnexion();
  const navigate = useNavigate();
  const [menuOuvert, setMenuOuvert] = useState(false);
  const compteurs = useCompteursAdmin(estConnecte && roleUtilisateur === "Admin");
  const totalATraiter = compteurs.signalements + compteurs.identites;

  if (!estConnecte || roleUtilisateur !== "Admin") {
    return (
      <div className="min-h-screen bg-immo-cream px-6 py-20 flex flex-col items-center text-center gap-3">
        <ShieldAlert size={36} className="text-immo-gold" />
        <h1 className="text-xl font-extrabold">Accès réservé aux administrateurs</h1>
        <Link to={lienConnexion} className="text-immo-gold font-semibold text-sm">
          Se connecter avec un compte admin
        </Link>
      </div>
    );
  }

  const seDeconnecter = () => {
    deconnexion();
    navigate("/login");
  };

  const menu = (
    <div className="flex flex-col h-full">
      <Link to="/admin" className="flex items-center gap-2 px-5 h-16 text-immo-cream font-extrabold text-lg shrink-0">
        <span className="flex items-center justify-center w-8 h-8 rounded-lg bg-immo-gold/15 text-immo-gold">
          <Building2 size={18} />
        </span>
        ImmoPlus
        <span className="text-[10px] font-bold uppercase tracking-wider text-immo-gold bg-immo-gold/10 px-1.5 py-0.5 rounded">
          Admin
        </span>
      </Link>

      <nav className="flex-1 min-h-0 overflow-y-auto px-3 py-3 lg:py-4 flex flex-col gap-1">
        {sections.map(({ to, label, icone: Icone, end, groupe, compteur }) => (
          <Fragment key={to}>
            {groupe && (
              <p className="px-3 pt-3 pb-1 text-[10px] font-bold uppercase tracking-wider text-gray-500">{groupe}</p>
            )}
            <NavLink
              to={to}
              end={end}
              onClick={() => setMenuOuvert(false)}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2 lg:py-2.5 rounded-lg text-sm font-medium transition-colors ${
                  isActive ? "bg-immo-gold text-immo-dark" : "text-gray-300 hover:bg-white/5 hover:text-white"
                }`
              }
            >
              <Icone size={18} />
              <span className="flex-1">{label}</span>
              {compteur && compteurs[compteur] > 0 && (
                <span
                  className="min-w-[20px] h-5 px-1.5 rounded-full bg-red-600 text-white text-[11px] font-bold leading-none inline-flex items-center justify-center"
                  title={`${compteurs[compteur]} ${titresCompteur[compteur]}`}
                >
                  {compteurs[compteur] > 99 ? "99+" : compteurs[compteur]}
                  <span className="sr-only"> {titresCompteur[compteur]}</span>
                </span>
              )}
            </NavLink>
          </Fragment>
        ))}
      </nav>

      <div className="border-t border-white/10 px-3 py-3 lg:py-4 flex flex-col gap-1 shrink-0">
        <p className="px-3 pb-2 text-xs text-gray-500">
          Connecté : <span className="text-immo-cream font-semibold">{nomUtilisateur}</span>
        </p>
        <Link
          to="/"
          className="flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium text-gray-300 hover:bg-white/5 hover:text-white transition-colors"
        >
          <ExternalLink size={18} />
          Voir le site
        </Link>
        <button
          onClick={seDeconnecter}
          className="flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium text-gray-300 hover:bg-white/5 hover:text-white transition-colors"
        >
          <LogOut size={18} />
          Déconnexion
        </button>
      </div>
    </div>
  );

  return (
    <div className="min-h-screen bg-immo-cream">
      {/* Menu fixe sur grand écran */}
      <aside className="hidden lg:block fixed inset-y-0 left-0 w-64 bg-immo-dark">{menu}</aside>

      {/* Tiroir sur mobile */}
      {menuOuvert && (
        <div className="lg:hidden fixed inset-0 z-50 flex">
          <div className="absolute inset-0 bg-black/50" onClick={() => setMenuOuvert(false)} />
          <aside className="relative w-64 max-w-[85vw] h-full bg-immo-dark">
            <button
              onClick={() => setMenuOuvert(false)}
              className="absolute top-4 right-3 text-gray-400 hover:text-white p-1"
              aria-label="Fermer le menu"
            >
              <X size={20} />
            </button>
            {menu}
          </aside>
        </div>
      )}

      <div className="lg:pl-64">
        <header className="lg:hidden sticky top-0 z-40 h-14 bg-immo-dark flex items-center gap-3 px-4">
          <button
            onClick={() => setMenuOuvert(true)}
            className="relative text-immo-cream p-1"
            aria-label={totalATraiter > 0 ? `Ouvrir le menu (${totalATraiter} élément(s) à traiter)` : "Ouvrir le menu"}
          >
            <Menu size={22} />
            {totalATraiter > 0 && (
              <span className="absolute top-0 right-0 w-2.5 h-2.5 rounded-full bg-red-600 ring-2 ring-immo-dark" />
            )}
          </button>
          <span className="text-immo-cream font-extrabold">ImmoPlus Admin</span>
        </header>
        <main className="px-4 sm:px-8 py-8 max-w-7xl">
          <Suspense fallback={<p className="text-gray-500 text-sm">Chargement...</p>}>
            <Outlet />
          </Suspense>
        </main>
      </div>
    </div>
  );
}
