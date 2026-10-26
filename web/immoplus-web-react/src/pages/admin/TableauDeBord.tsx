import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import {
  AlertTriangle,
  CalendarClock,
  CalendarDays,
  ChevronRight,
  ClipboardList,
  Eye,
  FileText,
  HandCoins,
  Home,
  KeyRound,
  LogOut,
  Users,
  Wallet,
  type LucideIcon,
} from "lucide-react";
import { Badge, EnTete, MessageErreur } from "../../components/admin/ui";
import { formaterDate, formaterMontant, formaterReference, useChargement } from "../../components/admin/outils";
import { useTempsReel } from "../../components/messages/tempsReel";
import type { TableauDeBordDto } from "../../types/models";

const refDossier = (id: number) => `Dossier n° ${formaterReference(id)}`;
const refLocation = (id: number) => `Location n° ${formaterReference(id)}`;
/** Référence du dossier, ou de la location quand elle ne provient pas d'une candidature. */
const refDossierOuLocation = (candidatureId: number | null, locationId: number) =>
  candidatureId !== null ? refDossier(candidatureId) : refLocation(locationId);
/** Lien vers une page admin filtrée sur une référence (l'élément s'ouvre s'il est seul). */
const lien = (page: string, id: number) => `/admin/${page}?q=${formaterReference(id)}`;

interface TuileProps {
  to: string;
  label: string;
  valeur: ReactNode;
  detail?: string;
  icone: LucideIcon;
  ton?: "normal" | "attente" | "danger";
}

function Tuile({ to, label, valeur, detail, icone: Icone, ton = "normal" }: TuileProps) {
  const couleurs = {
    normal: { icone: "bg-immo-gold/15 text-immo-gold", bord: "border-black/5", texte: "text-gray-500" },
    attente: { icone: "bg-amber-100 text-amber-700", bord: "border-amber-200", texte: "text-amber-700" },
    danger: { icone: "bg-red-100 text-red-600", bord: "border-red-200", texte: "text-red-600" },
  }[ton];
  return (
    <Link
      to={to}
      className={`bg-white rounded-2xl border ${couleurs.bord} shadow-sm p-4 flex items-start gap-3 hover:shadow-md transition-shadow min-w-0`}
    >
      <span className={`flex items-center justify-center w-10 h-10 rounded-xl shrink-0 ${couleurs.icone}`}>
        <Icone size={18} />
      </span>
      <div className="min-w-0">
        <p className="text-sm text-gray-500">{label}</p>
        <p className={`text-2xl font-extrabold leading-tight ${ton === "danger" ? "text-red-600" : ""}`}>{valeur}</p>
        {detail && <p className={`text-xs font-medium mt-0.5 wrap-break-word ${couleurs.texte}`}>{detail}</p>}
      </div>
    </Link>
  );
}

function SectionAFaire({
  titre,
  icone: Icone,
  nombre,
  children,
}: {
  titre: string;
  icone: LucideIcon;
  nombre: number;
  children: ReactNode;
}) {
  return (
    <section className="bg-white rounded-2xl border border-black/5 shadow-sm min-w-0">
      <h2 className="flex items-center gap-2 px-5 py-4 border-b border-black/5 font-bold">
        <Icone size={18} className="text-immo-gold shrink-0" />
        <span className="flex-1">{titre}</span>
        <Badge ton={nombre > 0 ? "attente" : "neutre"}>{nombre}</Badge>
      </h2>
      {nombre === 0 ? <p className="px-5 py-4 text-sm text-gray-400">Rien à faire</p> : <ul>{children}</ul>}
    </section>
  );
}

