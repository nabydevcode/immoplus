import { Fragment, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle, Check, ChevronDown, ChevronUp, Clock, FileText, FileUp, MapPin, X } from "lucide-react";
import { apiClient, ApiError } from "../api/client";
import { useAuth } from "../context/auth";
import {
  formaterDate,
  formaterMontant,
  messageErreur,
  useChargement,
  formaterReference,
} from "../components/admin/outils";
import type {
  CandidatureDto,
  DemandeComplementDto,
  DocumentDto,
  FinancementDto,
  MensualiteDto,
  PagedResultDto,
  PieceDemandeeDto,
  LocationDto,
} from "../types/models";
import { formaterJour } from "../components/sejour/dates";
import { formaterDateHeure, libellePiece, referenceDossier, VISITE_ACTIVE } from "../components/candidatures/analyse";
import { BadgePonctualite, BilanRetards, HistoriquePaiements } from "../components/financement/SuiviPaiements";
import Loyers from "../components/financement/Loyers";
import FinLocation from "../components/financement/FinLocation";
import AvisLocation from "../components/avis/AvisLocation";
import BoutonPdf from "../components/documents/BoutonPdf";
import { contratLocation } from "../components/documents/pdf";
import { useLienConnexion } from "../hooks/useLienConnexion";
import { avecPage, listeDe, TAILLE_MAX, useListeProgressive } from "../components/admin/pagination";
import { VoirPlus } from "../components/admin/ui";

type EtatEtape = "fait" | "en-cours" | "a-venir" | "refuse";

interface Etape {
  titre: string;
  detail?: string;
  etat: EtatEtape;
}

const libellesSituation: Record<string, string> = {
  CDI: "CDI",
  CDD: "CDD",
  Independant: "Indépendant",
  DebutActiviteRevenuFixe: "Début d'activité, revenu fixe",
  Autre: "Autre",
};

const libellesDocument: Record<string, string> = {
  PieceIdentite: "Pièce d'identité",
  JustificatifRevenu: "Justificatif de revenus",
  JustificatifDomicile: "Justificatif de domicile",
  Autre: "Autre",
};

/** Construit la frise d'étapes d'un dossier à partir de son statut et de son financement éventuel. */
/** Une pièce demandée est reçue si un document du même type, pour la même personne, a été déposé depuis la demande. */
const estRecue = (piece: PieceDemandeeDto, demande: DemandeComplementDto, documents: DocumentDto[]) =>
  documents.some(
    (d) =>
      d.proprietaire === piece.proprietaire &&
      d.typeDocument === piece.typeDocument &&
      new Date(d.dateUpload) >= new Date(demande.dateDemande),
  );

/**
 * Frise du dossier : toutes les étapes de la location sont toujours affichées (celles à venir en gris),
 * pour que le candidat voie dès le départ l'ordre Étude → Visite → Attribution → Apport → Clés.
 */
