import { useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ChevronLeft, ChevronRight, Search, X } from "lucide-react";
import { Badge, EnTete, EtatListe, Tableau } from "../../components/admin/ui";
import {
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterDateHeure,
  formaterReference,
  useChargement,
} from "../../components/admin/outils";
import { LIBELLES_CIBLES_JOURNAL, lienAdminCible } from "../../components/admin/liens";
import type { JournalActionDto, PagedResultDto } from "../../types/models";

const TAILLE_PAGE = 25;
const FILTRES = ["q", "action", "cible", "cibleId", "utilisateurId", "du", "au"] as const;

const tonRole = (role: string) =>
  role === "Admin" ? "info" : role === "Systeme" || role === "Anonyme" ? "neutre" : "attente";

/** Cible lisible d'une action, avec un lien vers la page d'administration quand il y en a une. */
function CibleAction({ action }: { action: JournalActionDto }) {
  const libelle = `${LIBELLES_CIBLES_JOURNAL[action.cible] ?? action.cible}${
    action.cibleId != null ? ` n° ${formaterReference(action.cibleId)}` : ""
  }`;
  const lien = lienAdminCible(action.cible, action.cibleId);
  return lien ? (
    <Link to={lien} className="text-immo-dark font-semibold underline underline-offset-2 hover:text-immo-gold">
      {libelle}
    </Link>
  ) : (
    <span>{libelle}</span>
  );
}

/** Filtres du journal, initialisés depuis l'URL ; « Filtrer » réécrit l'URL. */
function FiltresJournal({
  params,
  onAppliquer,
}: {
  params: URLSearchParams;
  onAppliquer: (suivants: URLSearchParams) => void;
}) {
  const [q, setQ] = useState(params.get("q") ?? "");
  const [cible, setCible] = useState(params.get("cible") ?? "");
  const [action, setAction] = useState(params.get("action") ?? "");
  const [du, setDu] = useState(params.get("du") ?? "");
  const [au, setAu] = useState(params.get("au") ?? "");

  const appliquer = (e: FormEvent) => {
    e.preventDefault();
    const suivants = new URLSearchParams();
    for (const cle of ["utilisateurId", "cibleId"] as const) {
      const valeur = params.get(cle);
      if (valeur) suivants.set(cle, valeur);
    }
    const valeurs = { q, cible, action, du, au };
    for (const [cle, valeur] of Object.entries(valeurs)) if (valeur.trim()) suivants.set(cle, valeur.trim());
    onAppliquer(suivants);
  };

  return (
    <form
      onSubmit={appliquer}
      className="bg-white rounded-2xl border border-black/5 shadow-sm p-4 mb-6 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-[2fr_1fr_1fr_1fr_1fr_auto] gap-3 items-end"
    >
      <label className="text-xs font-semibold text-gray-600">
        Recherche
        <input
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder="Nom, détail, adresse IP…"
          className={classeChamp}
        />
      </label>
      <label className="text-xs font-semibold text-gray-600">
        Cible
        <select value={cible} onChange={(e) => setCible(e.target.value)} className={classeChamp}>
          <option value="">Toutes</option>
          {Object.entries(LIBELLES_CIBLES_JOURNAL).map(([valeur, libelle]) => (
            <option key={valeur} value={valeur}>
              {libelle}
            </option>
          ))}
        </select>
      </label>
      <label className="text-xs font-semibold text-gray-600">
        Action
        <input
          value={action}
          onChange={(e) => setAction(e.target.value)}
          placeholder="ex. connexion"
          className={classeChamp}
        />
      </label>
      <label className="text-xs font-semibold text-gray-600">
        Du
        <input type="date" value={du} onChange={(e) => setDu(e.target.value)} className={classeChamp} />
      </label>
      <label className="text-xs font-semibold text-gray-600">
        Au
        <input
          type="date"
          value={au}
          min={du || undefined}
          onChange={(e) => setAu(e.target.value)}
          className={classeChamp}
        />
      </label>
      <button type="submit" className={`${classeBoutonPrincipal()} h-[38px]`}>
        <Search size={16} />
        Filtrer
      </button>
    </form>
  );
}

