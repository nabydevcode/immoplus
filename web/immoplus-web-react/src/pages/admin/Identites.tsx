import { useEffect, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Check, ExternalLink, FileText, IdCard, X } from "lucide-react";
import { apiClient, chargerFichierProtegeAvecType } from "../../api/client";
import { Badge, EnTete, EtatListe, MessageErreur, Onglets, Pagination } from "../../components/admin/ui";
import { useListeAOnglets } from "../../components/admin/pagination";
import {
  classeBoutonDanger,
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterDateHeure,
  messageErreur,
} from "../../components/admin/outils";
import { lienAdminCible } from "../../components/admin/liens";
import { signalerCompteursAdmin } from "../../components/admin/compteurs";
import { infoStatutPiece, libelleTypePiece, statutPiece } from "../../components/identite/identite";
import type { PieceIdentiteDto, StatutPieceIdentite } from "../../types/models";

type Filtre = StatutPieceIdentite | "Toutes";

/** Fichier privé (recto/verso) chargé avec le jeton : image affichée, PDF ouvert dans un cadre. */
function ApercuFichier({ pieceId, face }: { pieceId: number; face: "recto" | "verso" }) {
  const [fichier, setFichier] = useState<{ url: string; type: string } | null>(null);
  const [erreur, setErreur] = useState<string | null>(null);

  useEffect(() => {
    let annule = false;
    let url: string | null = null;
    chargerFichierProtegeAvecType(`admin/identites/${pieceId}/fichier/${face}`)
      .then((f) => {
        url = f.url;
        if (annule) URL.revokeObjectURL(f.url);
        else setFichier(f);
      })
      .catch((err) => {
        if (!annule) setErreur(messageErreur(err, "Fichier indisponible."));
      });
    return () => {
      annule = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [pieceId, face]);

  return (
    <figure className="flex flex-col gap-1.5 min-w-0">
      <figcaption className="text-xs font-semibold text-gray-600 capitalize">{face}</figcaption>
      {erreur ? (
        <MessageErreur>{erreur}</MessageErreur>
      ) : !fichier ? (
        <div className="h-48 rounded-xl bg-gray-200 animate-pulse" />
      ) : fichier.type === "application/pdf" ? (
        <div className="flex flex-col gap-2">
          <iframe src={fichier.url} title={`${face} (PDF)`} className="w-full h-64 rounded-xl border border-black/10" />
          <a
            href={fichier.url}
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-1 text-xs font-semibold underline"
          >
            <FileText size={13} />
            Ouvrir le PDF
          </a>
        </div>
      ) : (
        <a href={fichier.url} target="_blank" rel="noreferrer" title="Ouvrir en grand">
          <img
            src={fichier.url}
            alt={`Pièce d'identité, ${face}`}
            className="w-full max-h-72 object-contain rounded-xl border border-black/10 bg-gray-50"
          />
        </a>
      )}
    </figure>
  );
}

/** Validation ou refus (avec motif) d'une demande en attente. */
function Decision({ piece, onDecide }: { piece: PieceIdentiteDto; onDecide: () => void }) {
  const [refus, setRefus] = useState(false);
  const [motif, setMotif] = useState("");
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);

  const decider = async (valider: boolean, e?: FormEvent) => {
    e?.preventDefault();
    setErreur(null);
    if (!valider && !motif.trim()) return setErreur("Indiquez le motif du refus : il est envoyé au demandeur.");
    setEnCours(true);
    try {
      if (valider) await apiClient.post(`admin/identites/${piece.id}/valider`);
      else await apiClient.post(`admin/identites/${piece.id}/refuser`, { motif: motif.trim() });
      signalerCompteursAdmin();
      onDecide();
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'enregistrer la décision."));
      setEnCours(false);
    }
  };

  if (refus) {
    return (
      <form onSubmit={(e) => decider(false, e)} className="flex flex-col gap-2">
        <label className="text-xs font-semibold text-gray-600">
          Motif du refus
          <textarea
            value={motif}
            onChange={(e) => setMotif(e.target.value)}
            rows={2}
            maxLength={500}
            placeholder="Ex. photo floue, pièce expirée, nom différent du compte..."
            className={classeChamp}
          />
        </label>
        {erreur && <MessageErreur>{erreur}</MessageErreur>}
        <div className="flex flex-wrap gap-2">
          <button type="submit" disabled={enCours} className={classeBoutonDanger(true)}>
            <X size={14} />
            {enCours ? "Envoi..." : "Refuser la pièce"}
          </button>
          <button type="button" onClick={() => setRefus(false)} className={classeBoutonSecondaire(true)}>
            Annuler
          </button>
        </div>
      </form>
    );
  }
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap gap-2">
        <button type="button" onClick={() => decider(true)} disabled={enCours} className={classeBoutonPrincipal(true)}>
          <Check size={14} />
          {enCours ? "Validation..." : "Valider l'identité"}
        </button>
        <button type="button" onClick={() => setRefus(true)} className={classeBoutonDanger(true)}>
          <X size={14} />
          Refuser
        </button>
      </div>
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
    </div>
  );
}

