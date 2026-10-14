import { useState, type FormEvent } from "react";
import { Check, Clock, FileQuestion, Send, X } from "lucide-react";
import { apiClient } from "../../api/client";
import {
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterDate,
  messageErreur,
} from "../admin/outils";
import type { CandidatureDto } from "../../types/models";
import { libellePiece } from "./analyse";

const TYPES = ["PieceIdentite", "JustificatifRevenu", "JustificatifDomicile", "Autre"] as const;
const PRINCIPAUX = ["PieceIdentite", "JustificatifRevenu", "JustificatifDomicile"];

const dansNJours = (n: number) => {
  const d = new Date();
  d.setDate(d.getDate() + n);
  return d.toISOString().slice(0, 10);
};

/**
 * Côté bailleur/admin : historique des demandes de pièces et formulaire pour en envoyer une nouvelle.
 * Le candidat reçoit un email listant les pièces et la date limite.
 */
export default function DemandesDocuments({
  candidature,
  onMiseAJour,
}: {
  candidature: CandidatureDto;
  onMiseAJour: (c: CandidatureDto) => void;
}) {
  const demandes = candidature.demandesComplement ?? [];
  const proprietaires = candidature.garant ? (["Locataire", "Garant"] as const) : (["Locataire"] as const);

  // Pré-coche les pièces principales absentes du dossier.
  const manquantes = () =>
    new Set(
      proprietaires.flatMap((proprietaire) =>
        PRINCIPAUX.filter(
          (type) => !candidature.documents.some((d) => d.proprietaire === proprietaire && d.typeDocument === type),
        ).map((type) => `${proprietaire}:${type}`),
      ),
    );

  const [ouvert, setOuvert] = useState(false);
  const [cochees, setCochees] = useState<Set<string>>(manquantes);
  const [dateLimite, setDateLimite] = useState(dansNJours(7));
  const [message, setMessage] = useState("");
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);
  const [envoye, setEnvoye] = useState(false);

  const basculer = (cle: string) =>
    setCochees((s) => {
      const copie = new Set(s);
      if (copie.has(cle)) copie.delete(cle);
      else copie.add(cle);
      return copie;
    });

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    if (cochees.size === 0) {
      setErreur("Cochez au moins une pièce à demander.");
      return;
    }
    setEnCours(true);
    try {
      const pieces = [...cochees].map((cle) => {
        const [proprietaire, typeDocument] = cle.split(":");
        return { proprietaire, typeDocument };
      });
      onMiseAJour(
        await apiClient.post<CandidatureDto>(`candidature/${candidature.id}/demande-complement`, {
          pieces,
          dateLimite,
          message: message.trim() || null,
        }),
      );
      setOuvert(false);
      setEnvoye(true);
      setMessage("");
    } catch (err) {
      setErreur(messageErreur(err, "La demande n'a pas pu être envoyée."));
    } finally {
      setEnCours(false);
    }
  };

  const enAttente = candidature.statut === "EnAttente";
  if (demandes.length === 0 && !enAttente) return null;

  return (
    <div className="lg:col-span-3 bg-white rounded-xl border border-black/5 p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="font-semibold flex items-center gap-2">
          <FileQuestion size={16} className="text-immo-gold" />
          Documents demandés au candidat
        </p>
        {enAttente && !ouvert && (
          <button
            onClick={() => {
              setCochees(manquantes());
              setEnvoye(false);
              setOuvert(true);
            }}
            className={classeBoutonSecondaire(true)}
          >
            <Send size={14} />
            {demandes.length ? "Nouvelle demande" : "Demander des documents"}
          </button>
        )}
      </div>

      {envoye && (
        <p className="mt-3 text-sm bg-green-50 text-green-700 rounded-lg px-3 py-2">
          Demande envoyée : {candidature.locataireNom} vient de recevoir un email avec la liste des pièces et la date
          limite.
        </p>
      )}

      {ouvert && (
        <form onSubmit={envoyer} className="mt-4 grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
          <div className="space-y-3">
            {proprietaires.map((proprietaire) => (
              <fieldset key={proprietaire}>
                <legend className="font-medium text-gray-700 mb-1">
                  {proprietaire === "Locataire" ? "Pièces du candidat" : "Pièces du garant"}
                </legend>
                {TYPES.map((typeDocument) => {
                  const cle = `${proprietaire}:${typeDocument}`;
                  const fourni = candidature.documents.some(
                    (d) => d.proprietaire === proprietaire && d.typeDocument === typeDocument,
                  );
                  return (
                    <label key={cle} className="flex items-center gap-2 py-0.5 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={cochees.has(cle)}
                        onChange={() => basculer(cle)}
                        className="accent-immo-gold w-4 h-4"
                      />
                      {libellePiece({ proprietaire, typeDocument })}
                      {fourni && <span className="text-xs text-green-600">(déjà fourni)</span>}
                    </label>
                  );
                })}
              </fieldset>
            ))}
          </div>
          <div className="space-y-3">
            <label className="block font-medium text-gray-700">
              Date limite
              <input
                type="date"
                value={dateLimite}
                min={dansNJours(1)}
                max={dansNJours(60)}
                onChange={(e) => setDateLimite(e.target.value)}
                className={classeChamp}
                required
              />
            </label>
            <label className="block font-medium text-gray-700">
              Message au candidat (optionnel)
              <textarea
                value={message}
                onChange={(e) => setMessage(e.target.value)}
                rows={3}
                maxLength={500}
                placeholder="Ex. : merci de fournir vos 3 derniers bulletins de salaire."
                className={classeChamp}
              />
            </label>
            {erreur && <p className="text-red-600">{erreur}</p>}
            <div className="flex gap-2">
              <button type="submit" disabled={enCours} className={classeBoutonPrincipal(true)}>
                <Send size={14} />
                {enCours ? "Envoi..." : "Envoyer la demande par email"}
              </button>
              <button type="button" onClick={() => setOuvert(false)} className={classeBoutonSecondaire(true)}>
                Annuler
              </button>
            </div>
          </div>
        </form>
      )}

      {demandes.length > 0 && (
        <ul className="mt-4 space-y-3">
          {demandes.map((d) => (
            <li key={d.id} className="border border-black/5 rounded-lg p-3 text-sm">
              <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
                <span className="text-gray-500">
                  Demandée le {formaterDate(d.dateDemande)} · à fournir avant le{" "}
                  <strong className="text-gray-800">{formaterDate(d.dateLimite)}</strong>
                </span>
                <span
                  className={`text-xs font-semibold px-2.5 py-1 rounded-full inline-flex items-center gap-1 ${
                    d.complete
                      ? "bg-green-100 text-green-700"
                      : d.depassee
                        ? "bg-red-100 text-red-700"
                        : "bg-amber-100 text-amber-700"
                  }`}
                >
                  {d.complete ? <Check size={12} /> : <Clock size={12} />}
                  {d.complete ? "Dossier complété" : d.depassee ? "Date limite dépassée" : "En attente du candidat"}
                </span>
              </div>
              <ul className="flex flex-wrap gap-x-4 gap-y-1">
                {d.pieces.map((p) => (
                  <li
                    key={`${p.proprietaire}${p.typeDocument}`}
                    className={`inline-flex items-center gap-1 ${p.recue ? "text-gray-700" : "text-gray-400"}`}
                  >
                    {p.recue ? (
                      <Check size={14} className="text-green-600" />
                    ) : (
                      <X size={14} className="text-red-500" />
                    )}
                    {libellePiece(p)}
                  </li>
                ))}
              </ul>
              {d.message && <p className="text-gray-500 mt-2 italic">« {d.message} »</p>}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