function LigneAFaire({
  to,
  reference,
  titre,
  sousTitre,
  action,
  badge,
}: {
  to: string;
  reference: string;
  titre: string;
  sousTitre: ReactNode;
  action: ReactNode;
  badge?: ReactNode;
}) {
  return (
    <li className="not-last:border-b border-black/5">
      <Link to={to} className="flex items-center gap-3 px-5 py-3 hover:bg-immo-cream/60 transition-colors">
        <div className="flex-1 min-w-0">
          <p className="text-sm font-semibold flex flex-wrap items-center gap-x-2 gap-y-1">
            <span className="text-gray-400 text-xs font-medium whitespace-nowrap">{reference}</span>
            <span className="wrap-break-word">{titre}</span>
            {badge}
          </p>
          <p className="text-xs text-gray-500 wrap-break-word">{sousTitre}</p>
          <p className="text-xs font-semibold text-immo-dark mt-1">{action}</p>
        </div>
        <ChevronRight size={16} className="text-gray-400 shrink-0" />
      </Link>
    </li>
  );
}

export default function TableauDeBord() {
  const { donnees: tdb, chargement, erreur, recharger } = useChargement<TableauDeBordDto>("admin/tableau-de-bord");
  // « Comptes à valider » à jour sans actualiser (inscription confirmée, compte validé par un autre admin…).
  useTempsReel("utilisateurChange", (e) => {
    if (e.type !== "profilModifie") recharger();
  });

  return (
    <>
      <EnTete titre="Tableau de bord" description="Ce qui demande votre attention aujourd'hui." />
      {chargement ? (
        <p className="text-gray-500 text-sm">Chargement...</p>
      ) : erreur || !tdb ? (
        <MessageErreur>{erreur ?? "Impossible de charger le tableau de bord."}</MessageErreur>
      ) : (
        <>
          <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4 mb-8">
            <Tuile
              to="/admin/utilisateurs"
              label="Comptes à valider"
              valeur={tdb.utilisateursAValider}
              icone={Users}
              ton={tdb.utilisateursAValider > 0 ? "attente" : "normal"}
            />
            <Tuile
              to="/admin/candidatures"
              label="Candidatures à étudier"
              valeur={tdb.candidaturesAEtudier}
              icone={FileText}
              ton={tdb.candidaturesAEtudier > 0 ? "attente" : "normal"}
            />
            <Tuile to="/admin/candidatures" label="Visites à venir" valeur={tdb.visitesAVenir} icone={Eye} />
            <Tuile
              to="/admin/financements"
              label="Retards de paiement"
              valeur={tdb.retards.nombre}
              detail={tdb.retards.nombre > 0 ? `${formaterMontant(tdb.retards.montant)} dû` : "Aucun retard"}
              icone={AlertTriangle}
              ton={tdb.retards.nombre > 0 ? "danger" : "normal"}
            />
            <Tuile to="/admin/locations" label="Locations en cours" valeur={tdb.locationsEnCours} icone={KeyRound} />
            <Tuile to="/admin/appartements" label="Logements" valeur={tdb.logements} icone={Home} />
            <Tuile
              to="/admin/reservations"
              label="Réservations à encaisser"
              valeur={tdb.reservationsEnAttentePaiement}
              detail="En attente de paiement"
              icone={Wallet}
              ton={tdb.reservationsEnAttentePaiement > 0 ? "attente" : "normal"}
            />
            <Tuile
              to="/admin/reservations"
              label="Arrivées sous 7 jours"
              valeur={tdb.reservationsAVenir7Jours}
              detail="Réservations à venir"
              icone={CalendarDays}
            />
          </div>

          <h2 className="text-lg font-extrabold mb-4">À faire</h2>
          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            <SectionAFaire
              titre="Dossiers à prendre en charge"
              icone={ClipboardList}
              nombre={tdb.dossiersAPrendreEnCharge.length}
            >
              {tdb.dossiersAPrendreEnCharge.map((d) => (
                <LigneAFaire
                  key={d.candidatureId}
                  to={lien("candidatures", d.candidatureId)}
                  reference={refDossier(d.candidatureId)}
                  titre={d.locataireNom}
                  sousTitre={`${d.appartementAdresse} · attribué le ${formaterDate(d.dateAttribution)}`}
                  action={`Enregistrer l'apport (${formaterMontant(d.apportInitial)})`}
                />
              ))}
            </SectionAFaire>

            <SectionAFaire titre="Clés à remettre" icone={KeyRound} nombre={tdb.clesARemettre.length}>
              {tdb.clesARemettre.map((c) => (
                <LigneAFaire
                  key={c.locationId}
                  to={
                    c.candidatureId !== null ? lien("candidatures", c.candidatureId) : lien("locations", c.locationId)
                  }
                  reference={refDossierOuLocation(c.candidatureId, c.locationId)}
                  titre={c.locataireNom}
                  sousTitre={`${c.appartementAdresse} · apport payé le ${formaterDate(c.dateApportPaye)}`}
                  action="Remettre les clés"
                />
              ))}
            </SectionAFaire>

            <SectionAFaire titre="Échéanciers à générer" icone={Wallet} nombre={tdb.echeanciersAGenerer.length}>
              {tdb.echeanciersAGenerer.map((e) => (
                <LigneAFaire
                  key={e.financementId}
                  to={lien("financements", e.financementId)}
                  reference={refDossierOuLocation(e.candidatureId, e.locationId)}
                  titre={e.locataireNom}
                  sousTitre={`Montant à rembourser : ${formaterMontant(e.montantARembourser)}`}
                  action="Générer l'échéancier"
                />
              ))}
            </SectionAFaire>

            <SectionAFaire titre="Retards de paiement" icone={AlertTriangle} nombre={tdb.retards.liste.length}>
              {tdb.retards.liste.map((r) => (
                <LigneAFaire
                  key={`${r.type}-${r.locationId}-${r.dateEcheance}`}
                  to={r.type === "Loyer" ? lien("locations", r.locationId) : lien("financements", r.locationId)}
                  reference={
                    r.type === "Loyer" ? refLocation(r.locationId) : refDossierOuLocation(r.candidatureId, r.locationId)
                  }
                  titre={r.locataireNom}
                  badge={<Badge ton="danger">{r.type === "Loyer" ? "Loyer" : "Mensualité"}</Badge>}
                  sousTitre={`Échéance du ${formaterDate(r.dateEcheance)} · ${r.joursRetard} jour${
                    r.joursRetard > 1 ? "s" : ""
                  } de retard · reste dû ${formaterMontant(r.resteDu)}`}
                  action="Relancer / encaisser"
                />
              ))}
            </SectionAFaire>

            <SectionAFaire titre="Préavis en cours" icone={LogOut} nombre={tdb.preavisEnCours.length}>
              {tdb.preavisEnCours.map((p) => (
                <LigneAFaire
                  key={p.locationId}
                  to={lien("locations", p.locationId)}
                  reference={refLocation(p.locationId)}
                  titre={p.locataireNom}
                  badge={p.finAtteinte ? <Badge ton="danger">À clôturer</Badge> : undefined}
                  sousTitre={p.appartementAdresse}
                  action={
                    <span className="inline-flex items-center gap-1">
                      <CalendarClock size={12} />
                      Préavis : fin le {formaterDate(p.dateFinPrevue)}
                    </span>
                  }
                />
              ))}
            </SectionAFaire>

            <SectionAFaire
              titre="Remboursements à effectuer"
              icone={HandCoins}
              nombre={tdb.remboursementsAEffectuer?.length ?? 0}
            >
              {(tdb.remboursementsAEffectuer ?? []).map((r) => (
                <LigneAFaire
                  key={r.reservationId}
                  to={lien("reservations", r.reservationId)}
                  reference={`Réservation n° ${formaterReference(r.reservationId)}`}
                  titre={r.voyageurNom}
                  sousTitre={`Annulée${r.dateAnnulation ? ` le ${formaterDate(r.dateAnnulation)}` : ""}${
                    r.montantRembourse ? ` · déjà remboursé ${formaterMontant(r.montantRembourse)}` : ""
                  }`}
                  action={`Rembourser ${formaterMontant(r.montantARembourser - (r.montantRembourse ?? 0))}`}
                />
              ))}
            </SectionAFaire>
          </div>
        </>
      )}
    </>
  );
}
