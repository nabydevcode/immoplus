import { useEffect, useRef, useState, type FormEvent } from "react";
import { Ban, Check, MailCheck, MailWarning, RotateCcw, Search, ShieldPlus, X } from "lucide-react";
import { useSearchParams } from "react-router-dom";
import { apiClient } from "../../api/client";
import { Badge, EnTete, EtatListe, MessageErreur, Onglets, Tableau } from "../../components/admin/ui";
import {
  classeBoutonDanger,
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterDate,
  messageErreur,
  useChargement,
} from "../../components/admin/outils";
import { useAuth } from "../../context/auth";
import CreationAdministrateur from "../../components/admin/CreationAdministrateur";
import { useTempsReel } from "../../components/messages/tempsReel";
import type { PagedResultDto, UtilisateurDto } from "../../types/models";

const statuts = {
  EnAttente: { label: "En attente", ton: "attente" },
  Valide: { label: "Actif", ton: "succes" },
  Refuse: { label: "Refusé", ton: "danger" },
  Desactive: { label: "Désactivé", ton: "neutre" },
} as const;

/** « EnAttente » = à valider (en attente ET email confirmé) ; « NonConfirme » = en attente, email non confirmé. */
type Filtre = "EnAttente" | "NonConfirme" | "Valide" | "Desactive" | "Tous";
type Action = "valider" | "refuser" | "desactiver" | "reactiver";

const TAILLE_PAGE = 20;
/** Durée de la surbrillance d'une ligne modifiée par un autre (inscription, confirmation, autre admin). */
const DUREE_SURBRILLANCE = 2500;
/** Plusieurs événements rapprochés : une seule relecture. */
const DELAI_RELECTURE = 250;

const pluriel = (n: number, mot: string) => `${n} ${mot}${n > 1 ? "s" : ""}`;

/** Message affiché après la désactivation (réservations d'un hôte annulées ou encore à traiter). */
function bilanDesactivation(u: UtilisateurDto) {
  const morceaux = [`Le compte de ${u.nom} est désactivé.`];
  const annulees = u.reservationsAnnulees ?? 0;
  const aVenir = u.reservationsConfirmeesAVenir ?? 0;
  if (annulees > 0)
    morceaux.push(
      `${pluriel(annulees, "réservation")} non confirmée${annulees > 1 ? "s ont été annulées" : " a été annulée"} ; les voyageurs sont prévenus et remboursés de ce qu'ils avaient payé.`,
    );
  if (aVenir > 0)
    morceaux.push(
      `${pluriel(aVenir, "réservation")} confirmée${aVenir > 1 ? "s" : ""} à venir ${aVenir > 1 ? "restent" : "reste"} à traiter (menu Réservations).`,
    );
  return { texte: morceaux.join(" "), alerte: aVenir > 0 };
}
/** Filtres de l'API pour chaque onglet (statut et confirmation de l'adresse email). */
function filtresOnglet(filtre: Filtre): Record<string, string> {
  if (filtre === "EnAttente") return { statut: "EnAttente", emailConfirme: "true" };
  if (filtre === "NonConfirme") return { statut: "EnAttente", emailConfirme: "false" };
  if (filtre === "Tous") return {};
  return { statut: filtre };
}
/** Nombre de comptes d'un onglet (seul totalElements est lu). */
const cheminCompte = (filtre: Filtre) =>
  `utilisateur/lister?${new URLSearchParams({ ...filtresOnglet(filtre), taille: "1" })}`;

