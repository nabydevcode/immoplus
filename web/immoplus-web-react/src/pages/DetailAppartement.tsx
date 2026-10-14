import { lazy, Suspense, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { AlertCircle, ArrowLeft, Clock, MapPin, Rotate3d } from "lucide-react";
import { useAuth } from "../context/auth";
import Galerie from "../components/appartements/Galerie";
import EtapesLocation from "../components/candidatures/EtapesLocation";
import ReservationSejour from "../components/sejour/ReservationSejour";
import { BadgeInstantanee, Capacite, Equipements } from "../components/sejour/CaracteristiquesLogement";
import { descriptionAnnonce, localisation } from "../components/sejour/equipements";
import { selectionDepuisUrl } from "../components/sejour/parametres";
import { MENTION_ADRESSE_MASQUEE } from "../components/sejour/reservations";
import { ResumeNote, SectionAvis } from "../components/avis/AffichageAvis";
import { BadgeSuperhote, BlocHote, SectionPolitique } from "../components/sejour/Confiance";
import { politiqueDe } from "../components/sejour/politiques";
import BoutonFavori from "../components/favoris/BoutonFavori";
import Partage from "../components/favoris/Partage";
import { positionDe } from "../components/carte/position";
import Signaler from "../components/signalement/Signaler";
import { useTitrePage } from "../hooks/useTitrePage";
import { useAppartement } from "../hooks/useAppartement";
import { useLienConnexion } from "../hooks/useLienConnexion";

// Le lecteur 360° (three.js) n'est téléchargé qu'à l'ouverture d'une visite.
const Visite360 = lazy(() => import("../components/appartements/Visite360"));
// Leaflet n'est téléchargé que pour une annonce géolocalisée.
const CarteLogement = lazy(() => import("../components/carte/CarteLogement"));

export default function DetailAppartement() {
  const { type = "longue-duree", id } = useParams<{ type: string; id: string }>();
  const { estConnecte, roleUtilisateur, utilisateurId } = useAuth();
  const lienConnexion = useLienConnexion();
  const [parametres] = useSearchParams();

  const { appartement, chargement, erreur } = useAppartement(id);
  const [visiteOuverte, setVisiteOuverte] = useState(false);

  const estLongueDuree = type === "longue-duree";
  useTitrePage(
    appartement ? appartement.description || appartement.adresse : null,
    appartement ? descriptionAnnonce(appartement) : null,
  );

  if (chargement) {
    return (
      <div className="px-6 py-10 max-w-6xl mx-auto">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-2 mb-8 animate-pulse">
          <div className="md:col-span-2 bg-gray-200 h-64 md:h-96 rounded-2xl" />
          <div className="bg-gray-200 h-32 md:h-[11.5rem] rounded-2xl" />
          <div className="bg-gray-200 h-32 md:h-[11.5rem] rounded-2xl" />
          <div className="bg-gray-200 h-32 md:h-[11.5rem] rounded-2xl md:col-start-3" />
          <div className="bg-gray-200 h-32 md:h-[11.5rem] rounded-2xl" />
        </div>
      </div>
    );
  }

  if (erreur || !appartement) {
    return (
      <div className="px-6 py-16 max-w-xl mx-auto text-center">
        <AlertCircle className="text-red-500 mx-auto mb-3" size={32} />
        <p className="text-red-600 font-medium mb-4">{erreur ?? "Annonce introuvable."}</p>
        <Link to={`/annonces/${type}`} className="inline-flex items-center gap-2 text-immo-dark font-semibold text-sm">
          <ArrowLeft size={16} />
          Retour aux annonces
        </Link>
      </div>
    );
  }

  const photos = appartement.photos?.filter((p) => !p.est360) ?? [];
  const panoramas = appartement.photos?.filter((p) => p.est360) ?? [];

  const lieu = localisation(appartement);
  const position = positionDe(appartement.latitude, appartement.longitude);
  // Coordonnées exactes pour le propriétaire et l'admin ; arrondies (~100 m) pour le public.
  const positionExacte =
    roleUtilisateur === "Admin" || (roleUtilisateur === "Bailleur" && utilisateurId === appartement.bailleurId);
  const badge = "bg-immo-dark/5 text-xs font-semibold px-3 py-1.5 rounded-full";
  const estProprietaire = roleUtilisateur === "Bailleur" && utilisateurId === appartement.bailleurId;
  const avisEtHote = (
    <>
      <SectionAvis appartementId={appartement.id} estProprietaire={estProprietaire} longueDuree={estLongueDuree} />
      <hr className="border-black/10" />
      <BlocHote appartement={appartement} signalable={!estProprietaire} />
    </>
  );

  const presentation = (
    <div className="flex flex-col gap-6">
      <div>
        <div className="flex justify-between items-start gap-4 mb-1">
          <h1 className="text-2xl font-extrabold min-w-0 wrap-break-word">
            {appartement.description || appartement.adresse}
          </h1>
          <div className="text-right shrink-0">
            <p className="text-immo-gold text-2xl font-extrabold">
              {(estLongueDuree ? appartement.loyerMensuel : appartement.prixParNuit)?.toLocaleString("fr-FR") ?? "-"}
            </p>
            <p className="text-gray-500 text-sm">{estLongueDuree ? "GNF / mois" : "GNF / nuit"}</p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 mb-2">
          <ResumeNote
            note={appartement.noteMoyenne}
            nombre={appartement.nombreAvis}
            lien={appartement.nombreAvis ? "#avis" : undefined}
          />
          {appartement.hote?.superhote && <BadgeSuperhote petit />}
        </div>
        <p className="text-gray-500 flex items-start gap-1.5 mb-4">
          <MapPin size={15} className="shrink-0 mt-1" />
          {appartement.adresseMasquee ? (
            <span>
              <strong className="text-gray-700 font-semibold">{appartement.adresse}</strong>
              <span className="block text-xs text-gray-400">{MENTION_ADRESSE_MASQUEE}</span>
            </span>
          ) : (
            <span>
              {lieu && <strong className="text-gray-700 font-semibold">{lieu} · </strong>}
              {appartement.adresse}
            </span>
          )}
        </p>

        <div className="flex flex-wrap items-start gap-2 mb-5">
          <BoutonFavori appartementId={appartement.id} />
          <Partage titre={appartement.description || appartement.adresse} />
          {!estProprietaire && <Signaler cible="Appartement" cibleId={appartement.id} className="self-center" />}
        </div>

        <div className="flex flex-wrap gap-2 mb-5">
          {appartement.disponible && !appartement.estDesactive ? (
            <span className="bg-immo-sage-light text-immo-sage text-xs font-semibold px-3 py-1.5 rounded-full">
              Disponible
            </span>
          ) : (
            <span className="bg-red-100 text-red-700 text-xs font-semibold px-3 py-1.5 rounded-full">
              Non disponible
            </span>
          )}
          {estLongueDuree ? (
            <span className={badge}>Avance : {appartement.dureeAvanceExigee} mois</span>
          ) : (
            <>
              {appartement.reservationInstantanee && <BadgeInstantanee />}
              {(appartement.nuitsMin ?? 1) > 1 && <span className={badge}>{appartement.nuitsMin} nuits minimum</span>}
              {appartement.dureeMaxMois != null && (
                <span className={badge}>Séjour max : {appartement.dureeMaxMois} mois</span>
              )}
            </>
          )}
        </div>
        <Capacite appartement={appartement} />
      </div>

      {appartement.description && (
        <>
          <hr className="border-black/10" />
          <p className="text-gray-700 leading-relaxed whitespace-pre-line">{appartement.description}</p>
        </>
      )}

      {(appartement.equipements?.length ?? 0) > 0 && (
        <section>
          <h2 className="font-extrabold text-lg mb-3">Équipements</h2>
          <Equipements cles={appartement.equipements} />
        </section>
      )}

      {!estLongueDuree && (
        <p className="text-sm text-gray-700 flex flex-wrap items-center gap-x-4 gap-y-1">
          <span className="inline-flex items-center gap-1.5">
            <Clock size={15} className="text-immo-gold" />
            Arrivée à partir de <strong>{appartement.heureArrivee || "14:00"}</strong>
          </span>
          <span>
            Départ avant <strong>{appartement.heureDepart || "11:00"}</strong>
          </span>
        </p>
      )}

      {position && (
        <section>
          <h2 className="font-extrabold text-lg mb-1">Emplacement</h2>
          <p className="text-sm text-gray-500 mb-3">
            {positionExacte
              ? "Position exacte (visible par vous seul)."
              : estLongueDuree
                ? "Position approximative du logement."
                : "Position approximative : l'adresse exacte vous est communiquée une fois la réservation confirmée."}
          </p>
          <Suspense fallback={<div className="h-64 rounded-2xl bg-gray-200 animate-pulse" />}>
            <CarteLogement position={position} approximatif={!positionExacte} />
          </Suspense>
        </section>
      )}

      {!estLongueDuree && (!!appartement.caution || !!appartement.fraisMenage) && (
        <p className="text-sm text-gray-500">
          {appartement.fraisMenage
            ? `Frais de ménage : ${appartement.fraisMenage.toLocaleString("fr-FR")} GNF par séjour. `
            : ""}
          {appartement.caution
            ? `Caution remboursable : ${appartement.caution.toLocaleString("fr-FR")} GNF (non incluse dans le total).`
            : ""}
        </p>
      )}
    </div>
  );

  return (
    <div className="pb-16">
      <div className="px-6 pt-6 max-w-6xl mx-auto">
        <Link
          to={`/annonces/${type}`}
          className="inline-flex items-center gap-2 text-gray-600 text-sm font-medium hover:text-immo-dark transition-colors mb-6"
        >
          <ArrowLeft size={16} />
          Retour aux annonces
        </Link>

        <Galerie
          photos={photos}
          protegees={appartement.photosProtegees}
          action={
            panoramas.length > 0 && (
              <button
                onClick={() => setVisiteOuverte(true)}
                className="inline-flex items-center gap-2 bg-immo-dark/85 hover:bg-immo-dark text-immo-cream font-semibold text-sm px-4 py-2.5 rounded-full shadow-lg backdrop-blur transition-colors"
              >
                <Rotate3d size={18} className="text-immo-gold" />
                Visite 360° · {panoramas.length} pièce{panoramas.length > 1 ? "s" : ""}
              </button>
            )
          }
        />
        {visiteOuverte && (
          <Suspense
            fallback={
              <div className="fixed inset-0 z-[100] bg-black/90 flex items-center justify-center text-immo-cream">
                Chargement de la visite 360°...
              </div>
            }
          >
            <Visite360
              panoramas={panoramas}
              protegees={appartement.photosProtegees}
              titre={appartement.adresse}
              onFermer={() => setVisiteOuverte(false)}
            />
          </Suspense>
        )}
      </div>

      <div className="px-6 max-w-6xl mx-auto">
        {estLongueDuree ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div className="md:col-span-2 min-w-0 flex flex-col gap-8">
              {presentation}
              <hr className="border-black/10" />
              {avisEtHote}
            </div>
            <div className="bg-white rounded-2xl p-6 h-fit shadow-sm border border-black/5">
              <h3 className="font-bold mb-1">Intéressé par ce bien ?</h3>
              <p className="text-gray-500 text-sm mb-5">
                Envoyez votre candidature (situation professionnelle, garant si besoin, documents). Le bailleur
                l'examinera avant validation.
              </p>
              <div className="mb-5">
                <EtapesLocation />
              </div>

              {!appartement.disponible || appartement.estDesactive ? (
                <p className="text-sm text-gray-500">Cet appartement n'est plus disponible.</p>
              ) : !estConnecte ? (
                <Link
                  to={lienConnexion}
                  className="block text-center bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors"
                >
                  Se connecter pour candidater
                </Link>
              ) : roleUtilisateur !== "Locataire" ? (
                <p className="text-sm text-gray-500">Seul un compte Locataire peut candidater.</p>
              ) : (
                <Link
                  to={`/annonces/${type}/${id}/candidater`}
                  className="block text-center bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors"
                >
                  Candidater
                </Link>
              )}
            </div>
          </div>
        ) : (
          <ReservationSejour
            appartement={appartement}
            initial={selectionDepuisUrl(parametres)}
            suite={
              <>
                {avisEtHote}
                <SectionPolitique politique={politiqueDe(appartement.politiqueAnnulation)} />
              </>
            }
          >
            {presentation}
          </ReservationSejour>
        )}
      </div>
    </div>
  );
}
