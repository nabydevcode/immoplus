import { BrowserRouter, Routes, Route } from "react-router-dom";
import { AuthProvider } from "./context/AuthContext";
import Layout from "./components/Layout";
import Home from "./pages/Home";
import Login from "./pages/Login";
import Inscription from "./pages/Inscription";
import MotDePasseOublie from "./pages/MotDePasseOublie";
import ReinitialiserMotDePasse from "./pages/ReinitialiserMotDePasse";
import Annonces from "./pages/Annonces";
import DetailAppartement from "./pages/DetailAppartement";
import Candidater from "./pages/Candidater";
import Reserver from "./pages/Reserver";
import MesCandidatures from "./pages/MesCandidatures";
import MesReservations from "./pages/MesReservations";
import CandidaturesRecues from "./pages/bailleur/CandidaturesRecues";
import MesLogements from "./pages/bailleur/MesLogements";
import ReservationsRecues from "./pages/bailleur/ReservationsRecues";
import NotFound from "./pages/NotFound";
import AdminLayout from "./components/admin/AdminLayout";
import TableauDeBord from "./pages/admin/TableauDeBord";
import Utilisateurs from "./pages/admin/Utilisateurs";
import Appartements from "./pages/admin/Appartements";
import Candidatures from "./pages/admin/Candidatures";
import Locations from "./pages/admin/Locations";
import Financements from "./pages/admin/Financements";
import Reservations from "./pages/admin/Reservations";
import ProfilAdmin from "./pages/admin/Profil";
import SmsEnvoyes from "./pages/admin/SmsEnvoyes";
import Sauvegardes from "./pages/admin/Sauvegardes";
import Journal from "./pages/admin/Journal";
import Exports from "./pages/admin/Exports";
import Signalements from "./pages/admin/Signalements";
import Identites from "./pages/admin/Identites";
import Profil from "./pages/Profil";
import MesFavoris from "./pages/MesFavoris";
import TableauDeBordHote from "./pages/bailleur/TableauDeBordHote";
import TitreRoute from "./navigation/TitreRoute";
import FavorisProvider from "./components/favoris/FavorisProvider";

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
                <Layout>
                  <Home />
                </Layout>
              }
            />
            <Route path="/login" element={<Login />} />
            <Route path="/inscription" element={<Inscription />} />
            <Route path="/mot-de-passe-oublie" element={<MotDePasseOublie />} />
            <Route path="/reinitialiser-mot-de-passe" element={<ReinitialiserMotDePasse />} />
            <Route
              path="/annonces/:type"
              element={
                <Layout>
                  <Annonces />
                </Layout>
              }
            />
            <Route
              path="/annonces/:type/:id"
              element={
                <Layout>
                  <DetailAppartement />
                </Layout>
              }
            />
            <Route
              path="/annonces/:type/:id/candidater"
              element={
                <Layout>
                  <Candidater />
                </Layout>
              }
            />
            <Route
              path="/annonces/:type/:id/reserver"
              element={
                <Layout>
                  <Reserver />
                </Layout>
              }
            />
            <Route
              path="/mes-candidatures"
              element={
                <Layout>
                  <MesCandidatures />
                </Layout>
              }
            />
            <Route
              path="/mes-reservations"
              element={
                <Layout>
                  <MesReservations />
                </Layout>
              }
            />
            <Route
              path="/profil"
              element={
                <Layout>
                  <Profil />
                </Layout>
              }
            />
            <Route
              path="/favoris"
              element={
                <Layout>
                  <MesFavoris />
                </Layout>
              }
            />
            <Route
              path="/bailleur/tableau-de-bord"
              element={
                <Layout>
                  <TableauDeBordHote />
                </Layout>
              }
            />
            <Route
              path="/bailleur/logements"
              element={
                <Layout>
                  <MesLogements />
                </Layout>
              }
            />
            <Route
              path="/bailleur/candidatures"
              element={
                <Layout>
                  <CandidaturesRecues />
                </Layout>
              }
            />
            <Route
              path="/bailleur/reservations"
              element={
                <Layout>
                  <ReservationsRecues />
                </Layout>
              }
            />
            <Route path="/admin" element={<AdminLayout />}>
              <Route index element={<TableauDeBord />} />
              <Route path="utilisateurs" element={<Utilisateurs />} />
              <Route path="appartements" element={<Appartements />} />
              <Route path="candidatures" element={<Candidatures />} />
              <Route path="locations" element={<Locations />} />
              <Route path="financements" element={<Financements />} />
              <Route path="reservations" element={<Reservations />} />
              <Route path="sms" element={<SmsEnvoyes />} />
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
                <Layout>
                  <NotFound />
                </Layout>
              }
            />
          </Routes>
        </BrowserRouter>
      </FavorisProvider>
    </AuthProvider>
  );
}