function construireEtapes(
  c: CandidatureDto,
  financement: FinancementDto | undefined,
  mensualitesPayees?: number,
  documentsDemandes?: boolean,
  location?: LocationDto,
): Etape[] {
  const visiteFaite = c.visites?.find((v) => v.statut === "Effectuee");
  const visitePrevue = c.visites?.find((v) => VISITE_ACTIVE.includes(v.statut));
  const etapes: Etape[] = [{ titre: "Dossier déposé", detail: formaterDate(c.dateSoumission), etat: "fait" }];

  // Étude du dossier
  etapes.push(
    c.statut === "EnAttente"
      ? { titre: "Étude du dossier", detail: documentsDemandes ? "Documents demandés" : "En cours", etat: "en-cours" }
      : { titre: "Étude du dossier", detail: "Terminée", etat: "fait" },
  );

  // Dossier clos (refusé ou annulé) : on garde la visite si elle a eu lieu, puis la fin du parcours.
  if (c.statut === "Refusee" || c.statut === "Annulee") {
    if (visiteFaite)
      etapes.push({ titre: "Visite de l'appartement", detail: formaterJour(visiteFaite.dateHeure), etat: "fait" });
    etapes.push({
      titre: c.statut === "Annulee" ? "Candidature annulée" : "Dossier non retenu",
      detail: c.motifRefus ?? undefined,
      etat: "refuse",
    });
    return etapes;
  }

  // Visite de l'appartement
  if (visiteFaite) {
    etapes.push({
      titre: "Visite de l'appartement",
      detail: `Effectuée le ${formaterJour(visiteFaite.dateHeure)}`,
      etat: "fait",
    });
  } else if (visitePrevue) {
    etapes.push({
      titre: "Visite de l'appartement",
      detail: formaterDateHeure(visitePrevue.dateHeure),
      etat: "en-cours",
    });
  } else if (c.statut === "Preselectionnee") {
    etapes.push({ titre: "Visite de l'appartement", detail: "Date à convenir", etat: "en-cours" });
  } else if (c.statut === "Acceptee") {
    // Dossiers attribués avant la mise en place des visites.
    etapes.push({ titre: "Visite de l'appartement", detail: "Non enregistrée", etat: "fait" });
  } else {
    etapes.push({ titre: "Visite de l'appartement", detail: "Si votre dossier est retenu", etat: "a-venir" });
  }

  if (c.statut === "ListeAttente") {
    etapes.push(
      { titre: "Liste d'attente", detail: "Logement attribué à un autre candidat", etat: "en-cours" },
      { titre: "Attribution si désistement", etat: "a-venir" },
    );
    return etapes;
  }

  // Attribution
  const attribue = c.statut === "Acceptee";
  etapes.push(
    attribue
      ? {
          titre: "Logement attribué",
          detail: c.locationId ? `Location n° ${formaterReference(c.locationId)}` : undefined,
          etat: "fait",
        }
      : visiteFaite
        ? { titre: "Attribution", detail: "Décision du bailleur", etat: "en-cours" }
        : { titre: "Attribution", detail: "Après les visites", etat: "a-venir" },
  );

  // Paiement de l'apport, puis remise des clés
  etapes.push({
    titre: "Paiement de l'apport",
    detail: c.dateApportPaye
      ? `Reçu le ${formaterDate(c.dateApportPaye)}`
      : `${formaterMontant(c.apportInitial)}${attribue ? " à régler" : ""}`,
    etat: c.dateApportPaye ? "fait" : attribue ? "en-cours" : "a-venir",
  });
  etapes.push({
    titre: "Remise des clés",
    detail: c.dateRemiseCles ? formaterDate(c.dateRemiseCles) : undefined,
    etat: c.dateRemiseCles ? "fait" : c.dateApportPaye ? "en-cours" : "a-venir",
  });

  // Remboursement de l'avance financée, s'il y en a une
  if (attribue && c.financementLocationId) {
    const statut = financement?.statut;
    etapes.push({
      titre: "Remboursement",
      detail:
        statut === "EnCours" && financement
          ? `${mensualitesPayees ?? "…"} / ${financement.nombreMensualites} mensualité${financement.nombreMensualites > 1 ? "s" : ""} payée${financement.nombreMensualites > 1 ? "s" : ""}`
          : statut === "Solde"
            ? "Terminé"
            : c.dateApportPaye
              ? "Échéancier en préparation"
              : undefined,
      etat: statut === "Solde" ? "fait" : statut === "EnCours" ? "en-cours" : "a-venir",
    });
  }

  // Puis le loyer mensuel, sans frais, à la fin de la période couverte par l'avance
  const terminee = location?.statut === "Terminee";
  if (attribue) {
    etapes.push({
      titre: "Loyer mensuel",
      detail: "Sans frais, après l'avance",
      etat: terminee
        ? "fait"
        : c.dateRemiseCles && (!c.financementLocationId || financement?.statut === "Solde")
          ? "en-cours"
          : "a-venir",
    });
  }

  // Fin de location, après un préavis de 3 mois
  if (attribue && (location?.dateFinPrevue || terminee)) {
    etapes.push({
      titre: "Fin de location",
      detail: terminee ? formaterDate(location?.dateFin) : `Préavis : fin le ${formaterDate(location?.dateFinPrevue)}`,
      etat: terminee ? "fait" : "en-cours",
    });
  }
  return etapes;
}

