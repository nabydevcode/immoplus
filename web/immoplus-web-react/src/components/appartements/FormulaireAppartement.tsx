import { lazy, Suspense, useEffect, useRef, useState, type FormEvent } from "react";
import { ImagePlus, MapPin, Rotate3d, Star, X } from "lucide-react";
import { apiClient, ApiError } from "../../api/client";
import { useSourcesPhotos } from "../../hooks/useSourcesPhotos";
import { MessageErreur } from "../admin/ui";
import {
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  messageErreur,
  useChargement,
} from "../admin/outils";
import { useValeurDifferee } from "../admin/pagination";
import type {
  AppartementDto,
  PagedResultDto,
  PhotoDto,
  PolitiqueAnnulation,
  UtilisateurDto,
  VilleDto,
} from "../../types/models";
import { EQUIPEMENTS } from "../sejour/equipements";
import { NOTE_POLITIQUE, POLITIQUES, politiqueDe } from "../sejour/politiques";
import { positionDe, type Position } from "../carte/position";

// La carte (Leaflet) n'est téléchargée qu'à l'ouverture du formulaire.
const SelecteurPosition = lazy(() => import("../carte/SelecteurPosition"));
const INSTRUCTIONS_MAX = 2000;

type TypeOffre = "LongueDuree" | "SejourCourt";

const PHOTOS_MAX = 10;
const PANORAMAS_MAX = 5;
const TAILLE_MAX_PANORAMA = 15 * 1024 * 1024;
const TAILLE_MAX_PHOTO = 5 * 1024 * 1024;
const EXTENSIONS_PHOTO = [".jpg", ".jpeg", ".png", ".webp"];

interface NouvellePhoto {
  cle: string;
  fichier: File;
  apercu: string;
  /** Nom de la pièce (panoramas 360° uniquement). */
  libelle?: string;
}

/** Un panorama 360° (équirectangulaire) est deux fois plus large que haut. */
function estPanorama360(fichier: File): Promise<boolean> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(fichier);
    const image = new Image();
    image.onload = () => {
      const ratio = image.naturalWidth / image.naturalHeight;
      URL.revokeObjectURL(url);
      resolve(ratio > 1.9 && ratio < 2.1);
    };
    image.onerror = () => {
      URL.revokeObjectURL(url);
      resolve(false);
    };
    image.src = url;
  });
}

const texte = (n: number | null | undefined) => (n == null ? "" : String(n));

/**
 * Formulaire de publication ou de modification d'un appartement.
 * - `appartement` fourni : modification de cette annonce (le type d'offre et le bailleur ne changent pas).
 * - `bailleurIdFixe` (espace bailleur) : le logement est publié pour ce bailleur, sans choix de bailleur.
 */
