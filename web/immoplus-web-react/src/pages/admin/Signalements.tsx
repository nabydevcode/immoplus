import { useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ChevronLeft, ChevronRight, Flag, Gavel } from "lucide-react";
import { apiClient } from "../../api/client";
import { Badge, EnTete, EtatListe, MessageErreur, Onglets } from "../../components/admin/ui";
import {
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterDateHeure,
  formaterReference,
  messageErreur,
  useChargement,
} from "../../components/admin/outils";
import { lienAdminCible } from "../../components/admin/liens";
import {
  ACTIONS_SIGNALEMENT,
  LIBELLES_CIBLE,
  libelleMotif,
  statutSignalement,
} from "../../components/signalement/signalements";
import { signalerCompteursAdmin } from "../../components/admin/compteurs";
import type {
  ActionSignalement,
  CibleSignalement,
  PagedResultDto,
  SignalementDto,
  StatutSignalement,
} from "../../types/models";

const TAILLE_PAGE = 20;
type Filtre = StatutSignalement | "Tous";

/** Décision de l'admin : statut, explication et action éventuelle sur la cible. */
function FormulaireTraitement({
  signalement,
  onTraite,
  onAnnuler,
}: {
  signalement: SignalementDto;
  onTraite: () => void;
  onAnnuler: () => void;
}) {
  const [statut, setStatut] = useState<"EnCours" | "Traite" | "Rejete">("Traite");
  const [decision, setDecision] = useState(signalement.decisionAdmin ?? "");
  const [action, setAction] = useState<ActionSignalement>("aucune");
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);
  const actions = ACTIONS_SIGNALEMENT.filter((a) => a.cibles.includes(signalement.cible));

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    if (statut !== "EnCours" && !decision.trim())
      return setErreur("Expliquez la décision (visible dans l'historique).");
    if (
      action !== "aucune" &&
      !window.confirm(
        `${ACTIONS_SIGNALEMENT.find((a) => a.valeur === action)?.libelle} ? Les règles habituelles s'appliquent (personnes concernées prévenues).`,
      )
    )
      return;
    setEnCours(true);
    try {
      await apiClient.post(`admin/signalements/${signalement.id}/traiter`, {
        statut,
        decision: decision.trim() || null,
        action: statut === "Traite" ? action : "aucune",
      });
      signalerCompteursAdmin();
      onTraite();
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'enregistrer la décision."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <form onSubmit={envoyer} className="flex flex-col gap-3 bg-immo-cream/60 rounded-xl p-4 mt-4">
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <label className="text-xs font-semibold text-gray-600">
          Statut
          <select value={statut} onChange={(e) => setStatut(e.target.value as typeof statut)} className={classeChamp}>
            <option value="EnCours">En cours d'examen</option>
            <option value="Traite">Traité (fondé)</option>
            <option value="Rejete">Rejeté (non fondé)</option>
          </select>
        </label>
        {statut === "Traite" && (
          <label className="text-xs font-semibold text-gray-600">
            Action sur la cible
            <select
              value={action}
              onChange={(e) => setAction(e.target.value as ActionSignalement)}
              className={classeChamp}
            >
              {actions.map((a) => (
                <option key={a.valeur} value={a.valeur}>
                  {a.libelle}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>
      <label className="text-xs font-semibold text-gray-600">
        Décision
        <textarea
          value={decision}
          onChange={(e) => setDecision(e.target.value)}
          rows={3}
          maxLength={1000}
          placeholder="Ce qui a été constaté et décidé..."
          className={classeChamp}
        />
      </label>
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={enCours} className={classeBoutonPrincipal(true)}>
          {enCours ? "Enregistrement..." : "Enregistrer la décision"}
        </button>
        <button type="button" onClick={onAnnuler} className={classeBoutonSecondaire(true)}>
          Annuler
        </button>
      </div>
    </form>
  );
}

/** Cible signalée et liens utiles (annonce, réservation, location, compte concerné). */
function CibleSignalee({ s }: { s: SignalementDto }) {
  const libelle = `${LIBELLES_CIBLE[s.cible as CibleSignalement] ?? s.cible} n° ${formaterReference(s.cibleId)}`;
  const lien = lienAdminCible(s.cible, s.cibleId, s.cible === "Utilisateur" ? s.cibleResume : null);
  const liens = [
    s.cible !== "Appartement" && s.appartementId != null
      ? { texte: "Annonce", to: lienAdminCible("Appartement", s.appartementId) }
      : null,
    s.reservationId != null ? { texte: "Réservation", to: lienAdminCible("Reservation", s.reservationId) } : null,
    s.locationId != null ? { texte: "Location", to: lienAdminCible("Location", s.locationId) } : null,
    s.cible !== "Utilisateur" && s.utilisateurCibleId != null
      ? { texte: "Historique du compte concerné", to: `/admin/journal?utilisateurId=${s.utilisateurCibleId}` }
      : null,
  ].filter((l): l is { texte: string; to: string } => !!l?.to);
  return (
    <span className="block min-w-0">
      {lien ? (
        <Link to={lien} className="font-semibold underline underline-offset-2 hover:text-immo-gold">
          {libelle}
        </Link>
      ) : (
        <span className="font-semibold">{libelle}</span>
      )}
      {s.cibleResume && <span className="block text-xs text-gray-500 wrap-break-word">« {s.cibleResume} »</span>}
      {liens.length > 0 && (
        <span className="flex flex-wrap gap-x-3 gap-y-1 mt-1 text-xs">
          {liens.map((l) => (
            <Link key={l.texte} to={l.to} className="underline text-gray-600 hover:text-immo-gold">
              {l.texte}
            </Link>
          ))}
        </span>
      )}
    </span>
  );
}

/** Signalements envoyés par les utilisateurs et visiteurs : examen, décision et action sur la cible. */
export default function Signalements() {
  const [params] = useSearchParams();
  const idCible = Number(params.get("id")) || null;
  const [filtre, setFiltre] = useState<Filtre>(idCible ? "Tous" : "Nouveau");
  const [cible, setCible] = useState<"" | CibleSignalement>("");
  const [page, setPage] = useState(1);
  const [ouvert, setOuvert] = useState<number | null>(idCible);
  const requete = new URLSearchParams({ page: String(page), taille: String(TAILLE_PAGE) });
  if (filtre !== "Tous") requete.set("statut", filtre);
  if (cible) requete.set("cible", cible);
  const { donnees, chargement, erreur, recharger } = useChargement<PagedResultDto<SignalementDto>>(
    `admin/signalements?${requete}`,
  );
  const nouveaux = useChargement<PagedResultDto<SignalementDto>>("admin/signalements?statut=Nouveau&taille=1");
  const enCours = useChargement<PagedResultDto<SignalementDto>>("admin/signalements?statut=EnCours&taille=1");
  const signalements = donnees?.donnees ?? [];
  const totalPages = Math.max(donnees?.totalPages ?? 1, 1);
  const changerFiltre = (f: Filtre) => {
    setFiltre(f);
    setPage(1);
  };

  return (
    <>
      <EnTete
        titre="Signalements"
        description="Annonces, comptes, avis et messages signalés. Examinez, décidez et appliquez une action si besoin."
      />

      <div className="flex flex-wrap items-start justify-between gap-3">
        <Onglets
          valeur={filtre}
          onChange={changerFiltre}
          options={[
            {
              valeur: "Nouveau",
              label: "Nouveaux",
              pastille: nouveaux.donnees?.totalElements,
              pastilleTitre: "signalement(s) à examiner",
            },
            { valeur: "EnCours", label: `En cours${enCours.donnees ? ` (${enCours.donnees.totalElements})` : ""}` },
            { valeur: "Traite", label: "Traités" },
            { valeur: "Rejete", label: "Rejetés" },
            { valeur: "Tous", label: "Tous" },
          ]}
        />
        <label className="text-xs font-semibold text-gray-600 w-48 mb-6">
          Type de cible
          <select
            value={cible}
            onChange={(e) => {
              setCible(e.target.value as "" | CibleSignalement);
              setPage(1);
            }}
            className={classeChamp}
          >
            <option value="">Toutes</option>
            {Object.entries(LIBELLES_CIBLE).map(([valeur, libelle]) => (
              <option key={valeur} value={valeur}>
                {libelle}
              </option>
            ))}
          </select>
        </label>
      </div>

      <EtatListe
        chargement={chargement}
        erreur={erreur}
        vide={signalements.length === 0}
        messageVide={filtre === "Nouveau" ? "Aucun nouveau signalement." : "Aucun signalement."}
      >
        <ul className="flex flex-col gap-4">
          {signalements.map((s) => {
            const statut = statutSignalement(s.statut);
            const ouvertIci = ouvert === s.id;
            const clos = statut.valeur === "Traite" || statut.valeur === "Rejete";
            return (
              <li
                key={s.id}
                className={`bg-white rounded-2xl border shadow-sm p-4 sm:p-5 text-sm ${
                  idCible === s.id ? "border-immo-gold" : "border-black/5"
                }`}
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="flex items-start gap-3 min-w-0">
                    <span className="flex items-center justify-center w-9 h-9 rounded-full bg-red-50 text-red-600 shrink-0">
                      <Flag size={16} />
                    </span>
                    <div className="min-w-0">
                      <p className="font-bold">{libelleMotif(s.motif)}</p>
                      <CibleSignalee s={s} />
                    </div>
                  </div>
                  <Badge ton={statut.ton}>{statut.libelle}</Badge>
                </div>
                <p className="mt-3 text-gray-700 whitespace-pre-line wrap-break-word">{s.texte}</p>
                <p className="mt-2 text-xs text-gray-500">
                  Signalé le {formaterDateHeure(s.dateCreation)} par{" "}
                  {s.auteurId != null ? (
                    <Link
                      to={lienAdminCible("Utilisateur", s.auteurId, s.auteurNom) ?? "#"}
                      className="underline hover:text-immo-gold"
                    >
                      {s.auteurNom ?? `compte n° ${formaterReference(s.auteurId)}`}
                    </Link>
                  ) : (
                    <span>{s.auteurEmail ?? "un visiteur"} (non connecté)</span>
                  )}
                  {s.auteurId != null && s.auteurEmail && ` · ${s.auteurEmail}`} · réf. {formaterReference(s.id)}
                </p>
                {s.decisionAdmin && (
                  <div className="mt-3 border-l-2 border-immo-gold/50 pl-3">
                    <p className="text-xs font-semibold text-gray-700">
                      Décision{s.traiteParNom ? ` de ${s.traiteParNom}` : ""}
                      {s.dateTraitement ? ` · ${formaterDateHeure(s.dateTraitement)}` : ""}
                    </p>
                    <p className="text-gray-600 whitespace-pre-line wrap-break-word">{s.decisionAdmin}</p>
                    {s.actionAppliquee && s.actionAppliquee !== "aucune" && (
                      <p className="text-xs font-semibold text-red-700 mt-1">
                        Action appliquée :{" "}
                        {ACTIONS_SIGNALEMENT.find((x) => x.valeur === s.actionAppliquee)?.libelle ?? s.actionAppliquee}
                      </p>
                    )}
                  </div>
                )}
                {ouvertIci ? (
                  <FormulaireTraitement
                    signalement={s}
                    onAnnuler={() => setOuvert(null)}
                    onTraite={() => {
                      setOuvert(null);
                      recharger();
                      nouveaux.recharger();
                      enCours.recharger();
                    }}
                  />
                ) : (
                  !clos && (
                    <button
                      type="button"
                      onClick={() => setOuvert(s.id)}
                      className={`${classeBoutonSecondaire(true)} mt-4`}
                    >
                      <Gavel size={14} />
                      Traiter
                    </button>
                  )
                )}
              </li>
            );
          })}
        </ul>
        {totalPages > 1 && (
          <div className="flex items-center justify-center gap-3 mt-4 text-sm">
            <button
              type="button"
              onClick={() => setPage((p) => Math.max(p - 1, 1))}
              disabled={page <= 1}
              className={classeBoutonSecondaire(true)}
            >
              <ChevronLeft size={14} />
              Précédents
            </button>
            <span className="text-gray-500">
              Page {page} / {totalPages}
            </span>
            <button
              type="button"
              onClick={() => setPage((p) => Math.min(p + 1, totalPages))}
              disabled={page >= totalPages}
              className={classeBoutonSecondaire(true)}
            >
              Suivants
              <ChevronRight size={14} />
            </button>
          </div>
        )}
      </EtatListe>
    </>
  );
}
