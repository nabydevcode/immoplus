import { useEffect, useState, type FormEvent } from "react";
import { Clock, ShieldCheck, ShieldX, Upload } from "lucide-react";
import { apiClient, ApiError } from "../../api/client";
import type { PieceIdentiteDto, StatutIdentiteDto, TypePieceIdentite } from "../../types/models";
import { MessageErreur } from "../admin/ui";
import {
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterDate,
  messageErreur,
} from "../admin/outils";
import { erreurFichierPiece, FORMATS_PIECE, libelleTypePiece, statutPiece, TYPES_PIECE } from "./identite";

/** Envoi d'une pièce d'identité : type, recto (obligatoire) et verso (facultatif). */
function FormulairePiece({ onEnvoye, onAnnuler }: { onEnvoye: (p: PieceIdentiteDto) => void; onAnnuler?: () => void }) {
  const [type, setType] = useState<TypePieceIdentite>("CNI");
  const [recto, setRecto] = useState<File | null>(null);
  const [verso, setVerso] = useState<File | null>(null);
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    if (!recto) return setErreur("Ajoutez le recto de votre pièce.");
    const probleme = erreurFichierPiece(recto, "Recto") ?? erreurFichierPiece(verso, "Verso");
    if (probleme) return setErreur(probleme);
    const formulaire = new FormData();
    formulaire.append("type", type);
    formulaire.append("recto", recto);
    if (verso) formulaire.append("verso", verso);
    setEnCours(true);
    try {
      onEnvoye(await apiClient.postFormulaire<PieceIdentiteDto>("utilisateur/moi/identite", formulaire));
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'envoyer votre pièce d'identité."));
    } finally {
      setEnCours(false);
    }
  };

  const champFichier = (libelle: string, onChange: (f: File | null) => void, fichier: File | null) => (
    <label className="text-sm font-medium text-gray-700">
      {libelle}
      <input
        type="file"
        accept={FORMATS_PIECE}
        onChange={(e) => {
          onChange(e.target.files?.[0] ?? null);
          setErreur(null);
        }}
        className={`${classeChamp} file:mr-3 file:rounded-md file:border-0 file:bg-immo-dark/5 file:px-3 file:py-1 file:text-sm file:font-semibold`}
      />
      {fichier && (
        <span className="block text-xs text-gray-500 font-normal mt-1 truncate">
          {fichier.name} · {(fichier.size / 1024 / 1024).toLocaleString("fr-FR", { maximumFractionDigits: 1 })} Mo
        </span>
      )}
    </label>
  );

  return (
    <form onSubmit={envoyer} className="flex flex-col gap-4">
      <label className="text-sm font-medium text-gray-700">
        Type de pièce
        <select value={type} onChange={(e) => setType(e.target.value as TypePieceIdentite)} className={classeChamp}>
          {TYPES_PIECE.map((t) => (
            <option key={t.valeur} value={t.valeur}>
              {t.libelle}
            </option>
          ))}
        </select>
      </label>
      {champFichier("Recto (obligatoire)", setRecto, recto)}
      {champFichier("Verso (facultatif)", setVerso, verso)}
      <p className="text-xs text-gray-500">
        Photo ou PDF, 10 Mo maximum par fichier. Vos documents restent privés : seule l'équipe ImmoPlus les consulte
        pour vérifier votre identité.
      </p>
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={enCours} className={classeBoutonPrincipal()}>
          <Upload size={16} />
          {enCours ? "Envoi..." : "Envoyer pour vérification"}
        </button>
        {onAnnuler && (
          <button type="button" onClick={onAnnuler} className={classeBoutonSecondaire()}>
            Annuler
          </button>
        )}
      </div>
    </form>
  );
}

/** Section « Vérifier mon identité » du profil (bailleur, locataire) : statut de la dernière demande et envoi. */
export default function VerificationIdentite() {
  // undefined : chargement ; null : aucune demande.
  const [piece, setPiece] = useState<PieceIdentiteDto | null | undefined>(undefined);
  const [erreur, setErreur] = useState<string | null>(null);
  const [nouvelEnvoi, setNouvelEnvoi] = useState(false);

  useEffect(() => {
    let annule = false;
    apiClient
      .get<StatutIdentiteDto | null>("utilisateur/moi/identite")
      .then((statut) => {
        if (!annule) setPiece(statut?.demande ?? null);
      })
      .catch((err) => {
        if (annule) return;
        if (err instanceof ApiError && err.statut === 404) setPiece(null);
        else setErreur(messageErreur(err, "Impossible de charger l'état de votre vérification."));
      });
    return () => {
      annule = true;
    };
  }, []);

  if (erreur) return <MessageErreur>{erreur}</MessageErreur>;
  if (piece === undefined) return <p className="text-sm text-gray-500">Chargement...</p>;

  const statut = piece ? statutPiece(piece.statut) : undefined;
  const envoyee = (p: PieceIdentiteDto) => {
    setPiece(p);
    setNouvelEnvoi(false);
  };

  if (statut === "Validee") {
    return (
      <div className="flex items-start gap-3 rounded-xl bg-immo-sage-light/60 px-4 py-3 text-sm">
        <ShieldCheck size={20} className="text-immo-sage shrink-0 mt-0.5" />
        <p>
          <strong className="text-immo-sage">Identité vérifiée</strong>
          <span className="block text-gray-600">
            {libelleTypePiece(piece!.type)} validée le {formaterDate(piece!.dateDecision)}. Le badge « Identité vérifiée
            » s'affiche sur vos annonces et votre profil.
          </span>
        </p>
      </div>
    );
  }

  if (statut === "EnAttente") {
    return (
      <div className="flex items-start gap-3 rounded-xl bg-amber-50 border border-amber-100 px-4 py-3 text-sm">
        <Clock size={20} className="text-amber-600 shrink-0 mt-0.5" />
        <p>
          <strong className="text-amber-900">Vérification en cours</strong>
          <span className="block text-gray-600">
            {libelleTypePiece(piece!.type)} envoyée le {formaterDate(piece!.dateEnvoi)}. Un administrateur la vérifie ;
            vous serez prévenu par email.
          </span>
        </p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      {statut === "Refusee" && (
        <div className="flex items-start gap-3 rounded-xl bg-red-50 border border-red-100 px-4 py-3 text-sm">
          <ShieldX size={20} className="text-red-600 shrink-0 mt-0.5" />
          <p>
            <strong className="text-red-800">Pièce refusée le {formaterDate(piece!.dateDecision)}</strong>
            {piece!.motifRefus && <span className="block text-gray-700">Motif : {piece!.motifRefus}</span>}
            <span className="block text-gray-600">Vous pouvez envoyer une nouvelle pièce.</span>
          </p>
        </div>
      )}
      {statut !== "Refusee" || nouvelEnvoi ? (
        <>
          {!piece && (
            <p className="text-sm text-gray-600">
              Faites vérifier votre identité pour obtenir le badge « Identité vérifiée » : il rassure les locataires,
              voyageurs et hôtes avec qui vous échangez.
            </p>
          )}
          <FormulairePiece onEnvoye={envoyee} onAnnuler={piece ? () => setNouvelEnvoi(false) : undefined} />
        </>
      ) : (
        <button type="button" onClick={() => setNouvelEnvoi(true)} className={`${classeBoutonPrincipal()} self-start`}>
          <Upload size={16} />
          Envoyer une nouvelle pièce
        </button>
      )}
    </div>
  );
}
