import { useEffect, useState } from "react";
import { Camera, ClipboardCheck, ImagePlus, Trash2, X } from "lucide-react";
import { apiClient, chargerFichierProtege } from "../../api/client";
import { classeChamp, messageErreur, useChargement } from "../admin/outils";
import { MessageErreur } from "../admin/ui";
import type { PhotoEtatDesLieuxDto, ReservationDto } from "../../types/models";
import { jourLocal } from "../sejour/dates";
import BoutonPdf from "../documents/BoutonPdf";
import { etatDesLieuxPdf } from "../documents/pdf";
import {
  DELAI_SUPPRESSION_MS,
  fenetreAjout,
  horodatage,
  momentDe,
  PHOTOS_MAX_PAR_MOMENT,
  TAILLE_MAX_PHOTO_EDL,
  TYPES_PHOTO_EDL,
  type MomentEtatDesLieux,
} from "./etatDesLieux";

const TITRES: Record<MomentEtatDesLieux, string> = { Arrivee: "À l'arrivée", Depart: "Au départ" };

/** Vignette d'une photo protégée (chargée avec le jeton d'authentification). */
function VignetteProtegee({ photo, onOuvrir }: { photo: PhotoEtatDesLieuxDto; onOuvrir: (src: string) => void }) {
  const [src, setSrc] = useState<string | null>(null);
  const [echec, setEchec] = useState(false);
  useEffect(() => {
    let annule = false;
    let url: string | null = null;
    chargerFichierProtege(photo.url)
      .then((u) => {
        if (annule) URL.revokeObjectURL(u);
        else {
          url = u;
          setSrc(u);
        }
      })
      .catch(() => {
        if (!annule) setEchec(true);
      });
    return () => {
      annule = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [photo.url]);

  return (
    <button
      type="button"
      onClick={() => src && onOuvrir(src)}
      className="block w-full aspect-4/3 rounded-lg overflow-hidden bg-gray-100"
      aria-label={photo.commentaire ? `Photo : ${photo.commentaire}` : "Agrandir la photo"}
    >
      {src ? (
        <img src={src} alt={photo.commentaire ?? ""} decoding="async" className="w-full h-full object-cover" />
      ) : (
        <span className={`block w-full h-full ${echec ? "" : "animate-pulse bg-gray-200"}`}>
          {echec && <span className="text-xs text-gray-400 p-2 block">Photo indisponible</span>}
        </span>
      )}
    </button>
  );
}

function Visionneuse({ src, photo, onFermer }: { src: string; photo: PhotoEtatDesLieuxDto; onFermer: () => void }) {
  useEffect(() => {
    const touche = (e: KeyboardEvent) => e.key === "Escape" && onFermer();
    window.addEventListener("keydown", touche);
    return () => window.removeEventListener("keydown", touche);
  }, [onFermer]);
  return (
    <div
      className="fixed inset-0 z-[100] bg-black/90 flex flex-col items-center justify-center p-4"
      onClick={onFermer}
      role="dialog"
      aria-modal="true"
    >
      <button type="button" onClick={onFermer} className="absolute top-4 right-4 text-white p-2" aria-label="Fermer">
        <X size={26} />
      </button>
      <img src={src} alt={photo.commentaire ?? ""} className="max-w-full max-h-[80vh] object-contain rounded-lg" />
      <p className="text-white/90 text-sm mt-3 text-center max-w-xl">
        {photo.commentaire && <span className="block mb-1">{photo.commentaire}</span>}
        <span className="text-white/60 text-xs">
          {photo.auteurNom} · {new Date(horodatage(photo.dateCreation)).toLocaleString("fr-FR")}
        </span>
      </p>
    </div>
  );
}

function SectionMoment({
  reservation,
  moment,
  photos,
  utilisateurId,
  maintenant,
  aujourdhui,
  onModifie,
  onOuvrir,
}: {
  reservation: ReservationDto;
  moment: MomentEtatDesLieux;
  photos: PhotoEtatDesLieuxDto[];
  utilisateurId: number | null;
  maintenant: number;
  aujourdhui: string;
  onModifie: () => void;
  onOuvrir: (photo: PhotoEtatDesLieuxDto, src: string) => void;
}) {
  const [commentaire, setCommentaire] = useState("");
  const [progression, setProgression] = useState<string | null>(null);
  const [erreur, setErreur] = useState<string | null>(null);
  const [suppression, setSuppression] = useState<number | null>(null);
  const fenetre = fenetreAjout(reservation, moment, aujourdhui);
  const miennes = photos.filter((p) => p.auteurId === utilisateurId).length;
  const restantes = PHOTOS_MAX_PAR_MOMENT - miennes;

  const envoyer = async (fichiers: FileList | null) => {
    setErreur(null);
    const liste = Array.from(fichiers ?? []);
    if (liste.length === 0) return;
    const refus: string[] = [];
    const valides = liste.filter((f) => {
      if (!TYPES_PHOTO_EDL.includes(f.type)) refus.push(`${f.name} (format non accepté : JPG, PNG ou WEBP)`);
      else if (f.size > TAILLE_MAX_PHOTO_EDL) refus.push(`${f.name} (plus de 10 Mo)`);
      else return true;
      return false;
    });
    const aEnvoyer = valides.slice(0, Math.max(restantes, 0));
    if (valides.length > aEnvoyer.length) refus.push(`${PHOTOS_MAX_PAR_MOMENT} photos maximum par étape`);
    for (const [i, fichier] of aEnvoyer.entries()) {
      setProgression(`Envoi ${i + 1}/${aEnvoyer.length}...`);
      const formulaire = new FormData();
      formulaire.append("fichier", fichier);
      formulaire.append("moment", moment);
      if (commentaire.trim()) formulaire.append("commentaire", commentaire.trim());
      try {
        await apiClient.postFormulaire(`reservation/${reservation.id}/etat-des-lieux`, formulaire);
      } catch (err) {
        refus.push(`${fichier.name} (${messageErreur(err, "envoi impossible")})`);
      }
    }
    setProgression(null);
    if (aEnvoyer.length > 0) {
      setCommentaire("");
      onModifie();
    }
    if (refus.length) setErreur(`Photos non ajoutées : ${refus.join(", ")}.`);
  };

  const supprimer = async (photo: PhotoEtatDesLieuxDto) => {
    if (!window.confirm("Supprimer cette photo de l'état des lieux ?")) return;
    setErreur(null);
    setSuppression(photo.id);
    try {
      await apiClient.delete(`etat-des-lieux/${photo.id}`);
      onModifie();
    } catch (err) {
      setErreur(messageErreur(err, "Impossible de supprimer la photo."));
    } finally {
      setSuppression(null);
    }
  };

  const classeAjout =
    "inline-flex items-center gap-1.5 text-xs font-semibold px-3 py-2 rounded-lg cursor-pointer transition-colors";

  return (
    <div className="flex flex-col gap-3">
      <h4 className="text-sm font-bold">
        {TITRES[moment]} <span className="text-gray-400 font-normal">({photos.length})</span>
      </h4>
      {photos.length === 0 ? (
        <p className="text-xs text-gray-500">Aucune photo pour l'instant.</p>
      ) : (
        <ul className="grid grid-cols-2 min-[480px]:grid-cols-3 sm:grid-cols-4 gap-3">
          {photos.map((p) => {
            const moi = p.auteurId === utilisateurId;
            const supprimable = moi && maintenant - horodatage(p.dateCreation) < DELAI_SUPPRESSION_MS;
            return (
              <li key={p.id} className="relative min-w-0">
                <VignetteProtegee photo={p} onOuvrir={(src) => onOuvrir(p, src)} />
                {supprimable && (
                  <button
                    type="button"
                    onClick={() => supprimer(p)}
                    disabled={suppression === p.id}
                    className="absolute top-1 right-1 w-7 h-7 rounded-full bg-black/60 text-white flex items-center justify-center hover:bg-red-600"
                    aria-label="Supprimer cette photo"
                  >
                    <Trash2 size={13} />
                  </button>
                )}
                <p className="text-[11px] text-gray-500 mt-1 truncate">
                  {moi ? "Vous" : p.auteurNom}
                  {" · "}
                  {p.auteurId === reservation.locataireId ? "voyageur" : "hôte"}
                </p>
                {p.commentaire && <p className="text-xs text-gray-700 line-clamp-2 wrap-break-word">{p.commentaire}</p>}
              </li>
            );
          })}
        </ul>
      )}

      {fenetre.ouverte ? (
        restantes > 0 ? (
          <div className="flex flex-col gap-2 bg-immo-dark/[0.03] rounded-xl p-3">
            <input
              value={commentaire}
              onChange={(e) => setCommentaire(e.target.value)}
              maxLength={500}
              placeholder="Commentaire facultatif (ex. rayure sur la table du salon)"
              aria-label={`Commentaire des photos ${TITRES[moment].toLowerCase()}`}
              className={`${classeChamp} !mt-0`}
            />
            <div className="flex flex-wrap items-center gap-2">
              <label className={`${classeAjout} bg-immo-gold text-immo-dark hover:bg-immo-gold-light`}>
                <Camera size={14} />
                Prendre une photo
                <input
                  type="file"
                  accept="image/*"
                  capture="environment"
                  className="sr-only"
                  disabled={progression !== null}
                  onChange={(e) => {
                    envoyer(e.target.files);
                    e.target.value = "";
                  }}
                />
              </label>
              <label className={`${classeAjout} border border-gray-300 text-gray-700 bg-white hover:bg-gray-50`}>
                <ImagePlus size={14} />
                Choisir des photos
                <input
                  type="file"
                  accept="image/jpeg,image/png,image/webp"
                  multiple
                  className="sr-only"
                  disabled={progression !== null}
                  onChange={(e) => {
                    envoyer(e.target.files);
                    e.target.value = "";
                  }}
                />
              </label>
              <span className="text-xs text-gray-500">
                {progression ?? `JPG, PNG ou WEBP, 10 Mo max · encore ${restantes} photo${restantes > 1 ? "s" : ""}`}
              </span>
            </div>
          </div>
        ) : (
          <p className="text-xs text-gray-500">Vous avez atteint le maximum de {PHOTOS_MAX_PAR_MOMENT} photos.</p>
        )
      ) : (
        <p className="text-xs text-gray-500">{fenetre.message}</p>
      )}
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
    </div>
  );
}

/**
 * État des lieux en photos d'une réservation confirmée : à l'arrivée et au départ,
 * par le voyageur et l'hôte (chacun voit les photos des deux).
 */
export default function EtatDesLieuxPhotos({
  reservation,
  utilisateurId,
}: {
  reservation: ReservationDto;
  utilisateurId: number | null;
}) {
  const { donnees, chargement, erreur, recharger } = useChargement<PhotoEtatDesLieuxDto[]>(
    `reservation/${reservation.id}/etat-des-lieux`,
  );
  const [aujourdhui] = useState(jourLocal);
  const [maintenant, setMaintenant] = useState(() => Date.now());
  const [ouverte, setOuverte] = useState<{ photo: PhotoEtatDesLieuxDto; src: string } | null>(null);
  const photos = [...(donnees ?? [])].sort((a, b) => a.dateCreation.localeCompare(b.dateCreation));
  const modifie = () => {
    setMaintenant(Date.now());
    recharger();
  };

  return (
    <section className="border border-black/10 rounded-xl p-4 flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="font-bold flex items-center gap-2">
            <ClipboardCheck size={17} className="text-immo-gold" />
            État des lieux en photos
          </h3>
          <p className="text-xs text-gray-500 mt-0.5">
            Photographiez le logement à l'arrivée et au départ : en cas de litige sur la caution, ces photos font foi.
          </p>
        </div>
        {photos.length > 0 && <BoutonPdf fichier={etatDesLieuxPdf(reservation.id)} libelle="État des lieux (PDF)" />}
      </div>
      {chargement ? (
        <p className="text-sm text-gray-500">Chargement...</p>
      ) : erreur ? (
        <MessageErreur>{erreur}</MessageErreur>
      ) : (
        (["Arrivee", "Depart"] as const).map((moment) => (
          <SectionMoment
            key={moment}
            reservation={reservation}
            moment={moment}
            photos={photos.filter((p) => momentDe(p) === moment)}
            utilisateurId={utilisateurId}
            maintenant={maintenant}
            aujourdhui={aujourdhui}
            onModifie={modifie}
            onOuvrir={(photo, src) => setOuverte({ photo, src })}
          />
        ))
      )}
      {ouverte && <Visionneuse src={ouverte.src} photo={ouverte.photo} onFermer={() => setOuverte(null)} />}
    </section>
  );
}