/** Journal d'audit : qui a fait quoi, quand (connexions, décisions, paiements, exports…). Conservé 2 ans. */
export default function Journal() {
  // Les filtres vivent dans l'URL : liens directs (historique d'un compte, d'un dossier) et retour arrière.
  const [params, setParams] = useSearchParams();
  const page = Math.max(Number(params.get("page")) || 1, 1);
  const requete = new URLSearchParams({ page: String(page), taille: String(TAILLE_PAGE) });
  for (const cle of FILTRES) {
    const valeur = params.get(cle);
    if (valeur) requete.set(cle, valeur);
  }
  const { donnees, chargement, erreur } = useChargement<PagedResultDto<JournalActionDto>>(`admin/journal?${requete}`);
  const actions = donnees?.donnees ?? [];
  const totalPages = Math.max(donnees?.totalPages ?? 1, 1);

  const changerPage = (p: number) => {
    const suivants = new URLSearchParams(params);
    suivants.set("page", String(p));
    setParams(suivants);
  };
  const retirer = (cle: string) => {
    const suivants = new URLSearchParams(params);
    suivants.delete(cle);
    suivants.delete("page");
    setParams(suivants);
  };
  const toutEffacer = () => setParams({});
  const filtresActifs = FILTRES.some((cle) => params.get(cle));
  // Le formulaire repart des valeurs de l'URL quand elle change (lien vers l'historique d'un compte…).
  const cleFiltres = FILTRES.map((cle) => params.get(cle) ?? "").join("|");

  return (
    <>
      <EnTete
        titre="Journal"
        description={`Actions enregistrées sur la plateforme, de la plus récente à la plus ancienne (conservées 2 ans)${
          donnees ? ` · ${donnees.totalElements} au total` : ""
        }.`}
      />

      <FiltresJournal key={cleFiltres} params={params} onAppliquer={setParams} />

      {(params.get("utilisateurId") || params.get("cibleId") || filtresActifs) && (
        <div className="flex flex-wrap items-center gap-2 mb-4 text-sm">
          {params.get("utilisateurId") && (
            <span className="inline-flex items-center gap-1.5 bg-immo-gold/15 text-immo-dark text-xs font-semibold pl-3 pr-1.5 py-1.5 rounded-full">
              Compte n° {formaterReference(Number(params.get("utilisateurId")))}
              <button
                type="button"
                onClick={() => retirer("utilisateurId")}
                aria-label="Retirer le filtre de compte"
                className="w-5 h-5 rounded-full hover:bg-black/10 flex items-center justify-center"
              >
                <X size={13} />
              </button>
            </span>
          )}
          {params.get("cibleId") && (
            <span className="inline-flex items-center gap-1.5 bg-immo-gold/15 text-immo-dark text-xs font-semibold pl-3 pr-1.5 py-1.5 rounded-full">
              Objet n° {formaterReference(Number(params.get("cibleId")))}
              <button
                type="button"
                onClick={() => retirer("cibleId")}
                aria-label="Retirer le filtre d'objet"
                className="w-5 h-5 rounded-full hover:bg-black/10 flex items-center justify-center"
              >
                <X size={13} />
              </button>
            </span>
          )}
          {filtresActifs && (
            <button type="button" onClick={toutEffacer} className="underline font-semibold text-gray-600">
              Effacer les filtres
            </button>
          )}
        </div>
      )}

      <EtatListe
        chargement={chargement}
        erreur={erreur}
        vide={actions.length === 0}
        messageVide="Aucune action ne correspond à ces critères."
      >
        {/* Mobile : une carte par action ; à partir de md : tableau. */}
        <ul className="md:hidden flex flex-col gap-3">
          {actions.map((a) => (
            <li key={a.id} className="bg-white rounded-2xl border border-black/5 shadow-sm p-4 text-sm">
              <div className="flex items-start justify-between gap-3">
                <p className="min-w-0">
                  <span className="block font-semibold truncate">{a.utilisateurNom || "-"}</span>
                  <span className="text-xs text-gray-500">{formaterDateHeure(a.date)}</span>
                </p>
                <Badge ton={tonRole(a.role)}>{a.role || "-"}</Badge>
              </div>
              <p className="mt-2 font-mono text-xs text-gray-500 break-all">{a.action}</p>
              <p className="mt-1 text-gray-700 wrap-break-word">{a.details}</p>
              <p className="mt-2 text-xs text-gray-500 flex flex-wrap gap-x-3">
                <CibleAction action={a} />
                {a.adresseIp && <span>IP {a.adresseIp}</span>}
              </p>
            </li>
          ))}
        </ul>
        <div className="hidden md:block">
          <Tableau entetes={["Date", "Auteur", "Action", "Cible", "Détails", "IP"]}>
            {actions.map((a) => (
              <tr key={a.id} className="border-b border-black/5 last:border-0 align-top">
                <td className="px-5 py-3 whitespace-nowrap text-gray-600">{formaterDateHeure(a.date)}</td>
                <td className="px-5 py-3 whitespace-nowrap">
                  {a.utilisateurId != null ? (
                    <Link
                      to={`/admin/journal?utilisateurId=${a.utilisateurId}`}
                      className="block font-semibold hover:text-immo-gold"
                      title="Voir toutes les actions de ce compte"
                    >
                      {a.utilisateurNom}
                    </Link>
                  ) : (
                    <span className="block font-semibold">{a.utilisateurNom || "-"}</span>
                  )}
                  <Badge ton={tonRole(a.role)}>{a.role || "-"}</Badge>
                </td>
                <td className="px-5 py-3 whitespace-nowrap font-mono text-xs text-gray-600">{a.action}</td>
                <td className="px-5 py-3 whitespace-nowrap">
                  <CibleAction action={a} />
                </td>
                <td className="px-5 py-3 min-w-64 max-w-md text-gray-700 wrap-break-word">{a.details}</td>
                <td className="px-5 py-3 whitespace-nowrap text-xs text-gray-500">{a.adresseIp ?? "-"}</td>
              </tr>
            ))}
          </Tableau>
        </div>
        {totalPages > 1 && (
          <div className="flex items-center justify-center gap-3 mt-4 text-sm">
            <button
              type="button"
              onClick={() => changerPage(page - 1)}
              disabled={page <= 1}
              className={classeBoutonSecondaire(true)}
            >
              <ChevronLeft size={14} />
              Précédentes
            </button>
            <span className="text-gray-500">
              Page {page} / {totalPages}
            </span>
            <button
              type="button"
              onClick={() => changerPage(page + 1)}
              disabled={page >= totalPages}
              className={classeBoutonSecondaire(true)}
            >
              Suivantes
              <ChevronRight size={14} />
            </button>
          </div>
        )}
      </EtatListe>
    </>
  );
}
