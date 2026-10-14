import { useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import {
  BedDouble,
  CalendarCheck,
  ChevronRight,
  Inbox,
  MessageCircle,
  Star,
  Timer,
  Users,
  Wallet,
  type LucideIcon,
} from "lucide-react";
import { useAuth } from "../../context/auth";
import { MessageErreur } from "../../components/admin/ui";
import { formaterMontant, formaterReference, useChargement } from "../../components/admin/outils";
import { BadgeSuperhote } from "../../components/sejour/Confiance";
import GraphiqueRevenus from "../../components/hote/GraphiqueRevenus";
import { ecartJours, formaterJour } from "../../components/sejour/dates";
import type { TableauDeBordHoteDto } from "../../types/models";
import { useLienConnexion } from "../../hooks/useLienConnexion";

function Chiffre({
  label,
  valeur,
  detail,
  icone: Icone,
}: {
  label: string;
  valeur: ReactNode;
  detail?: ReactNode;
  icone: LucideIcon;
}) {
  return (
    <div className="bg-white rounded-2xl border border-black/5 shadow-sm p-4 flex items-start gap-3 min-w-0">
      <span className="flex items-center justify-center w-10 h-10 rounded-xl shrink-0 bg-immo-gold/15 text-immo-gold">
        <Icone size={18} />
      </span>
      <div className="min-w-0">
        <p className="text-sm text-gray-500">{label}</p>
        <p className="text-xl sm:text-2xl font-extrabold leading-tight wrap-break-word">{valeur}</p>
        {detail && <div className="text-xs text-gray-500 mt-0.5">{detail}</div>}
      </div>
    </div>
  );
}

function Raccourci({
  to,
  label,
  nombre,
  icone: Icone,
}: {
  to: string;
  label: string;
  nombre: number;
  icone: LucideIcon;
}) {
  const actif = nombre > 0;
  return (
    <Link
      to={to}
      className={`flex items-center gap-3 rounded-2xl border shadow-sm p-4 transition-shadow hover:shadow-md min-w-0 ${
        actif ? "bg-amber-50 border-amber-200" : "bg-white border-black/5"
      }`}
    >
      <Icone size={20} className={actif ? "text-amber-700 shrink-0" : "text-gray-400 shrink-0"} />
      <span className="flex-1 min-w-0 text-sm font-semibold">{label}</span>
      <span
        className={`min-w-7 h-7 px-2 rounded-full text-sm font-bold flex items-center justify-center ${
          actif ? "bg-amber-600 text-white" : "bg-gray-100 text-gray-500"
        }`}
      >
        {nombre}
      </span>
      <ChevronRight size={16} className="text-gray-400 shrink-0" />
    </Link>
  );
}

function Section({ titre, icone: Icone, children }: { titre: string; icone: LucideIcon; children: ReactNode }) {
  return (
    <section className="bg-white rounded-2xl border border-black/5 shadow-sm min-w-0">
      <h2 className="flex items-center gap-2 px-5 py-4 border-b border-black/5 font-bold">
        <Icone size={18} className="text-immo-gold shrink-0" />
        {titre}
      </h2>
      <div className="p-5">{children}</div>
    </section>
  );
}

/** Tableau de bord du bailleur : revenus, occupation, prochaines arrivées et demandes à traiter. */
export default function TableauDeBordHote() {
  const { estConnecte, roleUtilisateur } = useAuth();
  const lienConnexion = useLienConnexion();
  const estBailleur = estConnecte && roleUtilisateur === "Bailleur";
  const [anneeCourante] = useState(() => new Date().getFullYear());
  const [annee, setAnnee] = useState(anneeCourante);
  const {
    donnees: d,
    chargement,
    erreur,
  } = useChargement<TableauDeBordHoteDto>(estBailleur ? `bailleur/tableau-de-bord?annee=${annee}` : null);

  if (!estBailleur) {
    return (
      <div className="px-6 py-16 max-w-xl mx-auto text-center text-gray-600">
        <Link to={lienConnexion} className="text-immo-gold font-semibold">
          Connectez-vous
        </Link>{" "}
        avec un compte Bailleur pour voir votre tableau de bord.
      </div>
    );
  }

  const annees = Array.from({ length: 5 }, (_, i) => anneeCourante - i);

  return (
    <div className="px-4 sm:px-6 py-10">
      <div className="max-w-6xl mx-auto">
        <div className="flex flex-wrap items-end justify-between gap-4 mb-8">
          <div>
            <h1 className="text-2xl md:text-3xl font-extrabold mb-1">Tableau de bord</h1>
            <p className="text-gray-500 text-sm">Revenus, occupation de vos logements et séjours à venir.</p>
          </div>
          <label className="text-sm font-medium text-gray-700 flex items-center gap-2">
            Année
            <select
              value={annee}
              onChange={(e) => setAnnee(Number(e.target.value))}
              className="border border-gray-300 rounded-lg px-3 py-2 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-immo-gold/40"
            >
              {annees.map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </select>
          </label>
        </div>

        {chargement && !d ? (
          <p className="text-gray-500 text-sm">Chargement...</p>
        ) : erreur ? (
          <MessageErreur>{erreur}</MessageErreur>
        ) : d ? (
          <div className="flex flex-col gap-6">
            <div className="grid grid-cols-1 min-[480px]:grid-cols-2 lg:grid-cols-4 gap-4">
              <Chiffre
                label={`Revenus nets ${d.annee}`}
                valeur={formaterMontant(d.revenusTotal)}
                detail={
                  (d.remboursementsTotal ?? 0) > 0
                    ? `Après ${formaterMontant(d.remboursementsTotal)} remboursés aux voyageurs`
                    : "Encaissé, moins les remboursements"
                }
                icone={Wallet}
              />
              <Chiffre
                label="Taux d'occupation"
                valeur={d.tauxOccupation == null ? "—" : `${Math.round(d.tauxOccupation)} %`}
                detail={d.tauxOccupation == null ? "Pas encore de nuits à mesurer" : "Séjours courts, nuits réservées"}
                icone={BedDouble}
              />
              <Chiffre
                label="Note des voyageurs"
                valeur={
                  d.noteMoyenne == null ? (
                    "—"
                  ) : (
                    <span className="inline-flex items-center gap-1">
                      <Star size={18} className="text-immo-gold" fill="currentColor" />
                      {d.noteMoyenne.toLocaleString("fr-FR", { maximumFractionDigits: 1 })}
                    </span>
                  )
                }
                detail={
                  <span className="inline-flex flex-wrap items-center gap-1.5">
                    {d.nombreAvis} avis
                    {d.superhote && <BadgeSuperhote petit />}
                  </span>
                }
                icone={Star}
              />
              <Chiffre
                label="Taux de réponse"
                valeur={d.tauxReponse == null ? "—" : `${Math.round(d.tauxReponse)} %`}
                detail="Demandes de séjour sur 12 mois"
                icone={Timer}
              />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <Raccourci
                to="/bailleur/reservations"
                label="Demandes de séjour à traiter"
                nombre={d.demandesEnAttente}
                icone={Inbox}
              />
              <Raccourci
                to="/bailleur/reservations"
                label="Messages non lus"
                nombre={d.messagesNonLus}
                icone={MessageCircle}
              />
            </div>

            <Section titre={`Revenus nets par mois en ${d.annee}`} icone={Wallet}>
              <GraphiqueRevenus annee={d.annee} revenus={d.revenusParMois} />
            </Section>

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              <Section titre="Occupation par logement" icone={BedDouble}>
                {d.occupationParLogement.length === 0 ? (
                  <p className="text-sm text-gray-500">Aucun logement en séjour court.</p>
                ) : (
                  <ul className="flex flex-col gap-4">
                    {d.occupationParLogement.map((o) => {
                      const taux = Math.max(0, Math.min(100, o.taux ?? 0));
                      return (
                        <li key={o.appartementId} className="min-w-0">
                          <div className="flex items-baseline justify-between gap-3 text-sm mb-1">
                            <span className="font-semibold truncate min-w-0">{o.adresse}</span>
                            <span className="font-bold shrink-0">{o.taux == null ? "—" : `${Math.round(taux)} %`}</span>
                          </div>
                          <div
                            className="h-2 rounded-full bg-gray-100 overflow-hidden"
                            role="meter"
                            aria-valuemin={0}
                            aria-valuemax={100}
                            aria-valuenow={Math.round(taux)}
                            aria-label={`Occupation de ${o.adresse}`}
                          >
                            <div className="h-full rounded-full bg-immo-gold" style={{ width: `${taux}%` }} />
                          </div>
                          <p className="text-xs text-gray-500 mt-1">
                            {o.nuitsReservees} nuit{o.nuitsReservees > 1 ? "s" : ""} réservée
                            {o.nuitsReservees > 1 ? "s" : ""} sur {o.nuitsPossibles} disponible
                            {o.nuitsPossibles > 1 ? "s" : ""}
                          </p>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </Section>

              <Section titre="Prochaines arrivées (14 jours)" icone={CalendarCheck}>
                {d.prochainesArrivees.length === 0 ? (
                  <p className="text-sm text-gray-500">Aucune arrivée prévue dans les 14 prochains jours.</p>
                ) : (
                  <ul className="divide-y divide-black/5 -my-2">
                    {d.prochainesArrivees.map((a) => {
                      const nuits = ecartJours(a.dateArrivee, a.dateDepart);
                      return (
                        <li key={a.reservationId} className="py-3 flex items-start gap-3 min-w-0">
                          <span className="shrink-0 w-14 text-center rounded-xl bg-immo-dark text-immo-cream py-1.5">
                            <span className="block text-lg font-extrabold leading-none">
                              {formaterJour(a.dateArrivee).slice(0, 2)}
                            </span>
                            <span className="block text-[10px] uppercase tracking-wide text-immo-gold">
                              {new Date(`${a.dateArrivee.slice(0, 10)}T00:00:00Z`).toLocaleDateString("fr-FR", {
                                month: "short",
                                timeZone: "UTC",
                              })}
                            </span>
                          </span>
                          <div className="min-w-0 text-sm">
                            <p className="font-semibold">{a.voyageurNom}</p>
                            <p className="text-gray-500 truncate">{a.appartementAdresse}</p>
                            <p className="text-xs text-gray-500 flex flex-wrap items-center gap-x-2">
                              <span>
                                {nuits} nuit{nuits > 1 ? "s" : ""}, départ le {formaterJour(a.dateDepart)}
                              </span>
                              <span className="inline-flex items-center gap-1">
                                <Users size={12} />
                                {a.nombreVoyageurs}
                              </span>
                              <span>n° {formaterReference(a.reservationId)}</span>
                            </p>
                          </div>
                        </li>
                      );
                    })}
                  </ul>
                )}
                <Link
                  to="/bailleur/reservations"
                  className="inline-flex items-center gap-1 text-sm font-semibold text-immo-gold mt-4"
                >
                  Toutes les réservations
                  <ChevronRight size={15} />
                </Link>
              </Section>
            </div>
          </div>
        ) : null}
      </div>
    </div>
  );
}