const ONGLETS = ["EnAttente", "Validee", "Refusee", "Toutes"] as const;
const requeteOnglet = (f: Filtre) => (f === "Toutes" ? "" : `statut=${f}`);
const LOCAL = {
  dansOnglet: (f: Filtre, p: PieceIdentiteDto) => f === "Toutes" || statutPiece(p.statut) === f,
};

/** Demandes de vérification d'identité : aperçu des pièces (privées) et décision. */
export default function Identites() {
  const [filtre, setFiltre] = useState<Filtre>("EnAttente");
  const liste = useListeAOnglets<PieceIdentiteDto, Filtre>({
    base: "admin/identites",
    onglet: filtre,
    onglets: ONGLETS,
    requeteOnglet,
    mode: "pages",
    local: LOCAL,
  });
  const { chargement, erreur, recharger, compteurs } = liste;
  const pieces = liste.elements;
  const nombre = (f: Filtre) => (compteurs[f] !== undefined ? ` (${compteurs[f]})` : "");
  const [ouverte, setOuverte] = useState<number | null>(null);

  return (
    <>
      <EnTete
        titre="Identités"
        description="Pièces d'identité envoyées par les bailleurs et locataires. Une pièce validée affiche le badge « Identité vérifiée »."
      />

      <Onglets
        valeur={filtre}
        onChange={(f) => {
          setFiltre(f);
          setOuverte(null);
        }}
        options={[
          { valeur: "EnAttente", label: `En attente${nombre("EnAttente")}` },
          { valeur: "Validee", label: `Validées${nombre("Validee")}` },
          { valeur: "Refusee", label: `Refusées${nombre("Refusee")}` },
          { valeur: "Toutes", label: `Toutes${nombre("Toutes")}` },
        ]}
      />

      <EtatListe
        chargement={chargement}
        erreur={erreur}
        vide={pieces.length === 0}
        messageVide={filtre === "EnAttente" ? "Aucune pièce à vérifier." : "Aucune demande."}
      >
        <ul className="flex flex-col gap-4">
          {pieces.map((p) => {
            const statut = infoStatutPiece(p.statut);
            const enAttente = statutPiece(p.statut) === "EnAttente";
            const visible = ouverte === p.id;
            const lienCompte = lienAdminCible("Utilisateur", p.utilisateurId, p.utilisateurEmail ?? p.utilisateurNom);
            return (
              <li key={p.id} className="bg-white rounded-2xl border border-black/5 shadow-sm p-4 sm:p-5 text-sm">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="flex items-start gap-3 min-w-0">
                    <span className="flex items-center justify-center w-9 h-9 rounded-full bg-immo-dark/5 text-gray-600 shrink-0">
                      <IdCard size={17} />
                    </span>
                    <div className="min-w-0">
                      <p className="font-bold wrap-break-word">
                        {p.utilisateurNom ?? `Compte n° ${p.utilisateurId}`}
                        {p.utilisateurRole && <span className="font-normal text-gray-500"> · {p.utilisateurRole}</span>}
                      </p>
                      {p.utilisateurEmail && <p className="text-xs text-gray-500 break-all">{p.utilisateurEmail}</p>}
                      <p className="text-xs text-gray-500">
                        {libelleTypePiece(p.type)} · envoyée le {formaterDateHeure(p.dateEnvoi)}
                        {p.dateDecision && ` · décision le ${formaterDateHeure(p.dateDecision)}`}
                      </p>
                    </div>
                  </div>
                  <Badge ton={statut.ton}>{statut.libelle}</Badge>
                </div>
                {p.motifRefus && <p className="mt-2 text-red-700">Motif du refus : {p.motifRefus}</p>}

                <div className="flex flex-wrap items-center gap-3 mt-4">
                  <button
                    type="button"
                    onClick={() => setOuverte(visible ? null : p.id)}
                    aria-expanded={visible}
                    className={classeBoutonSecondaire(true)}
                  >
                    {visible ? "Masquer la pièce" : "Voir la pièce"}
                  </button>
                  {lienCompte && (
                    <Link
                      to={lienCompte}
                      className="inline-flex items-center gap-1 text-xs font-semibold underline text-gray-600"
                    >
                      <ExternalLink size={12} />
                      Compte
                    </Link>
                  )}
                </div>

                {visible && (
                  <div className="mt-4 flex flex-col gap-4">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <ApercuFichier pieceId={p.id} face="recto" />
                      {p.aVerso !== false && <ApercuFichier pieceId={p.id} face="verso" />}
                    </div>
                    {enAttente && (
                      <Decision
                        piece={p}
                        onDecide={() => {
                          setOuverte(null);
                          recharger();
                        }}
                      />
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
        <Pagination page={liste.page} totalPages={liste.totalPages} onChange={liste.setPage} total={liste.total} />
      </EtatListe>
    </>
  );
}
