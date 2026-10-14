import { useState, type ReactNode } from "react";
import { AlertTriangle, Check, Download, Mail, Phone, X } from "lucide-react";
import { apiClient, telechargerFichier } from "../../api/client";
import {
  classeBoutonDanger,
  classeBoutonPrincipal,
  classeChamp,
  formaterDate,
  formaterMontant,
  messageErreur,
  formaterReference,
} from "../admin/outils";
import type { AppartementDto, CandidatureDto } from "../../types/models";
import { libellesSituation, simulerFinancement } from "./analyse";
import DemandesDocuments from "./DemandesDocuments";
import VisiteCandidature from "./VisiteCandidature";
import SuiviAttribution from "./SuiviAttribution";
import { useAuth } from "../../context/auth";

const justificatifsAttendus = [
  { type: "PieceIdentite", label: "Pièce d'identité" },
  { type: "JustificatifRevenu", label: "Justificatif de revenus" },
  { type: "JustificatifDomicile", label: "Justificatif de domicile" },
];

const libellesDocument: Record<string, string> = {
  PieceIdentite: "Pièce d'identité",
  JustificatifRevenu: "Revenus",
  JustificatifDomicile: "Domicile",
  Autre: "Autre",
};

function Ligne({ libelle, children }: { libelle: string; children: ReactNode }) {
  return (
    <div className="flex justify-between gap-4 py-1">
      <span className="text-gray-500">{libelle}</span>
      <span className="font-semibold text-right">{children}</span>
    </div>
  );
}

function Bloc({ titre, children }: { titre: string; children: ReactNode }) {
  return (
    <div className="bg-white rounded-xl border border-black/5 p-4">
      <p className="font-semibold mb-2">{titre}</p>
      {children}
    </div>
  );
}

