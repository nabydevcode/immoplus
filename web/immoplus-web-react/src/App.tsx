import { lazy, Suspense, type ReactNode } from "react";
import { BrowserRouter, Routes, Route } from "react-router-dom";
import { AuthProvider } from "./context/AuthContext";
import Layout from "./components/Layout";
import Home from "./pages/Home";
import NotFound from "./pages/NotFound";
import TitreRoute from "./navigation/TitreRoute";
import FavorisProvider from "./components/favoris/FavorisProvider";

// Pages téléchargées à la demande : le paquet principal ne contient que l'accueil et la structure du site.
const Login = lazy(() => import("./pages/Login"));
const Inscription = lazy(() => import("./pages/Inscription"));
const MotDePasseOublie = lazy(() => import("./pages/MotDePasseOublie"));
const ReinitialiserMotDePasse = lazy(() => import("./pages/ReinitialiserMotDePasse"));
const ConfirmerEmail = lazy(() => import("./pages/ConfirmerEmail"));
const Annonces = lazy(() => import("./pages/Annonces"));
const DetailAppartement = lazy(() => import("./pages/DetailAppartement"));
const Candidater = lazy(() => import("./pages/Candidater"));
const Reserver = lazy(() => import("./pages/Reserver"));
const MesCandidatures = lazy(() => import("./pages/MesCandidatures"));
const MesReservations = lazy(() => import("./pages/MesReservations"));
const CandidaturesRecues = lazy(() => import("./pages/bailleur/CandidaturesRecues"));
const MesLogements = lazy(() => import("./pages/bailleur/MesLogements"));
const ReservationsRecues = lazy(() => import("./pages/bailleur/ReservationsRecues"));
const AdminLayout = lazy(() => import("./components/admin/AdminLayout"));
const TableauDeBord = lazy(() => import("./pages/admin/TableauDeBord"));
const Utilisateurs = lazy(() => import("./pages/admin/Utilisateurs"));
const Appartements = lazy(() => import("./pages/admin/Appartements"));
const Candidatures = lazy(() => import("./pages/admin/Candidatures"));
const Locations = lazy(() => import("./pages/admin/Locations"));
const Financements = lazy(() => import("./pages/admin/Financements"));
const Reservations = lazy(() => import("./pages/admin/Reservations"));
const ProfilAdmin = lazy(() => import("./pages/admin/Profil"));
const SmsEnvoyes = lazy(() => import("./pages/admin/SmsEnvoyes"));
const MessagesSortants = lazy(() => import("./pages/admin/MessagesSortants"));
const Sauvegardes = lazy(() => import("./pages/admin/Sauvegardes"));
const Journal = lazy(() => import("./pages/admin/Journal"));
const Exports = lazy(() => import("./pages/admin/Exports"));
const Signalements = lazy(() => import("./pages/admin/Signalements"));
const Identites = lazy(() => import("./pages/admin/Identites"));
const Profil = lazy(() => import("./pages/Profil"));
const MesFavoris = lazy(() => import("./pages/MesFavoris"));
const TableauDeBordHote = lazy(() => import("./pages/bailleur/TableauDeBordHote"));

function ChargementPage() {
  return (
    <div className="px-6 py-16 text-center text-gray-500 text-sm" role="status">
      Chargement...
    </div>
  );
}

/** Page publique ou d'espace personnel : barre de navigation et pied de page restent affichés pendant le téléchargement. */
function AvecLayout({ children }: { children: ReactNode }) {
  return (
    <Layout>
      <Suspense fallback={<ChargementPage />}>{children}</Suspense>
    </Layout>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <FavorisProvider>
        <BrowserRouter>
          <TitreRoute />
          <Routes>
            <Route
              path="/"
              element={
                <AvecLayout>
                  <Home />
                </AvecLayout>
              }
            />
            <Route
              path="/login"
              element={
                <Suspense fallback={<ChargementPage />}>
                  <Login />
                </Suspense>
              }
            />
            <Route
              path="/inscription"
              element={
                <Suspense fallback={<ChargementPage />}>
                  <Inscription />
                </Suspense>
              }
            />
            <Route
              path="/mot-de-passe-oublie"
              element={
                <Suspense fallback={<ChargementPage />}>
                  <MotDePasseOublie />
                </Suspense>
              }
            />
            <Route
              path="/reinitialiser-mot-de-passe"
              element={
                <Suspense fallback={<ChargementPage />}>
                  <ReinitialiserMotDePasse />
                </Suspense>
              }
            />
            <Route
              path="/confirmer-email"
              element={
                <Suspense fallback={<ChargementPage />}>
                  <ConfirmerEmail />
                </Suspense>
              }
            />
            <Route
              path="/annonces/:type"
              element={
                <AvecLayout>
                  <Annonces />
                </AvecLayout>
              }
            />
            <Route
              path="/annonces/:type/:id"
              element={
                <AvecLayout>
                  <DetailAppartement />
                </AvecLayout>
              }
            />
            <Route
              path="/annonces/:type/:id/candidater"
              element={
                <AvecLayout>
                  <Candidater />
                </AvecLayout>
              }
            />
            <Route
              path="/annonces/:type/:id/reserver"
              element={
                <AvecLayout>
                  <Reserver />
                </AvecLayout>
              }
            />
            <Route
              path="/mes-candidatures"
              element={
                <AvecLayout>
                  <MesCandidatures />
                </AvecLayout>
              }
            />
            <Route
              path="/mes-reservations"
              element={
                <AvecLayout>
                  <MesReservations />
                </AvecLayout>
              }
            />
            <Route
              path="/profil"
              element={
                <AvecLayout>
                  <Profil />
                </AvecLayout>
              }
            />
            <Route
              path="/favoris"
              element={
                <AvecLayout>
                  <MesFavoris />
                </AvecLayout>
              }
            />
            <Route
              path="/bailleur/tableau-de-bord"
              element={
                <AvecLayout>
                  <TableauDeBordHote />
                </AvecLayout>
              }
            />
            <Route
              path="/bailleur/logements"
              element={
                <AvecLayout>
                  <MesLogements />
                </AvecLayout>
              }
            />
            <Route
              path="/bailleur/candidatures"
              element={
                <AvecLayout>
                  <CandidaturesRecues />
                </AvecLayout>
              }
            />
            <Route
              path="/bailleur/reservations"
              element={
                <AvecLayout>
                  <ReservationsRecues />
                </AvecLayout>
              }
            />
            <Route
              path="/admin"
              element={
                <Suspense fallback={<ChargementPage />}>
                  <AdminLayout />
                </Suspense>
              }
            >
              <Route index element={<TableauDeBord />} />
              <Route path="utilisateurs" element={<Utilisateurs />} />
              <Route path="appartements" element={<Appartements />} />
              <Route path="candidatures" element={<Candidatures />} />
              <Route path="locations" element={<Locations />} />
              <Route path="financements" element={<Financements />} />
              <Route path="reservations" element={<Reservations />} />
              <Route path="sms" element={<SmsEnvoyes />} />
              <Route path="messages-sortants" element={<MessagesSortants />} />
              <Route path="sauvegardes" element={<Sauvegardes />} />
              <Route path="identites" element={<Identites />} />
              <Route path="signalements" element={<Signalements />} />
              <Route path="journal" element={<Journal />} />
              <Route path="exports" element={<Exports />} />
              <Route path="profil" element={<ProfilAdmin />} />
            </Route>
            <Route
              path="*"
              element={
                <AvecLayout>
                  <NotFound />
                </AvecLayout>
              }
            />
          </Routes>
        </BrowserRouter>
      </FavorisProvider>
    </AuthProvider>
  );
}