export default function Utilisateurs() {
  const { utilisateurId } = useAuth();
  // Lien direct (?q=nom, depuis le journal ou un signalement) : la page s'ouvre sur la recherche, tous statuts.
  const [params] = useSearchParams();
  const qInitial = params.get("q") ?? "";
  const [filtre, setFiltre] = useState<Filtre>(qInitial ? "Tous" : "EnAttente");
  const [page, setPage] = useState(1);
  // Recherche côté serveur (nom, email, téléphone), lancée à la validation du champ.
  const [saisie, setSaisie] = useState(qInitial);
  const [recherche, setRecherche] = useState(qInitial);

  const parametres = new URLSearchParams({ ...filtresOnglet(filtre), page: String(page), taille: String(TAILLE_PAGE) });
  if (recherche) parametres.set("q", recherche);
  const { donnees, setDonnees, chargement, erreur, recharger } = useChargement<PagedResultDto<UtilisateurDto>>(
    `utilisateur/lister?${parametres}`,
  );
  const enAttente = useChargement<PagedResultDto<UtilisateurDto>>(cheminCompte("EnAttente"));
  const nonConfirmes = useChargement<PagedResultDto<UtilisateurDto>>(cheminCompte("NonConfirme"));
  const valides = useChargement<PagedResultDto<UtilisateurDto>>(cheminCompte("Valide"));
  const desactives = useChargement<PagedResultDto<UtilisateurDto>>(cheminCompte("Desactive"));
  const tous = useChargement<PagedResultDto<UtilisateurDto>>("utilisateur/lister?taille=1");
  const nombre = (c: { donnees: PagedResultDto<UtilisateurDto> | null }) =>
    c.donnees ? ` (${c.donnees.totalElements})` : "";
  const [actionEnCours, setActionEnCours] = useState<number | null>(null);
  const [erreurAction, setErreurAction] = useState<string | null>(null);
  // Compte dont on saisit le motif de désactivation (confirmation en ligne).
  const [aDesactiver, setADesactiver] = useState<number | null>(null);
  const [motif, setMotif] = useState("");
  const [creationAdmin, setCreationAdmin] = useState(false);
  // Compte rendu de la dernière action (création d'un admin, désactivation d'un hôte et ses réservations).
  const [information, setInformation] = useState<{ texte: string; alerte: boolean } | null>(null);

  // Comptes modifiés en temps réel : ligne en surbrillance quelques secondes.
  const [surbrillance, setSurbrillance] = useState<ReadonlySet<number>>(() => new Set());
  const minuteries = useRef(new Map<number, number>());
  const relecture = useRef<number | undefined>(undefined);
  useEffect(() => {
    const enCours = minuteries.current;
    return () => {
      for (const m of enCours.values()) window.clearTimeout(m);
      window.clearTimeout(relecture.current);
    };
  }, []);

  // Inscription, confirmation d'email, action d'un autre admin… : onglet affiché et compteurs relus sans
  // indicateur de chargement (la page et la recherche sont conservées).
  useTempsReel("utilisateurChange", ({ utilisateurId: id }) => {
    window.clearTimeout(relecture.current);
    relecture.current = window.setTimeout(() => {
      for (const c of [enAttente, nonConfirmes, valides, desactives, tous]) c.recharger();
      recharger();
    }, DELAI_RELECTURE);
    window.clearTimeout(minuteries.current.get(id));
    setSurbrillance((s) => new Set(s).add(id));
    minuteries.current.set(
      id,
      window.setTimeout(() => {
        minuteries.current.delete(id);
        setSurbrillance((s) => {
          const suite = new Set(s);
          suite.delete(id);
          return suite;
        });
      }, DUREE_SURBRILLANCE),
    );
  });

  const affiches = donnees?.donnees ?? [];
  const totalPages = donnees?.totalPages ?? 1;
  // Dernière page vidée (comptes passés dans un autre onglet) : retour sur la nouvelle dernière page.
  if (donnees && donnees.totalPages > 0 && page > donnees.totalPages) setPage(donnees.totalPages);

  const changerFiltre = (f: Filtre) => {
    setFiltre(f);
    setPage(1);
  };
  const rechercher = (e: FormEvent) => {
    e.preventDefault();
    setRecherche(saisie.trim());
    setPage(1);
  };

  const changerStatut = async (id: number, action: Action, corps?: unknown) => {
    setErreurAction(null);
    setInformation(null);
    setActionEnCours(id);
    try {
      const misAJour = await apiClient.post<UtilisateurDto>(`utilisateur/${id}/${action}`, corps);
      setDonnees((d) => d && { ...d, donnees: d.donnees.map((u) => (u.id === id ? misAJour : u)) });
      setADesactiver(null);
      setMotif("");
      if (action === "desactiver") setInformation(bilanDesactivation(misAJour));
      // Le compte change d'onglet : compteurs et page rechargés.
      for (const c of [enAttente, nonConfirmes, valides, desactives]) c.recharger();
      recharger();
    } catch (err) {
      setErreurAction(messageErreur(err, "L'action a échoué."));
    } finally {
      setActionEnCours(null);
    }
  };

  const confirmerDesactivation = (e: FormEvent, id: number) => {
    e.preventDefault();
    changerStatut(id, "desactiver", { motif: motif.trim() || null });
  };

  return (
    <>
      <EnTete
        titre="Utilisateurs"
        description="Validez les inscriptions, désactivez ou réactivez les comptes. Un compte désactivé ne peut plus se connecter et ses annonces sont masquées."
        action={
          !creationAdmin && (
            <button
              type="button"
              onClick={() => {
                setCreationAdmin(true);
                setInformation(null);
              }}
              className={classeBoutonPrincipal()}
            >
              <ShieldPlus size={16} />
              Créer un administrateur
            </button>
          )
        }
      />
      {creationAdmin && (
        <CreationAdministrateur
          onFermer={() => setCreationAdmin(false)}
          onCree={(admin) => {
            setCreationAdmin(false);
            setInformation({
              texte: `Le compte administrateur de ${admin.nom} (${admin.email}) est créé et actif.`,
              alerte: false,
            });
            for (const c of [valides, tous]) c.recharger();
            recharger();
          }}
        />
      )}
      {information && (
        <p
          className={`text-sm rounded-lg px-4 py-3 mb-4 ${information.alerte ? "bg-amber-50 text-amber-900" : "bg-green-50 text-green-800"}`}
          role="status"
        >
          {information.texte}
        </p>
      )}
      <Onglets
        valeur={filtre}
        onChange={changerFiltre}
        options={[
          { valeur: "EnAttente", label: `À valider${nombre(enAttente)}` },
          { valeur: "NonConfirme", label: `Email non confirmé${nombre(nonConfirmes)}` },
          { valeur: "Valide", label: `Actifs${nombre(valides)}` },
          { valeur: "Desactive", label: `Désactivés${nombre(desactives)}` },
          { valeur: "Tous", label: `Tous${nombre(tous)}` },
        ]}
      />
      <form onSubmit={rechercher} role="search" className="flex gap-2 mb-4 max-w-xl">
        <input
          type="search"
          value={saisie}
          onChange={(e) => setSaisie(e.target.value)}
          placeholder="Nom, email ou téléphone"
          aria-label="Rechercher un utilisateur"
          className={`${classeChamp} mt-0`}
        />
        <button type="submit" className={classeBoutonSecondaire()}>
          <Search size={15} />
          Rechercher
        </button>
      </form>
      {erreurAction && (
        <div className="mb-4">
          <MessageErreur>{erreurAction}</MessageErreur>
        </div>
      )}
      <EtatListe
        chargement={chargement}
        erreur={erreur}
        vide={affiches.length === 0}
        messageVide={
          recherche
            ? `Aucun utilisateur ne correspond à « ${recherche} ».`
            : filtre === "EnAttente"
              ? "Aucune inscription à valider (email confirmé)."
              : filtre === "NonConfirme"
                ? "Aucune inscription en attente de confirmation de l'adresse email."
                : filtre === "Desactive"
                  ? "Aucun compte désactivé."
                  : "Aucun utilisateur."
        }
      >
        <Tableau entetes={["Nom", "Email", "Téléphone", "Rôle", "Statut", ""]}>
          {affiches.map((u) => (
            <tr
              key={u.id}
              className={`border-b border-black/5 last:border-0 align-top transition-colors duration-700 ${surbrillance.has(u.id) ? "bg-amber-50" : ""}`}
            >
              <td className="px-5 py-3 font-semibold whitespace-nowrap">{u.nom}</td>
              <td className="px-5 py-3 text-gray-600">{u.email}</td>
              <td className="px-5 py-3 text-gray-600 whitespace-nowrap">{u.telephone}</td>
              <td className="px-5 py-3">
                <Badge ton="neutre">{u.role}</Badge>
              </td>
              <td className="px-5 py-3">
                <div className="flex flex-col items-start gap-1">
                  <Badge ton={statuts[u.statut].ton}>{statuts[u.statut].label}</Badge>
                  {u.emailConfirme ? (
                    <span
                      className="inline-flex items-center gap-1 text-xs text-green-700 whitespace-nowrap"
                      title={
                        u.dateConfirmationEmail ? `Confirmé le ${formaterDate(u.dateConfirmationEmail)}` : undefined
                      }
                    >
                      <MailCheck size={13} aria-hidden="true" />
                      Email confirmé
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 text-xs text-amber-700 whitespace-nowrap">
                      <MailWarning size={13} aria-hidden="true" />
                      Email non confirmé
                    </span>
                  )}
                </div>
                {u.statut === "Desactive" && (
                  <p className="text-xs text-gray-500 mt-1 max-w-56">
                    Depuis le {formaterDate(u.dateDesactivation)}
                    {u.motifDesactivation && <> — {u.motifDesactivation}</>}
                  </p>
                )}
              </td>
              <td className="px-5 py-3">
                <div className="flex gap-2 justify-end flex-wrap">
                  {u.statut === "EnAttente" && (
                    <>
                      {u.emailConfirme ? (
                        <button
                          onClick={() => changerStatut(u.id, "valider")}
                          disabled={actionEnCours === u.id}
                          className={classeBoutonPrincipal(true)}
                        >
                          <Check size={14} />
                          Valider
                        </button>
                      ) : (
                        <span className="text-xs text-gray-500 self-center max-w-44">
                          Validation possible après la confirmation de l'email.
                        </span>
                      )}
                      <button
                        onClick={() => changerStatut(u.id, "refuser")}
                        disabled={actionEnCours === u.id}
                        className={classeBoutonDanger(true)}
                      >
                        <X size={14} />
                        Refuser
                      </button>
                    </>
                  )}
                  {u.statut === "Valide" &&
                    u.id !== utilisateurId &&
                    (aDesactiver === u.id ? (
                      <form onSubmit={(e) => confirmerDesactivation(e, u.id)} className="flex flex-col gap-2 w-52">
                        <label className="text-xs font-medium text-gray-700">
                          Motif (envoyé par email, optionnel)
                          <input
                            value={motif}
                            onChange={(e) => setMotif(e.target.value)}
                            maxLength={500}
                            autoFocus
                            className={classeChamp}
                          />
                        </label>
                        <div className="flex gap-2 justify-end">
                          <button
                            type="button"
                            onClick={() => setADesactiver(null)}
                            className={classeBoutonSecondaire(true)}
                          >
                            Annuler
                          </button>
                          <button type="submit" disabled={actionEnCours === u.id} className={classeBoutonDanger(true)}>
                            <Ban size={14} />
                            Confirmer
                          </button>
                        </div>
                      </form>
                    ) : (
                      <button
                        onClick={() => {
                          setADesactiver(u.id);
                          setMotif("");
                        }}
                        className={classeBoutonDanger(true)}
                      >
                        <Ban size={14} />
                        Désactiver
                      </button>
                    ))}
                  {u.statut === "Refuse" && u.emailConfirme && (
                    <button
                      onClick={() => changerStatut(u.id, "valider")}
                      disabled={actionEnCours === u.id}
                      className={classeBoutonPrincipal(true)}
                    >
                      <Check size={14} />
                      Valider
                    </button>
                  )}
                  {u.statut === "Desactive" && (
                    <button
                      onClick={() => changerStatut(u.id, "reactiver")}
                      disabled={actionEnCours === u.id}
                      className={classeBoutonPrincipal(true)}
                    >
                      <RotateCcw size={14} />
                      Réactiver
                    </button>
                  )}
                </div>
              </td>
            </tr>
          ))}
        </Tableau>
        {totalPages > 1 && (
          <nav aria-label="Pagination" className="flex items-center justify-center gap-3 mt-4 text-sm">
            <button
              type="button"
              onClick={() => setPage((p) => p - 1)}
              disabled={page <= 1}
              className={classeBoutonSecondaire(true)}
            >
              Précédente
            </button>
            <span className="text-gray-600">
              Page {page} sur {totalPages}
            </span>
            <button
              type="button"
              onClick={() => setPage((p) => p + 1)}
              disabled={page >= totalPages}
              className={classeBoutonSecondaire(true)}
            >
              Suivante
            </button>
          </nav>
        )}
      </EtatListe>
    </>
  );
}