function Frise({ etapes }: { etapes: Etape[] }) {
  const styles: Record<EtatEtape, { pastille: string; icone: ReactNode; texte: string }> = {
    fait: { pastille: "bg-green-600 text-white", icone: <Check size={14} />, texte: "text-gray-900" },
    "en-cours": {
      pastille: "bg-immo-gold text-immo-dark ring-4 ring-immo-gold/25",
      icone: <Clock size={14} />,
      texte: "text-gray-900 font-semibold",
    },
    "a-venir": { pastille: "bg-gray-200 text-gray-400", icone: null, texte: "text-gray-400" },
    refuse: { pastille: "bg-red-600 text-white", icone: <X size={14} />, texte: "text-red-700 font-semibold" },
  };

  return (
    <ol className="flex flex-col sm:flex-row sm:items-start gap-3 sm:gap-0">
      {etapes.map((etape, i) => {
        const s = styles[etape.etat];
        return (
          <li
            key={etape.titre}
            className="flex sm:flex-col sm:flex-1 items-start sm:items-center gap-3 sm:gap-2 relative sm:text-center"
          >
            {i < etapes.length - 1 && (
              <span
                className={`hidden sm:block absolute top-3.5 left-1/2 w-full h-0.5 ${etape.etat === "fait" ? "bg-green-600" : "bg-gray-200"}`}
              />
            )}
            <span
              className={`relative z-10 flex items-center justify-center w-7 h-7 rounded-full shrink-0 text-xs font-bold ${s.pastille}`}
            >
              {s.icone ?? i + 1}
            </span>
            <div className="sm:px-2">
              <p className={`text-sm ${s.texte}`}>{etape.titre}</p>
              {etape.detail && <p className="text-xs text-gray-500 mt-0.5">{etape.detail}</p>}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

function AjoutJustificatif({
  candidatureId,
  avecGarant,
  suggestion,
  onAjoute,
}: {
  candidatureId: number;
  avecGarant: boolean;
  /** Pièce présélectionnée (la première pièce demandée par le bailleur et pas encore fournie). */
  suggestion?: { typeDocument: string; proprietaire: string };
  onAjoute: (d: DocumentDto) => void;
}) {
  const [typeDocument, setTypeDocument] = useState(suggestion?.typeDocument ?? "PieceIdentite");
  const [proprietaire, setProprietaire] = useState(suggestion?.proprietaire ?? "Locataire");
  const [erreur, setErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);

  const envoyer = async (fichier: File | undefined) => {
    if (!fichier) return;
    setErreur(null);
    if (fichier.size > 10 * 1024 * 1024) {
      setErreur("Le fichier dépasse 10 Mo.");
      return;
    }
    const formulaire = new FormData();
    formulaire.append("fichier", fichier);
    formulaire.append("typeDocument", typeDocument);
    formulaire.append("proprietaire", proprietaire);
    setEnCours(true);
    try {
      onAjoute(await apiClient.postFormulaire<DocumentDto>(`candidature/${candidatureId}/documents`, formulaire));
    } catch (err) {
      setErreur(err instanceof ApiError ? err.message : "Envoi impossible.");
    } finally {
      setEnCours(false);
    }
  };

  const classeSelect = "border border-gray-300 rounded-lg px-3 py-2 text-sm bg-white";
  return (
    <div className="mt-3">
      <div className="flex flex-wrap items-center gap-2">
        <select value={typeDocument} onChange={(e) => setTypeDocument(e.target.value)} className={classeSelect}>
          {Object.entries(libellesDocument).map(([valeur, label]) => (
            <option key={valeur} value={valeur}>
              {label}
            </option>
          ))}
        </select>
        {avecGarant && (
          <select value={proprietaire} onChange={(e) => setProprietaire(e.target.value)} className={classeSelect}>
            <option value="Locataire">Moi</option>
            <option value="Garant">Mon garant</option>
          </select>
        )}
        <label
          className={`inline-flex items-center gap-1.5 text-sm font-semibold px-3 py-2 rounded-lg border border-immo-gold text-immo-dark cursor-pointer hover:bg-immo-gold/10 ${enCours ? "opacity-60 pointer-events-none" : ""}`}
        >
          <FileUp size={16} />
          {enCours ? "Envoi..." : "Ajouter un fichier"}
          <input
            type="file"
            accept=".pdf,.jpg,.jpeg,.png"
            className="hidden"
            onChange={(e) => {
              envoyer(e.target.files?.[0]);
              e.target.value = "";
            }}
          />
        </label>
      </div>
      {erreur && <p className="text-red-600 text-sm mt-2">{erreur}</p>}
    </div>
  );
}

/** Échéancier en lecture seule pour le locataire : ponctualité et historique de chaque mensualité. */
function Echeancier({ financement }: { financement: FinancementDto }) {
  const { donnees, chargement, erreur } = useChargement<MensualiteDto[]>(`financement/${financement.id}/mensualites`);
  const [historiqueOuvert, setHistoriqueOuvert] = useState<number | null>(null);

  if (chargement) return <p className="text-sm text-gray-500">Chargement de l'échéancier...</p>;
  if (erreur) return <p className="text-sm text-red-600">{erreur}</p>;
  if (!donnees?.length)
    return (
      <p className="text-sm text-gray-500">L'échéancier est en cours de préparation. Vous serez prévenu par email.</p>
    );

  return (
    <div className="space-y-3">
      <BilanRetards financement={financement} />
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-gray-500 border-b border-black/5">
              <th className="py-2 pr-4 font-medium">Échéance</th>
              <th className="py-2 pr-4 font-medium">À payer le</th>
              <th className="py-2 pr-4 font-medium">Montant</th>
              <th className="py-2 pr-4 font-medium">Payé</th>
              <th className="py-2 pr-4 font-medium">Suivi</th>
              <th className="py-2" />
            </tr>
          </thead>
          <tbody>
            {donnees.map((m) => (
              <Fragment key={m.id}>
                <tr className="border-b border-black/5 last:border-0">
                  <td className="py-2 pr-4">n° {m.numeroEcheance}</td>
                  <td className="py-2 pr-4 whitespace-nowrap">{formaterDate(m.dateEcheance)}</td>
                  <td className="py-2 pr-4 whitespace-nowrap">{formaterMontant(m.montantDu)}</td>
                  <td className="py-2 pr-4 whitespace-nowrap">{formaterMontant(m.montantPaye)}</td>
                  <td className="py-2 pr-4">
                    <BadgePonctualite mensualite={m} />
                  </td>
                  <td className="py-2 text-right whitespace-nowrap">
                    {m.paiements.length > 0 && (
                      <button
                        onClick={() => setHistoriqueOuvert(historiqueOuvert === m.id ? null : m.id)}
                        className="inline-flex items-center gap-0.5 text-xs font-semibold text-gray-600 hover:text-immo-dark"
                      >
                        {historiqueOuvert === m.id ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
                        {m.paiements.length} paiement{m.paiements.length > 1 ? "s" : ""}
                      </button>
                    )}
                  </td>
                </tr>
                {historiqueOuvert === m.id && (
                  <tr>
                    <td colSpan={6} className="py-2 px-3 bg-gray-50">
                      <HistoriquePaiements paiements={m.paiements} />
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function CarteCandidature({
  candidature,
  financement,
  location,
  onChange,
}: {
  candidature: CandidatureDto;
  financement?: FinancementDto;
  location?: LocationDto;
  onChange: () => void;
}) {
  const [ouvert, setOuvert] = useState(false);
  const [erreurRetrait, setErreurRetrait] = useState<string | null>(null);
  // Un désistement n'est plus possible une fois l'apport payé (l'API le refuserait aussi).
  const retirable =
    ["EnAttente", "ListeAttente", "Preselectionnee"].includes(candidature.statut) ||
    (candidature.statut === "Acceptee" && !candidature.dateApportPaye);
  const enEtude = candidature.statut === "EnAttente" || candidature.statut === "Preselectionnee";
  const visite =
    candidature.statut === "Preselectionnee"
      ? candidature.visites?.find((v) => VISITE_ACTIVE.includes(v.statut))
      : undefined;
  const [refusVisite, setRefusVisite] = useState(false);
  const [messageVisite, setMessageVisite] = useState("");
  const [erreurVisite, setErreurVisite] = useState<string | null>(null);
  // Évite un double envoi (réponse à la visite, retrait) pendant l'appel à l'API.
  const [actionEnCours, setActionEnCours] = useState(false);

  const repondreVisite = async (disponible: boolean) => {
    if (!visite || actionEnCours) return;
    setErreurVisite(null);
    setActionEnCours(true);
    try {
      await apiClient.post(`visite/${visite.id}/repondre`, { disponible, message: messageVisite.trim() || null });
      setRefusVisite(false);
      onChange();
    } catch (err) {
      setErreurVisite(messageErreur(err, "La réponse n'a pas pu être envoyée."));
    } finally {
      setActionEnCours(false);
    }
  };

  const seRetirer = async () => {
    const question =
      candidature.statut === "Acceptee"
        ? "Vous désister de ce logement ? La location et le financement seront annulés, et le bailleur pourra attribuer le logement à un autre candidat. Cette action est définitive."
        : "Retirer votre candidature ? Cette action est définitive.";
    if (actionEnCours || !window.confirm(question)) return;
    setErreurRetrait(null);
    setActionEnCours(true);
    try {
      await apiClient.post(`candidature/${candidature.id}/desister`);
      onChange();
    } catch (err) {
      setErreurRetrait(messageErreur(err, "L'opération a échoué."));
    } finally {
      setActionEnCours(false);
    }
  };
  const [documents, setDocuments] = useState(candidature.documents);
  // Le nombre de mensualités payées n'est chargé que pour un remboursement en cours.
  const { donnees: mensualites } = useChargement<MensualiteDto[]>(
    financement?.statut === "EnCours" ? `financement/${financement.id}/mensualites` : null,
  );
  const payees =
    financement?.statut === "EnCours" ? mensualites?.filter((m) => m.statut === "Payee").length : undefined;
  // Dernière demande de pièces du bailleur encore incomplète (recalculée à chaque dépôt de fichier).
  const demandeActive = enEtude
    ? (candidature.demandesComplement ?? []).find((d) => !d.pieces.every((p) => estRecue(p, d, documents)))
    : undefined;
  const pieceSuivante = demandeActive?.pieces.find((p) => !estRecue(p, demandeActive, documents));
  const delaiDepasse = demandeActive?.depassee ?? false;
  const etapes = construireEtapes(candidature, financement, payees, !!demandeActive, location);
  const etapeActuelle = etapes.find((e) => e.etat === "en-cours" || e.etat === "refuse") ?? etapes[etapes.length - 1];

  return (
    <article className="bg-white rounded-2xl shadow-sm border border-black/5">
      <div className="p-6">
        <div className="flex flex-wrap items-start justify-between gap-3 mb-6">
          <div>
            <p className="inline-flex items-center gap-1.5 text-xs font-semibold text-immo-dark bg-immo-gold/15 border border-immo-gold/30 px-2.5 py-1 rounded-md mb-2">
              Réf. dossier : n° {referenceDossier(candidature.id)}
            </p>
            <h2 className="font-bold flex items-center gap-1.5">
              <MapPin size={16} className="text-immo-gold shrink-0" />
              {candidature.appartementAdresse}
            </h2>
          </div>
          <span
            className={`text-xs font-semibold px-3 py-1.5 rounded-full ${
              etapeActuelle.etat === "refuse"
                ? "bg-red-100 text-red-700"
                : etapeActuelle.etat === "en-cours"
                  ? "bg-immo-gold/15 text-immo-dark"
                  : "bg-green-100 text-green-700"
            }`}
          >
            Étape actuelle : {etapeActuelle.titre}
          </span>
        </div>

        {visite && (
          <div className="mb-6 rounded-xl border border-purple-200 bg-purple-50 p-4 text-sm">
            <p className="font-semibold text-purple-900 mb-1">
              {visite.statut === "Indisponible"
                ? "Vous avez signalé ne pas être disponible : le bailleur va vous proposer une autre date"
                : "Vous êtes invité à visiter le logement"}
            </p>
            <p className="text-gray-800">
              Le <strong>{formaterDateHeure(visite.dateHeure)}</strong> — {candidature.appartementAdresse}
            </p>
            {visite.message && <p className="text-gray-600 italic mt-1">Message du bailleur : « {visite.message} »</p>}
            {visite.statut === "Confirmee" && (
              <p className="text-green-700 font-semibold mt-2 flex items-center gap-1.5">
                <Check size={16} />
                Présence confirmée
              </p>
            )}
            {visite.statut !== "Indisponible" &&
              (refusVisite ? (
                <div className="mt-3 space-y-2">
                  <textarea
                    value={messageVisite}
                    onChange={(e) => setMessageVisite(e.target.value)}
                    rows={2}
                    maxLength={300}
                    placeholder="Indiquez vos disponibilités (ex. : en semaine après 17h, ou samedi matin)"
                    className="w-full border border-gray-300 rounded-lg px-3 py-2 bg-white"
                  />
                  <div className="flex gap-2">
                    <button
                      onClick={() => repondreVisite(false)}
                      disabled={actionEnCours}
                      className="font-semibold text-red-600 border border-red-300 bg-white rounded-lg px-4 py-2 hover:bg-red-50 disabled:opacity-60"
                    >
                      {actionEnCours ? "Envoi..." : "Envoyer"}
                    </button>
                    <button onClick={() => setRefusVisite(false)} className="font-semibold text-gray-600 px-4 py-2">
                      Annuler
                    </button>
                  </div>
                </div>
              ) : (
                <div className="mt-3 flex flex-wrap gap-2">
                  {visite.statut === "Proposee" && (
                    <button
                      onClick={() => repondreVisite(true)}
                      disabled={actionEnCours}
                      className="inline-flex items-center gap-1.5 bg-immo-gold text-immo-dark font-semibold px-4 py-2 rounded-lg hover:bg-immo-gold-light disabled:opacity-60"
                    >
                      <Check size={16} />
                      Je confirme ma présence
                    </button>
                  )}
                  <button
                    onClick={() => setRefusVisite(true)}
                    className="font-semibold text-gray-700 border border-gray-300 bg-white rounded-lg px-4 py-2 hover:bg-gray-50"
                  >
                    {visite.statut === "Confirmee" ? "Je ne pourrai finalement pas venir" : "Je ne suis pas disponible"}
                  </button>
                </div>
              ))}
            {erreurVisite && <p className="text-red-600 mt-2">{erreurVisite}</p>}
          </div>
        )}

        {demandeActive && (
          <div
            className={`mb-6 rounded-xl border p-4 text-sm ${delaiDepasse ? "bg-red-50 border-red-200" : "bg-amber-50 border-amber-200"}`}
          >
            <p
              className={`font-semibold flex items-center gap-2 mb-2 ${delaiDepasse ? "text-red-800" : "text-amber-900"}`}
            >
              <AlertTriangle size={16} />
              {delaiDepasse
                ? `Date limite dépassée (${formaterDate(demandeActive.dateLimite)}) : ajoutez ces documents au plus vite`
                : `Le bailleur demande des documents avant le ${formaterDate(demandeActive.dateLimite)}`}
            </p>
            <ul className="space-y-1 mb-3">
              {demandeActive.pieces.map((p) => {
                const recue = estRecue(p, demandeActive, documents);
                return (
                  <li
                    key={`${p.proprietaire}${p.typeDocument}`}
                    className={`flex items-center gap-2 ${recue ? "text-gray-500" : "text-gray-800"}`}
                  >
                    {recue ? <Check size={14} className="text-green-600" /> : <X size={14} className="text-red-500" />}
                    {libellePiece(p)}
                    {recue && <span className="text-xs text-green-700">reçu</span>}
                  </li>
                );
              })}
            </ul>
            {demandeActive.message && <p className="text-gray-600 italic mb-3">« {demandeActive.message} »</p>}
            {!ouvert && (
              <button
                onClick={() => setOuvert(true)}
                className="inline-flex items-center gap-1.5 bg-immo-gold text-immo-dark font-semibold px-4 py-2 rounded-lg hover:bg-immo-gold-light"
              >
                <FileUp size={16} />
                Ajouter les documents
              </button>
            )}
          </div>
        )}

        <Frise etapes={etapes} />

        {location && (
          <div className="mt-6 empty:hidden">
            <AvisLocation location={location} pourLogement onEnvoye={onChange} />
          </div>
        )}

        <button
          onClick={() => setOuvert((o) => !o)}
          className="mt-6 inline-flex items-center gap-1 text-sm font-semibold text-immo-dark hover:text-immo-gold"
        >
          {ouvert ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
          {ouvert ? "Masquer le détail" : "Voir le détail"}
        </button>
      </div>

      {ouvert && (
        <div className="border-t border-black/5 p-6 grid grid-cols-1 md:grid-cols-2 gap-6 text-sm">
          {candidature.statut === "ListeAttente" && (
            <p className="md:col-span-2 bg-blue-50 text-blue-900 rounded-lg px-4 py-3">
              Le logement a été attribué à un autre candidat, mais votre dossier reste en réserve : si ce candidat se
              désiste, le bailleur pourra vous le proposer et vous serez prévenu par email.
            </p>
          )}
          <div className="space-y-1">
            <p className="font-semibold mb-2">Mon dossier</p>
            <p>
              <span className="text-gray-500">Référence :</span> <strong>n° {referenceDossier(candidature.id)}</strong>
              {candidature.locationId && (
                <span className="text-gray-500"> · location n° {referenceDossier(candidature.locationId)}</span>
              )}
            </p>
            {candidature.statut === "Acceptee" && candidature.locationId && (
              <div className="pt-1 pb-2">
                <BoutonPdf fichier={contratLocation(candidature.locationId)} libelle="Contrat de location (PDF)" />
              </div>
            )}
            <p>
              <span className="text-gray-500">Apport initial :</span> {formaterMontant(candidature.apportInitial)}
            </p>
            <p>
              <span className="text-gray-500">Situation :</span>{" "}
              {libellesSituation[candidature.situationProfessionnelle] ?? candidature.situationProfessionnelle}
            </p>
            <p>
              <span className="text-gray-500">Garant :</span>{" "}
              {candidature.garant ? `${candidature.garant.nom} (${candidature.garant.telephone})` : "Aucun"}
            </p>
            {financement && (
              <p>
                <span className="text-gray-500">Financement :</span> {financement.nombreMensualites} mensualité
                {financement.nombreMensualites > 1 ? "s" : ""} de {formaterMontant(financement.montantMensualite)}
              </p>
            )}
          </div>

          <div>
            <p className="font-semibold mb-2">Justificatifs ({documents.length})</p>
            {documents.length === 0 ? (
              <p className="text-gray-500">Aucun justificatif joint.</p>
            ) : (
              <ul className="space-y-1">
                {documents.map((d) => (
                  <li key={d.id} className="flex items-center gap-1.5 text-gray-700">
                    <FileText size={14} className="text-gray-400 shrink-0" />
                    <span className="truncate">{d.nomFichierOriginal}</span>
                    <span className="text-xs text-gray-400 shrink-0">
                      ({libellesDocument[d.typeDocument] ?? d.typeDocument})
                    </span>
                  </li>
                ))}
              </ul>
            )}
            {enEtude && (
              <AjoutJustificatif
                key={pieceSuivante ? `${pieceSuivante.proprietaire}${pieceSuivante.typeDocument}` : "libre"}
                candidatureId={candidature.id}
                avecGarant={!!candidature.garant}
                suggestion={pieceSuivante}
                onAjoute={(d) => setDocuments((liste) => [...liste, d])}
              />
            )}
          </div>

          {financement && (
            <div className="md:col-span-2">
              <p className="font-semibold mb-2">Échéancier de remboursement</p>
              <Echeancier financement={financement} />
            </div>
          )}

          {candidature.statut === "Acceptee" && candidature.locationId && candidature.dateRemiseCles && (
            <div className="md:col-span-2">
              <p className="font-semibold mb-2">Mes loyers</p>
              <Loyers locationId={candidature.locationId} />
            </div>
          )}

          {candidature.statut === "Acceptee" && candidature.locationId && candidature.dateRemiseCles && (
            <div className="md:col-span-2">
              <p className="font-semibold mb-2">Fin de location</p>
              <FinLocation locationId={candidature.locationId} onChange={onChange} />
            </div>
          )}
        </div>
      )}

      {ouvert && retirable && (
        <div className="border-t border-black/5 px-6 py-4 flex flex-wrap items-center justify-between gap-3 text-sm">
          <span className="text-gray-500">
            {candidature.statut === "Acceptee"
              ? "Vous ne souhaitez plus ce logement ? Possible jusqu'au paiement de l'apport."
              : "Vous ne souhaitez plus ce logement ?"}
          </span>
          <button
            onClick={seRetirer}
            disabled={actionEnCours}
            className="font-semibold text-red-600 border border-red-300 rounded-lg px-4 py-2 hover:bg-red-50 disabled:opacity-60"
          >
            {candidature.statut === "Acceptee" ? "Me désister" : "Retirer ma candidature"}
          </button>
          {erreurRetrait && <p className="w-full text-red-600">{erreurRetrait}</p>}
        </div>
      )}
    </article>
  );
}

export default function MesCandidatures() {
  const { estConnecte, roleUtilisateur } = useAuth();
  const lienConnexion = useLienConnexion();
  const estLocataire = estConnecte && roleUtilisateur === "Locataire";
  // Dossiers 10 par 10 (« Voir plus ») ; financements et locations du locataire (peu nombreux) : 100 au plus.
  const candidatures = useListeProgressive<CandidatureDto>(estLocataire ? "candidature/lister" : null, 10);
  const financements = useChargement<PagedResultDto<FinancementDto> | FinancementDto[]>(
    estLocataire ? avecPage("financement/lister", 1, TAILLE_MAX) : null,
  );
  const locations = useChargement<PagedResultDto<LocationDto> | LocationDto[]>(
    estLocataire ? avecPage("location/lister", 1, TAILLE_MAX) : null,
  );

  if (!estLocataire) {
    return (
      <div className="px-6 py-16 max-w-xl mx-auto text-center text-gray-600">
        <Link to={lienConnexion} className="text-immo-gold font-semibold">
          Connectez-vous
        </Link>{" "}
        avec un compte Locataire pour suivre vos candidatures.
      </div>
    );
  }

  const liste = candidatures.elements;

  return (
    <div className="px-6 py-10">
      <div className="max-w-5xl mx-auto">
        <h1 className="text-2xl md:text-3xl font-extrabold mb-1">Mes candidatures</h1>
        <p className="text-gray-500 text-sm mb-8">Suivez l'avancement de chacun de vos dossiers de location.</p>

        {candidatures.chargement || financements.chargement ? (
          <p className="text-gray-500 text-sm">Chargement...</p>
        ) : candidatures.erreur ? (
          <p className="text-red-600 text-sm">{candidatures.erreur}</p>
        ) : liste.length === 0 ? (
          <div className="bg-white rounded-2xl shadow-sm border border-black/5 py-14 px-6 text-center">
            <p className="text-gray-500 mb-4">Vous n'avez encore déposé aucune candidature.</p>
            <Link
              to="/annonces/longue-duree"
              className="inline-block bg-immo-gold text-immo-dark font-semibold px-6 py-3 rounded-lg hover:bg-immo-gold-light transition-colors"
            >
              Voir les logements
            </Link>
          </div>
        ) : (
          <div className="flex flex-col gap-6">
            {liste.map((c) => (
              <CarteCandidature
                key={c.id}
                onChange={() => {
                  candidatures.recharger();
                  financements.recharger();
                  locations.recharger();
                }}
                candidature={c}
                financement={listeDe(financements.donnees).find((f) => f.id === c.financementLocationId)}
                location={listeDe(locations.donnees).find((l) => l.id === c.locationId)}
              />
            ))}
            <VoirPlus
              aPlus={candidatures.aPlus}
              chargement={candidatures.chargementPlus}
              onClick={candidatures.chargerPlus}
              affiches={liste.length}
              total={candidatures.total}
              libelle="Voir plus de candidatures"
            />
          </div>
        )}
      </div>
    </div>
  );
}