/** Détail d'une candidature pour le bailleur ou l'admin : analyse du dossier, justificatifs et décision. */
export default function ExamenCandidature({
  candidature,
  appartement,
  nbPreselectionnes = 0,
  onMiseAJour,
}: {
  candidature: CandidatureDto;
  appartement: AppartementDto | undefined;
  /** Candidats déjà retenus pour une visite sur ce logement (3 maximum). */
  nbPreselectionnes?: number;
  onMiseAJour: (c: CandidatureDto) => void;
}) {
  const [tauxFrais, setTauxFrais] = useState("3");
  const [motifRefus, setMotifRefus] = useState("");
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);

  const taux = Number(tauxFrais) / 100;
  const simulation = simulerFinancement(candidature, appartement, taux);
  const situation = libellesSituation[candidature.situationProfessionnelle];
  const { garant, documents } = candidature;
  const documentsLocataire = documents.filter((d) => d.proprietaire === "Locataire");
  const documentsGarant = documents.filter((d) => d.proprietaire === "Garant");

  const pointsAttention: string[] = [];
  for (const j of justificatifsAttendus) {
    if (!documentsLocataire.some((d) => d.typeDocument === j.type)) pointsAttention.push(`${j.label} manquant`);
  }
  if (!garant && candidature.situationProfessionnelle !== "CDI")
    pointsAttention.push("Pas de garant, et situation autre qu'un CDI");
  if (garant && documentsGarant.length === 0) pointsAttention.push("Aucun justificatif fourni pour le garant");
  if (simulation.financement && simulation.moisCouverts < 3)
    pointsAttention.push("Apport proche du minimum (2 mois de loyer)");
  if (candidature.demandesComplement?.some((d) => d.depassee))
    pointsAttention.push("Date limite dépassée pour des documents demandés au candidat");

  // Un dossier en liste d'attente ne peut être accepté que si le logement a été libéré (désistement, annulation).
  const logementAttribue = !!appartement?.enLocation;
  const decisionPossible = ["EnAttente", "ListeAttente", "Preselectionnee"].includes(candidature.statut);
  // Le bailleur s'arrête à l'acceptation : financement, apport, clés et échéancier relèvent de l'admin.
  const { roleUtilisateur } = useAuth();
  const estAdmin = roleUtilisateur === "Admin";
  // La décision se prend après la visite : « Accepter » n'est possible qu'une fois une visite effectuée.
  const visiteEffectuee = !!candidature.visites?.some((v) => v.statut === "Effectuee");

  const traiter = async (action: "accepter" | "refuser" | "annuler-attribution") => {
    setErreur(null);
    const confirmations = {
      accepter: `Accepter le dossier de ${candidature.locataireNom} ? Les autres candidats en attente pour ce logement passeront en liste d'attente.`,
      refuser: `Refuser le dossier de ${candidature.locataireNom} ? Un email lui sera envoyé.`,
      "annuler-attribution": `Annuler l'attribution à ${candidature.locataireNom} ? La location et le financement seront annulés et le logement redeviendra disponible pour un candidat de la liste d'attente.`,
    };
    if (!window.confirm(confirmations[action])) return;
    setEnCours(true);
    try {
      const corps = action === "accepter" ? { tauxFrais: taux } : { motifRefus: motifRefus.trim() || null };
      onMiseAJour(await apiClient.post<CandidatureDto>(`candidature/${candidature.id}/${action}`, corps));
    } catch (err) {
      setErreur(messageErreur(err, "L'action a échoué."));
    } finally {
      setEnCours(false);
    }
  };

  const telecharger = async (id: number, nom: string) => {
    setErreur(null);
    try {
      await telechargerFichier(`document/${id}/telecharger`, nom);
    } catch (err) {
      setErreur(messageErreur(err, "Téléchargement impossible."));
    }
  };

  return (
    <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 text-sm">
      <Bloc titre="Capacité financière">
        <Ligne libelle="Avance exigée">{formaterMontant(simulation.avance)}</Ligne>
        <Ligne libelle="Apport proposé">{formaterMontant(candidature.apportInitial)}</Ligne>
        <div className="my-2">
          <div className="h-2 rounded-full bg-gray-100 overflow-hidden">
            <div className="h-full bg-immo-gold" style={{ width: `${Math.round(simulation.couverture * 100)}%` }} />
          </div>
          <p className="text-xs text-gray-500 mt-1">
            {Math.round(simulation.couverture * 100)} % de l'avance, soit{" "}
            {simulation.moisCouverts.toLocaleString("fr-FR", { maximumFractionDigits: 1 })} mois de loyer
          </p>
        </div>
        {!estAdmin ? null : simulation.financement ? (
          <>
            <Ligne libelle="À financer">{formaterMontant(simulation.financement.montant)}</Ligne>
            <Ligne libelle="Remboursement">
              {simulation.financement.nombreMensualites} ×{" "}
              {formaterMontant(Math.round(simulation.financement.mensualite))}
            </Ligne>
            <p className="text-xs text-gray-400 mt-1">Avec {tauxFrais} % de frais.</p>
          </>
        ) : (
          <p className="text-green-700 text-xs mt-1">L'apport couvre toute l'avance : aucun financement.</p>
        )}
      </Bloc>

      <Bloc titre="Profil du candidat">
        <Ligne libelle="Situation">{situation?.label ?? candidature.situationProfessionnelle}</Ligne>
        {situation && <p className="text-xs text-gray-500 -mt-1 mb-2 text-right">{situation.note}</p>}
        <Ligne libelle="Garant">{garant ? garant.nom : "Aucun"}</Ligne>
        {garant && (
          <p className="text-xs text-gray-500 text-right">
            {libellesSituation[garant.situationProfessionnelle]?.label ?? garant.situationProfessionnelle} ·{" "}
            {garant.telephone}
          </p>
        )}
        <div className="border-t border-black/5 mt-3 pt-3 space-y-1">
          <p className="flex items-center gap-2 text-gray-700">
            <Phone size={14} className="text-gray-400" />
            {candidature.locataireTelephone || "-"}
          </p>
          <p className="flex items-center gap-2 text-gray-700 break-all">
            <Mail size={14} className="text-gray-400 shrink-0" />
            {candidature.locataireEmail || "-"}
          </p>
        </div>
      </Bloc>

      <Bloc titre={`Justificatifs (${documents.length})`}>
        <ul className="space-y-1 mb-3">
          {justificatifsAttendus.map((j) => {
            const present = documentsLocataire.some((d) => d.typeDocument === j.type);
            return (
              <li key={j.type} className={`flex items-center gap-2 ${present ? "text-gray-700" : "text-gray-400"}`}>
                {present ? <Check size={14} className="text-green-600" /> : <X size={14} className="text-red-500" />}
                {j.label}
              </li>
            );
          })}
        </ul>
        {documents.length > 0 && (
          <ul className="space-y-1 border-t border-black/5 pt-3">
            {documents.map((d) => (
              <li key={d.id}>
                <button
                  onClick={() => telecharger(d.id, d.nomFichierOriginal)}
                  className="inline-flex items-start gap-1.5 text-left text-immo-dark hover:text-immo-gold"
                >
                  <Download size={14} className="shrink-0 mt-0.5" />
                  <span>
                    <span className="underline underline-offset-2 break-all">{d.nomFichierOriginal}</span>{" "}
                    <span className="text-gray-400 text-xs">
                      ({libellesDocument[d.typeDocument] ?? d.typeDocument}
                      {d.proprietaire === "Garant" ? ", garant" : ""})
                    </span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </Bloc>

      {decisionPossible && pointsAttention.length > 0 && (
        <div className="lg:col-span-3 bg-amber-50 border border-amber-200 rounded-xl p-4">
          <p className="font-semibold text-amber-800 flex items-center gap-2 mb-1">
            <AlertTriangle size={16} />
            Points d'attention
          </p>
          <ul className="list-disc pl-6 text-amber-800 space-y-0.5">
            {pointsAttention.map((p) => (
              <li key={p}>{p}</li>
            ))}
          </ul>
        </div>
      )}

      <VisiteCandidature
        candidature={candidature}
        logementAttribue={logementAttribue}
        nbPreselectionnes={nbPreselectionnes}
        onMiseAJour={onMiseAJour}
      />

      <DemandesDocuments candidature={candidature} onMiseAJour={onMiseAJour} />

      <div className="lg:col-span-3 bg-white rounded-xl border border-black/5 p-4">
        {decisionPossible && logementAttribue ? (
          <p className="text-gray-600">
            <strong className="text-blue-700">En liste d'attente.</strong> Le logement est attribué à un autre candidat.
            Vous pourrez accepter ce dossier si l'attribution est annulée (désistement du candidat retenu).
          </p>
        ) : candidature.statut === "Acceptee" && !estAdmin ? (
          <div className="text-gray-600 space-y-1">
            <p className="font-semibold text-green-700 flex items-center gap-1.5">
              <Check size={16} />
              Dossier accepté le {formaterDate(candidature.dateTraitement)}
            </p>
            <p>
              ImmoPlus prend le relais : paiement de l'apport, remise des clés et, le cas échéant, financement.
              {candidature.dateRemiseCles
                ? ` Les clés ont été remises le ${formaterDate(candidature.dateRemiseCles)}.`
                : candidature.dateApportPaye
                  ? " L'apport est payé ; la remise des clés est en cours d'organisation."
                  : " En attente du paiement de l'apport."}
            </p>
          </div>
        ) : candidature.statut === "Acceptee" ? (
          <div className="space-y-4">
            <SuiviAttribution candidature={candidature} onMiseAJour={onMiseAJour} />
            {!candidature.dateApportPaye && (
              <div className="flex flex-wrap items-end gap-2 border-t border-black/5 pt-4">
                <p className="text-gray-500 w-full mb-1">
                  Le candidat se désiste ? Annulez l'attribution pour choisir un remplaçant dans la liste d'attente. Ce
                  n'est plus possible une fois l'apport payé.
                </p>
                <label className="text-sm font-medium text-gray-700 flex-1 min-w-[16rem]">
                  Motif de l'annulation (envoyé au candidat)
                  <input
                    value={motifRefus}
                    onChange={(e) => setMotifRefus(e.target.value)}
                    placeholder="Ex. : désistement du candidat"
                    className={classeChamp}
                  />
                </label>
                <button
                  onClick={() => traiter("annuler-attribution")}
                  disabled={enCours}
                  className={classeBoutonDanger()}
                >
                  <X size={16} />
                  Annuler l'attribution
                </button>
              </div>
            )}
          </div>
        ) : decisionPossible ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {!visiteEffectuee && (
              <p className="md:col-span-2 text-gray-500">
                Pour accepter ce dossier, organisez d'abord une visite avec le candidat (bloc « Visite du logement »
                ci-dessus). Vous pouvez le refuser à tout moment.
              </p>
            )}
            <div className="flex items-end gap-2">
              {!estAdmin ? (
                <p className="text-sm text-gray-500 flex-1 self-center">
                  Après acceptation, ImmoPlus prend en charge l'apport, la remise des clés et le financement.
                </p>
              ) : simulation.financement ? (
                <label className="text-sm font-medium text-gray-700 flex-1">
                  Taux de frais du financement (2 à 5 %)
                  <input
                    type="number"
                    min="2"
                    max="5"
                    step="0.5"
                    value={tauxFrais}
                    onChange={(e) => setTauxFrais(e.target.value)}
                    className={classeChamp}
                  />
                </label>
              ) : (
                <p className="text-sm text-gray-500 flex-1 self-center">Aucun financement à prévoir pour ce dossier.</p>
              )}
              <button
                onClick={() => traiter("accepter")}
                disabled={enCours || !visiteEffectuee}
                title={visiteEffectuee ? undefined : "Disponible après une visite effectuée avec ce candidat"}
                className={`${classeBoutonPrincipal()} disabled:cursor-not-allowed`}
              >
                <Check size={16} />
                Accepter
              </button>
            </div>
            <div className="flex items-end gap-2">
              <label className="text-sm font-medium text-gray-700 flex-1">
                Motif du refus (envoyé au candidat)
                <input value={motifRefus} onChange={(e) => setMotifRefus(e.target.value)} className={classeChamp} />
              </label>
              <button onClick={() => traiter("refuser")} disabled={enCours} className={classeBoutonDanger()}>
                <X size={16} />
                Refuser
              </button>
            </div>
          </div>
        ) : (
          <p className="text-gray-600">
            Dossier {candidature.statut === "Annulee" ? "annulé" : "refusé"} le{" "}
            {formaterDate(candidature.dateTraitement)}
            {candidature.motifRefus && <> — motif : {candidature.motifRefus}</>}
            {candidature.locationId && <> — location n° {formaterReference(candidature.locationId)}</>}
          </p>
        )}
        {erreur && <p className="text-red-600 text-sm mt-3">{erreur}</p>}
      </div>
    </div>
  );
}