export default function FormulaireAppartement({
  appartement,
  bailleurIdFixe,
  onEnregistre,
  onAnnuler,
}: {
  appartement?: AppartementDto;
  bailleurIdFixe?: number;
  /** `avertissement` signale des photos qui n'ont pas pu être envoyées (l'annonce, elle, est enregistrée). */
  onEnregistre: (a: AppartementDto, avertissement?: string) => void;
  onAnnuler: () => void;
}) {
  const modification = appartement !== undefined;
  // Choix du bailleur (admin) : recherche côté serveur (nom, email, téléphone), 50 résultats au plus.
  const [rechercheBailleur, setRechercheBailleur] = useState("");
  const rechercheEnvoyee = useValeurDifferee(rechercheBailleur.trim());
  const { donnees: utilisateurs } = useChargement<PagedResultDto<UtilisateurDto>>(
    bailleurIdFixe || modification
      ? null
      : `utilisateur/lister?role=Bailleur&statut=Valide&taille=50${rechercheEnvoyee ? `&q=${encodeURIComponent(rechercheEnvoyee)}` : ""}`,
  );
  const [bailleurId, setBailleurId] = useState(bailleurIdFixe ? String(bailleurIdFixe) : "");
  // Le bailleur choisi reste proposé même s'il ne correspond plus à la recherche.
  const [bailleurChoisi, setBailleurChoisi] = useState<UtilisateurDto | null>(null);
  const trouves = utilisateurs?.donnees.filter((u) => u.role === "Bailleur" && u.statut === "Valide") ?? [];
  const bailleurs =
    bailleurChoisi && !trouves.some((u) => u.id === bailleurChoisi.id) ? [bailleurChoisi, ...trouves] : trouves;
  const [typeOffre, setTypeOffre] = useState<TypeOffre>((appartement?.typeOffre as TypeOffre) ?? "LongueDuree");
  const [adresse, setAdresse] = useState(appartement?.adresse ?? "");
  const [description, setDescription] = useState(appartement?.description ?? "");
  const [loyerMensuel, setLoyerMensuel] = useState(texte(appartement?.loyerMensuel));
  const [dureeAvanceExigee, setDureeAvanceExigee] = useState(texte(appartement?.dureeAvanceExigee));
  const [prixParNuit, setPrixParNuit] = useState(texte(appartement?.prixParNuit));
  const [dureeMaxMois, setDureeMaxMois] = useState(texte(appartement?.dureeMaxMois));
  const [disponible, setDisponible] = useState(appartement?.disponible ?? true);
  const [ville, setVille] = useState(appartement?.ville ?? "");
  const [quartier, setQuartier] = useState(appartement?.quartier ?? "");
  const [capacite, setCapacite] = useState(texte(appartement?.capacite));
  const [chambres, setChambres] = useState(texte(appartement?.chambres));
  const [lits, setLits] = useState(texte(appartement?.lits));
  const [sallesDeBain, setSallesDeBain] = useState(texte(appartement?.sallesDeBain));
  const [equipements, setEquipements] = useState<string[]>(appartement?.equipements ?? []);
  const [fraisMenage, setFraisMenage] = useState(texte(appartement?.fraisMenage));
  const [caution, setCaution] = useState(texte(appartement?.caution));
  const [reductionSemainePct, setReductionSemainePct] = useState(texte(appartement?.reductionSemainePct));
  const [reductionMoisPct, setReductionMoisPct] = useState(texte(appartement?.reductionMoisPct));
  const [nuitsMin, setNuitsMin] = useState(texte(appartement?.nuitsMin));
  const [reservationInstantanee, setReservationInstantanee] = useState(appartement?.reservationInstantanee ?? false);
  const [politique, setPolitique] = useState<PolitiqueAnnulation>(politiqueDe(appartement?.politiqueAnnulation));
  const [heureArrivee, setHeureArrivee] = useState(appartement?.heureArrivee || "14:00");
  const [heureDepart, setHeureDepart] = useState(appartement?.heureDepart || "11:00");
  const [instructionsArrivee, setInstructionsArrivee] = useState(appartement?.instructionsArrivee ?? "");
  const [position, setPosition] = useState<Position | null>(positionDe(appartement?.latitude, appartement?.longitude));
  // Suggestions de villes et quartiers déjà utilisés (saisie libre possible).
  const { donnees: villes } = useChargement<VilleDto[]>("appartement/villes");
  const quartiersConnus = villes?.find((v) => v.ville.toLowerCase() === ville.trim().toLowerCase())?.quartiers ?? [];
  const basculerEquipement = (cle: string) =>
    setEquipements((liste) => (liste.includes(cle) ? liste.filter((e) => e !== cle) : [...liste, cle]));
  const [photosConservees, setPhotosConservees] = useState<PhotoDto[]>(
    appartement?.photos?.filter((p) => !p.est360) ?? [],
  );
  const [nouvellesPhotos, setNouvellesPhotos] = useState<NouvellePhoto[]>([]);
  // Photos déjà en ligne : chargées avec le jeton si l'annonce ou son hôte est désactivé.
  const sourcePhoto = useSourcesPhotos(appartement?.photos, appartement?.photosProtegees, "miniature");
  const [panoramasConserves, setPanoramasConserves] = useState<PhotoDto[]>(
    appartement?.photos?.filter((p) => p.est360) ?? [],
  );
  const [nouveauxPanoramas, setNouveauxPanoramas] = useState<NouvellePhoto[]>([]);
  const [erreur, setErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);

  // Libère les aperçus locaux quand le formulaire se ferme.
  const apercus = useRef<string[]>([]);
  useEffect(() => {
    apercus.current = [...nouvellesPhotos, ...nouveauxPanoramas].map((p) => p.apercu);
  }, [nouvellesPhotos, nouveauxPanoramas]);
  useEffect(() => () => apercus.current.forEach((url) => URL.revokeObjectURL(url)), []);

  const nombrePhotos = photosConservees.length + nouvellesPhotos.length;

  const choisirPhotos = (fichiers: FileList | null) => {
    setErreur(null);
    const ajouts: NouvellePhoto[] = [];
    const refus: string[] = [];
    for (const fichier of Array.from(fichiers ?? [])) {
      const extension = fichier.name.slice(fichier.name.lastIndexOf(".")).toLowerCase();
      if (!EXTENSIONS_PHOTO.includes(extension)) refus.push(`${fichier.name} (format non accepté)`);
      else if (fichier.size > TAILLE_MAX_PHOTO) refus.push(`${fichier.name} (plus de 5 Mo)`);
      else if (nombrePhotos + ajouts.length >= PHOTOS_MAX) refus.push(`${fichier.name} (${PHOTOS_MAX} photos maximum)`);
      else ajouts.push({ cle: crypto.randomUUID(), fichier, apercu: URL.createObjectURL(fichier) });
    }
    setNouvellesPhotos((liste) => [...liste, ...ajouts]);
    if (refus.length) setErreur(`Photos ignorées : ${refus.join(", ")}.`);
  };

  const nombrePanoramas = panoramasConserves.length + nouveauxPanoramas.length;

  const choisirPanoramas = async (fichiers: FileList | null) => {
    setErreur(null);
    const ajouts: NouvellePhoto[] = [];
    const refus: string[] = [];
    for (const fichier of Array.from(fichiers ?? [])) {
      const extension = fichier.name.slice(fichier.name.lastIndexOf(".")).toLowerCase();
      if (!EXTENSIONS_PHOTO.includes(extension)) refus.push(`${fichier.name} (format non accepté)`);
      else if (fichier.size > TAILLE_MAX_PANORAMA) refus.push(`${fichier.name} (plus de 15 Mo)`);
      else if (nombrePanoramas + ajouts.length >= PANORAMAS_MAX)
        refus.push(`${fichier.name} (${PANORAMAS_MAX} maximum)`);
      else if (!(await estPanorama360(fichier)))
        refus.push(`${fichier.name} (pas une image 360°, qui doit être 2 fois plus large que haute)`);
      else ajouts.push({ cle: crypto.randomUUID(), fichier, apercu: URL.createObjectURL(fichier), libelle: "" });
    }
    setNouveauxPanoramas((liste) => [...liste, ...ajouts]);
    if (refus.length) setErreur(`Panoramas ignorés : ${refus.join(", ")}.`);
  };

  const retirerNouveauPanorama = (cle: string) =>
    setNouveauxPanoramas((liste) => {
      const panorama = liste.find((p) => p.cle === cle);
      if (panorama) URL.revokeObjectURL(panorama.apercu);
      return liste.filter((p) => p.cle !== cle);
    });

  const retirerNouvelle = (cle: string) =>
    setNouvellesPhotos((liste) => {
      const photo = liste.find((p) => p.cle === cle);
      if (photo) URL.revokeObjectURL(photo.apercu);
      return liste.filter((p) => p.cle !== cle);
    });

  /** Après l'enregistrement de l'annonce : supprime les photos retirées puis envoie les nouvelles. */
  const synchroniserPhotos = async (enregistre: AppartementDto) => {
    const echecs: string[] = [];
    const idsConserves = new Set([...photosConservees, ...panoramasConserves].map((p) => p.id));
    for (const photo of appartement?.photos ?? []) {
      if (idsConserves.has(photo.id)) continue;
      try {
        await apiClient.delete(`appartement/photos/${photo.id}`);
      } catch {
        idsConserves.add(photo.id);
        echecs.push("une photo n'a pas pu être retirée");
      }
    }
    const envoyer = async (liste: NouvellePhoto[], est360: boolean) => {
      const envoyees: PhotoDto[] = [];
      for (const { fichier, libelle } of liste) {
        const formulaire = new FormData();
        formulaire.append("fichier", fichier);
        formulaire.append("est360", String(est360));
        if (libelle?.trim()) formulaire.append("libelle", libelle.trim());
        try {
          envoyees.push(await apiClient.postFormulaire<PhotoDto>(`appartement/${enregistre.id}/photos`, formulaire));
        } catch (err) {
          echecs.push(`${fichier.name}${err instanceof ApiError ? ` (${err.message})` : ""}`);
        }
      }
      return envoyees;
    };
    const photosEnvoyees = await envoyer(nouvellesPhotos, false);
    const panoramasEnvoyes = await envoyer(nouveauxPanoramas, true);
    const conservees = (enregistre.photos ?? []).filter((p) => idsConserves.has(p.id));
    const photos = [
      ...conservees.filter((p) => !p.est360),
      ...photosEnvoyees,
      ...conservees.filter((p) => p.est360),
      ...panoramasEnvoyes,
    ];
    return { photos, avertissement: echecs.length ? `Problème avec les photos : ${echecs.join(", ")}.` : undefined };
  };

  const nombre = (v: string) => (v.trim() === "" ? null : Number(v));

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    if ((!modification && !bailleurId) || !adresse.trim()) {
      setErreur(
        bailleurIdFixe ? "Renseignez l'adresse du logement." : "Choisissez un bailleur et renseignez l'adresse.",
      );
      return;
    }

    const sejourCourt = typeOffre === "SejourCourt";
    const champs = {
      adresse: adresse.trim(),
      description: description.trim() || null,
      ville: ville.trim() || null,
      quartier: quartier.trim() || null,
      capacite: nombre(capacite),
      chambres: nombre(chambres),
      lits: nombre(lits),
      sallesDeBain: nombre(sallesDeBain),
      equipements,
      fraisMenage: sejourCourt ? nombre(fraisMenage) : null,
      caution: sejourCourt ? nombre(caution) : null,
      reductionSemainePct: sejourCourt ? nombre(reductionSemainePct) : null,
      reductionMoisPct: sejourCourt ? nombre(reductionMoisPct) : null,
      nuitsMin: sejourCourt ? nombre(nuitsMin) : null,
      reservationInstantanee: sejourCourt && reservationInstantanee,
      politiqueAnnulation: sejourCourt ? politique : undefined,
      heureArrivee: sejourCourt ? heureArrivee || null : null,
      heureDepart: sejourCourt ? heureDepart || null : null,
      instructionsArrivee: sejourCourt ? instructionsArrivee.trim() || null : null,
      latitude: position?.lat ?? null,
      longitude: position?.lng ?? null,
      loyerMensuel: typeOffre === "LongueDuree" ? nombre(loyerMensuel) : null,
      dureeAvanceExigee: typeOffre === "LongueDuree" ? nombre(dureeAvanceExigee) : null,
      prixParNuit: typeOffre === "SejourCourt" ? nombre(prixParNuit) : null,
      dureeMaxMois: typeOffre === "SejourCourt" ? nombre(dureeMaxMois) : null,
    };

    setEnCours(true);
    try {
      const enregistre = modification
        ? await apiClient.put<AppartementDto>(`appartement/${appartement.id}`, { ...champs, disponible })
        : await apiClient.post<AppartementDto>("appartement/ajouter", {
            ...champs,
            bailleurId: Number(bailleurId),
            typeOffre,
          });
      const { photos, avertissement } = await synchroniserPhotos(enregistre);
      onEnregistre({ ...enregistre, photos }, avertissement);
    } catch (err) {
      setErreur(
        messageErreur(
          err,
          modification ? "Impossible d'enregistrer les modifications." : "Impossible d'ajouter l'appartement.",
        ),
      );
    } finally {
      setEnCours(false);
    }
  };

  return (
    <form onSubmit={envoyer} className="bg-white rounded-2xl border border-black/5 shadow-sm p-6 mb-6">
      <div className="flex items-center justify-between mb-4">
        <h2 className="font-extrabold text-lg">{modification ? "Modifier l'annonce" : "Nouvel appartement"}</h2>
        <button type="button" onClick={onAnnuler} className="text-gray-400 hover:text-gray-700" aria-label="Fermer">
          <X size={20} />
        </button>
      </div>

      <div className="grid sm:grid-cols-2 gap-4">
        {!bailleurIdFixe && !modification && (
          <label className="text-sm font-medium text-gray-700">
            Bailleur
            <input
              type="search"
              value={rechercheBailleur}
              onChange={(e) => setRechercheBailleur(e.target.value)}
              placeholder="Rechercher : nom, email ou téléphone"
              aria-label="Rechercher un bailleur"
              className={classeChamp}
            />
            <select
              value={bailleurId}
              onChange={(e) => {
                setBailleurId(e.target.value);
                setBailleurChoisi(bailleurs.find((b) => String(b.id) === e.target.value) ?? null);
              }}
              className={classeChamp}
              aria-label="Bailleur"
            >
              <option value="">
                {utilisateurs && trouves.length === 0 ? "-- Aucun bailleur trouvé --" : "-- Choisir un bailleur --"}
              </option>
              {bailleurs.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.nom} ({b.email})
                </option>
              ))}
            </select>
          </label>
        )}
        <label className="text-sm font-medium text-gray-700">
          Type d'offre
          <select
            value={typeOffre}
            onChange={(e) => setTypeOffre(e.target.value as TypeOffre)}
            disabled={modification}
            className={`${classeChamp} disabled:bg-gray-50 disabled:text-gray-500`}
          >
            <option value="LongueDuree">Location longue durée</option>
            <option value="SejourCourt">Séjour court</option>
          </select>
          {modification && (
            <span className="block text-xs text-gray-400 font-normal mt-1">
              Le type d'offre ne peut pas être changé.
            </span>
          )}
        </label>
        <label className="text-sm font-medium text-gray-700 sm:col-span-2">
          Adresse
          <input value={adresse} onChange={(e) => setAdresse(e.target.value)} className={classeChamp} />
        </label>
        <label className="text-sm font-medium text-gray-700 sm:col-span-2">
          Description
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={2}
            className={classeChamp}
          />
        </label>
        <label className="text-sm font-medium text-gray-700">
          Ville
          <input
            value={ville}
            onChange={(e) => setVille(e.target.value)}
            list="villes-connues"
            placeholder="Ex. Conakry"
            maxLength={100}
            className={classeChamp}
          />
          <datalist id="villes-connues">
            {villes?.map((v) => (
              <option key={v.ville} value={v.ville} />
            ))}
          </datalist>
        </label>
        <label className="text-sm font-medium text-gray-700">
          Quartier
          <input
            value={quartier}
            onChange={(e) => setQuartier(e.target.value)}
            list="quartiers-connus"
            placeholder="Ex. Kipé"
            maxLength={100}
            className={classeChamp}
          />
          <datalist id="quartiers-connus">
            {quartiersConnus.map((q) => (
              <option key={q} value={q} />
            ))}
          </datalist>
        </label>
        <div className="sm:col-span-2">
          <p className="text-sm font-medium text-gray-700 flex items-center gap-1.5">
            <MapPin size={15} className="text-immo-gold" />
            Position sur la carte <span className="text-gray-400 font-normal">(facultatif)</span>
          </p>
          <p className="text-xs text-gray-400 mb-2">
            Le public ne voit qu'une zone approximative (≈ 100 m)
            {typeOffre === "SejourCourt" &&
              " ; la position exacte n'est donnée qu'aux voyageurs dont la réservation est confirmée"}
            .
          </p>
          <Suspense fallback={<div className="h-72 rounded-xl bg-gray-100 animate-pulse" />}>
            <SelecteurPosition valeur={position} onChange={setPosition} />
          </Suspense>
        </div>
        <div className="sm:col-span-2 grid grid-cols-2 sm:grid-cols-4 gap-4">
          {[
            { libelle: "Voyageurs max", valeur: capacite, changer: setCapacite, min: 1 },
            { libelle: "Chambres", valeur: chambres, changer: setChambres, min: 0 },
            { libelle: "Lits", valeur: lits, changer: setLits, min: 0 },
            { libelle: "Salles de bain", valeur: sallesDeBain, changer: setSallesDeBain, min: 0 },
          ].map((c) => (
            <label key={c.libelle} className="text-sm font-medium text-gray-700">
              {c.libelle}
              <input
                type="number"
                min={c.min}
                max={50}
                value={c.valeur}
                onChange={(e) => c.changer(e.target.value)}
                className={classeChamp}
              />
            </label>
          ))}
        </div>
        <fieldset className="sm:col-span-2">
          <legend className="text-sm font-medium text-gray-700 mb-2">Équipements</legend>
          <div className="grid grid-cols-1 min-[420px]:grid-cols-2 sm:grid-cols-3 gap-2">
            {EQUIPEMENTS.map(({ cle, libelle, icone: Icone }) => (
              <label
                key={cle}
                className={`flex items-center gap-2 text-sm rounded-lg border px-3 py-2 cursor-pointer transition-colors ${
                  equipements.includes(cle)
                    ? "border-immo-gold bg-immo-gold/10 text-immo-dark"
                    : "border-gray-200 text-gray-600 hover:border-gray-300"
                }`}
              >
                <input
                  type="checkbox"
                  checked={equipements.includes(cle)}
                  onChange={() => basculerEquipement(cle)}
                  className="accent-immo-gold w-4 h-4"
                />
                <Icone size={15} className="shrink-0" />
                {libelle}
              </label>
            ))}
          </div>
        </fieldset>

        {typeOffre === "LongueDuree" ? (
          <>
            <label className="text-sm font-medium text-gray-700">
              Loyer mensuel (GNF)
              <input
                type="number"
                min="1"
                value={loyerMensuel}
                onChange={(e) => setLoyerMensuel(e.target.value)}
                className={classeChamp}
              />
            </label>
            <label className="text-sm font-medium text-gray-700">
              Avance exigée (mois)
              <input
                type="number"
                min="1"
                value={dureeAvanceExigee}
                onChange={(e) => setDureeAvanceExigee(e.target.value)}
                className={classeChamp}
              />
            </label>
          </>
        ) : (
          <>
            <label className="text-sm font-medium text-gray-700">
              Prix par nuit (GNF)
              <input
                type="number"
                min="1"
                value={prixParNuit}
                onChange={(e) => setPrixParNuit(e.target.value)}
                className={classeChamp}
              />
            </label>
            <label className="text-sm font-medium text-gray-700">
              Durée max (mois)
              <input
                type="number"
                min="1"
                value={dureeMaxMois}
                onChange={(e) => setDureeMaxMois(e.target.value)}
                placeholder="1"
                className={classeChamp}
              />
            </label>
            <label className="text-sm font-medium text-gray-700">
              Frais de ménage (GNF, par séjour)
              <input
                type="number"
                min="0"
                value={fraisMenage}
                onChange={(e) => setFraisMenage(e.target.value)}
                className={classeChamp}
              />
            </label>
            <label className="text-sm font-medium text-gray-700">
              Caution remboursable (GNF)
              <input
                type="number"
                min="0"
                value={caution}
                onChange={(e) => setCaution(e.target.value)}
                className={classeChamp}
              />
              <span className="block text-xs text-gray-400 font-normal mt-1">
                Affichée à part, non incluse dans le total.
              </span>
            </label>
            <label className="text-sm font-medium text-gray-700">
              Réduction semaine (%, dès 7 nuits)
              <input
                type="number"
                min="0"
                max="50"
                value={reductionSemainePct}
                onChange={(e) => setReductionSemainePct(e.target.value)}
                placeholder="0"
                className={classeChamp}
              />
            </label>
            <label className="text-sm font-medium text-gray-700">
              Réduction mois (%, dès 28 nuits)
              <input
                type="number"
                min="0"
                max="70"
                value={reductionMoisPct}
                onChange={(e) => setReductionMoisPct(e.target.value)}
                placeholder="0"
                className={classeChamp}
              />
              <span className="block text-xs text-gray-400 font-normal mt-1">Remplace la réduction semaine.</span>
            </label>
            <label className="text-sm font-medium text-gray-700">
              Nuits minimum
              <input
                type="number"
                min="1"
                value={nuitsMin}
                onChange={(e) => setNuitsMin(e.target.value)}
                placeholder="1"
                className={classeChamp}
              />
            </label>
            <label className="flex items-start gap-2 text-sm font-medium text-gray-700 cursor-pointer sm:mt-6">
              <input
                type="checkbox"
                checked={reservationInstantanee}
                onChange={(e) => setReservationInstantanee(e.target.checked)}
                className="accent-immo-gold w-4 h-4 mt-0.5"
              />
              <span>
                Réservation instantanée
                <span className="block text-xs text-gray-400 font-normal">
                  Sinon, vous acceptez ou refusez chaque demande sous 48 h.
                </span>
              </span>
            </label>
            <label className="text-sm font-medium text-gray-700">
              Arrivée à partir de
              <input
                type="time"
                value={heureArrivee}
                onChange={(e) => setHeureArrivee(e.target.value)}
                className={classeChamp}
              />
            </label>
            <label className="text-sm font-medium text-gray-700">
              Départ avant
              <input
                type="time"
                value={heureDepart}
                onChange={(e) => setHeureDepart(e.target.value)}
                className={classeChamp}
              />
            </label>
            <label className="text-sm font-medium text-gray-700 sm:col-span-2">
              Instructions d'arrivée
              <textarea
                value={instructionsArrivee}
                onChange={(e) => setInstructionsArrivee(e.target.value)}
                rows={4}
                maxLength={INSTRUCTIONS_MAX}
                placeholder="Accès, remise des clés, code du portail, wifi, contact du gardien…"
                className={classeChamp}
              />
              <span className="flex justify-between gap-2 text-xs text-gray-400 font-normal mt-1">
                <span>Communiquées uniquement au voyageur dont la réservation est confirmée (e-mail la veille).</span>
                <span className="shrink-0">
                  {instructionsArrivee.length}/{INSTRUCTIONS_MAX}
                </span>
              </span>
            </label>
            <fieldset className="sm:col-span-2">
              <legend className="text-sm font-medium text-gray-700">Politique d'annulation</legend>
              <p className="text-xs text-gray-400 mb-2">Remboursement du voyageur s'il annule. {NOTE_POLITIQUE}</p>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-2">
                {(Object.keys(POLITIQUES) as PolitiqueAnnulation[]).map((cle) => (
                  <label
                    key={cle}
                    className={`flex items-start gap-2 rounded-xl border p-3 cursor-pointer transition-colors ${
                      politique === cle ? "border-immo-gold bg-immo-gold/10" : "border-gray-200 hover:bg-gray-50"
                    }`}
                  >
                    <input
                      type="radio"
                      name="politiqueAnnulation"
                      value={cle}
                      checked={politique === cle}
                      onChange={() => setPolitique(cle)}
                      className="accent-immo-gold w-4 h-4 mt-0.5 shrink-0"
                    />
                    <span>
                      <span className="block text-sm font-semibold text-gray-800">{POLITIQUES[cle].libelle}</span>
                      <span className="block text-xs text-gray-500">{POLITIQUES[cle].resume}</span>
                    </span>
                  </label>
                ))}
              </div>
            </fieldset>
          </>
        )}

        {modification && (
          <label className="sm:col-span-2 flex items-start gap-2 text-sm font-medium text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={disponible}
              onChange={(e) => setDisponible(e.target.checked)}
              className="accent-immo-gold w-4 h-4 mt-0.5"
            />
            <span>
              Annonce disponible
              <span className="block text-xs text-gray-400 font-normal">
                Décochez pour bloquer les nouvelles candidatures et réservations : l'annonce reste visible mais marquée
                indisponible.
              </span>
            </span>
          </label>
        )}

        <div className="sm:col-span-2">
          <p className="text-sm font-medium text-gray-700">
            Photos{" "}
            <span className="text-gray-400 font-normal">
              ({nombrePhotos}/{PHOTOS_MAX})
            </span>
          </p>
          <p className="text-xs text-gray-400 mb-2">
            JPG, PNG ou WEBP, 5 Mo maximum. La première photo sert de couverture.
          </p>
          <div className="grid grid-cols-3 sm:grid-cols-5 gap-3">
            {[
              ...photosConservees.map((p) => ({
                cle: `p${p.id}`,
                src: sourcePhoto(p),
                retirer: () => setPhotosConservees((l) => l.filter((x) => x.id !== p.id)),
              })),
              ...nouvellesPhotos.map((p) => ({ cle: p.cle, src: p.apercu, retirer: () => retirerNouvelle(p.cle) })),
            ].map((photo, index) => (
              <div key={photo.cle} className="relative aspect-4/3 rounded-lg overflow-hidden bg-gray-100 group">
                <img src={photo.src} alt="" loading="lazy" decoding="async" className="w-full h-full object-cover" />
                {index === 0 && (
                  <span className="absolute bottom-1 left-1 inline-flex items-center gap-0.5 bg-immo-dark/80 text-immo-gold text-[10px] font-bold px-1.5 py-0.5 rounded">
                    <Star size={10} fill="currentColor" />
                    Couverture
                  </span>
                )}
                <button
                  type="button"
                  onClick={photo.retirer}
                  className="absolute top-1 right-1 w-6 h-6 rounded-full bg-black/60 text-white flex items-center justify-center hover:bg-red-600"
                  aria-label="Retirer cette photo"
                >
                  <X size={14} />
                </button>
              </div>
            ))}
            {nombrePhotos < PHOTOS_MAX && (
              <label className="aspect-4/3 rounded-lg border-2 border-dashed border-gray-300 hover:border-immo-gold text-gray-400 hover:text-immo-gold flex flex-col items-center justify-center gap-1 text-xs font-semibold cursor-pointer transition-colors">
                <ImagePlus size={22} />
                Ajouter
                <input
                  type="file"
                  accept={EXTENSIONS_PHOTO.join(",")}
                  multiple
                  className="hidden"
                  onChange={(e) => {
                    choisirPhotos(e.target.files);
                    e.target.value = "";
                  }}
                />
              </label>
            )}
          </div>
        </div>

        <div className="sm:col-span-2">
          <p className="text-sm font-medium text-gray-700 flex items-center gap-1.5">
            <Rotate3d size={16} className="text-immo-gold" />
            Visite 360°{" "}
            <span className="text-gray-400 font-normal">
              ({nombrePanoramas}/{PANORAMAS_MAX})
            </span>
          </p>
          <p className="text-xs text-gray-400 mb-2">
            Une photo panoramique 360° par pièce (mode « Photo Sphere » / « 360° » du téléphone, ou caméra 360). Image 2
            fois plus large que haute, 15 Mo maximum.
          </p>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            {panoramasConserves.map((p) => (
              <div key={p.id} className="rounded-lg overflow-hidden border border-black/5">
                <div className="relative aspect-2/1 bg-gray-100">
                  <img
                    src={sourcePhoto(p)}
                    alt=""
                    loading="lazy"
                    decoding="async"
                    className="w-full h-full object-cover"
                  />
                  <button
                    type="button"
                    onClick={() => setPanoramasConserves((l) => l.filter((x) => x.id !== p.id))}
                    className="absolute top-1 right-1 w-6 h-6 rounded-full bg-black/60 text-white flex items-center justify-center hover:bg-red-600"
                    aria-label="Retirer ce panorama"
                  >
                    <X size={14} />
                  </button>
                </div>
                <p className="px-2 py-1.5 text-xs font-semibold text-gray-700">{p.libelle || "Pièce sans nom"}</p>
              </div>
            ))}
            {nouveauxPanoramas.map((p) => (
              <div key={p.cle} className="rounded-lg overflow-hidden border border-immo-gold/40">
                <div className="relative aspect-2/1 bg-gray-100">
                  <img src={p.apercu} alt="" decoding="async" className="w-full h-full object-cover" />
                  <button
                    type="button"
                    onClick={() => retirerNouveauPanorama(p.cle)}
                    className="absolute top-1 right-1 w-6 h-6 rounded-full bg-black/60 text-white flex items-center justify-center hover:bg-red-600"
                    aria-label="Retirer ce panorama"
                  >
                    <X size={14} />
                  </button>
                </div>
                <input
                  value={p.libelle}
                  onChange={(e) =>
                    setNouveauxPanoramas((liste) =>
                      liste.map((x) => (x.cle === p.cle ? { ...x, libelle: e.target.value } : x)),
                    )
                  }
                  placeholder="Nom de la pièce (ex. Salon)"
                  maxLength={50}
                  aria-label="Nom de la pièce"
                  className="w-full px-2 py-1.5 text-xs border-t border-black/5 focus:outline-none focus:bg-immo-gold/5"
                />
              </div>
            ))}
            {nombrePanoramas < PANORAMAS_MAX && (
              <label className="aspect-2/1 rounded-lg border-2 border-dashed border-gray-300 hover:border-immo-gold text-gray-400 hover:text-immo-gold flex flex-col items-center justify-center gap-1 text-xs font-semibold cursor-pointer transition-colors">
                <Rotate3d size={22} />
                Ajouter un panorama 360°
                <input
                  type="file"
                  accept={EXTENSIONS_PHOTO.join(",")}
                  multiple
                  className="hidden"
                  onChange={(e) => {
                    choisirPanoramas(e.target.files);
                    e.target.value = "";
                  }}
                />
              </label>
            )}
          </div>
        </div>
      </div>

      {erreur && (
        <div className="mt-4">
          <MessageErreur>{erreur}</MessageErreur>
        </div>
      )}

      <div className="flex gap-2 mt-6">
        <button type="submit" disabled={enCours} className={classeBoutonPrincipal()}>
          {enCours ? "Enregistrement..." : modification ? "Enregistrer les modifications" : "Ajouter l'appartement"}
        </button>
        <button type="button" onClick={onAnnuler} className={classeBoutonSecondaire()}>
          Annuler
        </button>
      </div>
    </form>
  );
}
