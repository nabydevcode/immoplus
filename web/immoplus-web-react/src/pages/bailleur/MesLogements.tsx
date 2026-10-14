import { useState } from "react";
import { Link } from "react-router-dom";
import {
  CalendarDays,
  CalendarRange,
  FileText,
  Heart,
  Home,
  MapPin,
  Pencil,
  Plus,
  RotateCcw,
  Trash2,
} from "lucide-react";
import { apiClient } from "../../api/client";
import { useAuth } from "../../context/auth";
import FormulaireAppartement from "../../components/appartements/FormulaireAppartement";
import PhotoCouverture from "../../components/appartements/PhotoCouverture";
import CalendrierLogement from "../../components/sejour/CalendrierLogement";
import { BadgeInstantanee, Capacite } from "../../components/sejour/CaracteristiquesLogement";
import { localisation } from "../../components/sejour/equipements";
import { classeBoutonPrincipal, formaterMontant, messageErreur, useChargement } from "../../components/admin/outils";
import { MessageErreur } from "../../components/admin/ui";
import type { AppartementDto, CandidatureDto, PagedResultDto, ReservationDto } from "../../types/models";
import { useLienConnexion } from "../../hooks/useLienConnexion";

export default function MesLogements() {
  const { estConnecte, roleUtilisateur, utilisateurId } = useAuth();
  const lienConnexion = useLienConnexion();
  const estBailleur = estConnecte && roleUtilisateur === "Bailleur" && utilisateurId !== null;
  const appartements = useChargement<AppartementDto[]>(
    estBailleur ? `appartement/lister?bailleurId=${utilisateurId}` : null,
  );
  const candidatures = useChargement<PagedResultDto<CandidatureDto>>(
    estBailleur ? "candidature/lister?page=1&taille=100" : null,
  );
  const reservations = useChargement<ReservationDto[]>(estBailleur ? "reservation/lister" : null);
  const [calendrier, setCalendrier] = useState<AppartementDto | null>(null);
  // null : formulaire fermé ; "ajout" : nouveau logement ; sinon l'annonce en cours de modification.
  const [formulaire, setFormulaire] = useState<null | "ajout" | AppartementDto>(null);
  const [confirmation, setConfirmation] = useState<string | null>(null);
  const [avertissement, setAvertissement] = useState<string | null>(null);
  const formulaireOuvert = formulaire !== null;
  // Message de refus de l'API (ex. logement avec historique), affiché sur la carte concernée.
  const [erreursSuppression, setErreursSuppression] = useState<Record<number, string>>({});
  const [suppressionEnCours, setSuppressionEnCours] = useState<number | null>(null);

  const supprimer = async (a: AppartementDto) => {
    if (
      !window.confirm(
        `Supprimer ce logement ?\n\n${a.adresse}\n\nIl disparaîtra des annonces, mais son historique et ses photos sont conservés : vous pourrez le réactiver à tout moment.`,
      )
    )
      return;
    setErreursSuppression((e) => {
      const copie = { ...e };
      delete copie[a.id];
      return copie;
    });
    setSuppressionEnCours(a.id);
    try {
      await apiClient.delete(`appartement/${a.id}`);
      appartements.setDonnees((liste) => (liste ?? []).map((x) => (x.id === a.id ? { ...x, estDesactive: true } : x)));
      setAvertissement(null);
      setConfirmation(`« ${a.adresse} » a été supprimé des annonces. Vous pouvez le réactiver depuis sa carte.`);
    } catch (err) {
      setErreursSuppression((e) => ({ ...e, [a.id]: messageErreur(err, "Impossible de supprimer ce logement.") }));
    } finally {
      setSuppressionEnCours(null);
    }
  };

  const reactiver = async (a: AppartementDto) => {
    setSuppressionEnCours(a.id);
    try {
      const misAJour = await apiClient.post<AppartementDto>(`appartement/${a.id}/reactiver`);
      appartements.setDonnees((liste) => (liste ?? []).map((x) => (x.id === a.id ? misAJour : x)));
      setAvertissement(null);
      setConfirmation(`« ${a.adresse} » est de nouveau publié.`);
    } catch (err) {
      setErreursSuppression((e) => ({ ...e, [a.id]: messageErreur(err, "Impossible de réactiver ce logement.") }));
    } finally {
      setSuppressionEnCours(null);
    }
  };

  const ouvrir = (cible: "ajout" | AppartementDto) => {
    setConfirmation(null);
    setAvertissement(null);
    setFormulaire(cible);
    setCalendrier(null);
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  if (!estBailleur) {
    return (
      <div className="px-6 py-16 max-w-xl mx-auto text-center text-gray-600">
        <Link to={lienConnexion} className="text-immo-gold font-semibold">
          Connectez-vous
        </Link>{" "}
        avec un compte Bailleur pour gérer vos logements.
      </div>
    );
  }

  // L'API liste toutes les annonces publiques : on ne garde que celles du bailleur connecté.
  const mesLogements = (appartements.donnees ?? [])
    .filter((a) => a.bailleurId === utilisateurId)
    .sort((x, y) => Number(!!x.estDesactive) - Number(!!y.estDesactive));
  const aEtudier = (appartementId: number) =>
    candidatures.donnees?.donnees.filter((c) => c.appartementId === appartementId && c.statut === "EnAttente").length ??
    0;
  const demandesEnAttente = (appartementId: number) =>
    reservations.donnees?.filter((r) => r.appartementId === appartementId && r.statut === "DemandeEnvoyee").length ?? 0;

  return (
    <div className="px-6 py-10">
      <div className="max-w-6xl mx-auto">
        <div className="flex flex-wrap items-end justify-between gap-4 mb-8">
          <div>
            <h1 className="text-2xl md:text-3xl font-extrabold mb-1">Mes logements</h1>
            <p className="text-gray-500 text-sm">Publiez vos appartements et suivez les demandes reçues.</p>
          </div>
          {!formulaireOuvert && (
            <button onClick={() => ouvrir("ajout")} className={classeBoutonPrincipal()}>
              <Plus size={16} />
              Ajouter un logement
            </button>
          )}
        </div>

        {formulaire && (
          <FormulaireAppartement
            key={formulaire === "ajout" ? "ajout" : formulaire.id}
            appartement={formulaire === "ajout" ? undefined : formulaire}
            bailleurIdFixe={utilisateurId ?? undefined}
            onAnnuler={() => setFormulaire(null)}
            onEnregistre={(a, avertissement) => {
              setAvertissement(avertissement ?? null);
              appartements.setDonnees((liste) =>
                formulaire === "ajout" ? [...(liste ?? []), a] : (liste ?? []).map((x) => (x.id === a.id ? a : x)),
              );
              setConfirmation(
                formulaire === "ajout"
                  ? `« ${a.adresse} » est publié : il est désormais visible dans les annonces.`
                  : `Les modifications de « ${a.adresse} » sont enregistrées.`,
              );
              setFormulaire(null);
            }}
          />
        )}

        {calendrier && (
          <CalendrierLogement key={calendrier.id} appartement={calendrier} onFermer={() => setCalendrier(null)} />
        )}

        {(confirmation || avertissement) && (
          <div className="flex flex-col gap-2 mb-6">
            {confirmation && <p className="bg-green-50 text-green-700 text-sm rounded-lg px-4 py-3">{confirmation}</p>}
            {avertissement && (
              <p className="bg-amber-50 text-amber-800 text-sm rounded-lg px-4 py-3">{avertissement}</p>
            )}
          </div>
        )}

        {appartements.chargement ? (
          <p className="text-gray-500 text-sm">Chargement...</p>
        ) : appartements.erreur ? (
          <p className="text-red-600 text-sm">{appartements.erreur}</p>
        ) : mesLogements.length === 0 ? (
          !formulaireOuvert && (
            <div className="bg-white rounded-2xl shadow-sm border border-black/5 py-14 px-6 text-center">
              <Home size={32} className="mx-auto text-immo-gold mb-3" />
              <p className="text-gray-500 mb-4">Vous n'avez encore publié aucun logement.</p>
              <button onClick={() => ouvrir("ajout")} className={classeBoutonPrincipal()}>
                <Plus size={16} />
                Publier mon premier logement
              </button>
            </div>
          )
        ) : (
          <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-5">
            {mesLogements.map((a) => {
              const longueDuree = a.typeOffre === "LongueDuree";
              const nombreAEtudier = aEtudier(a.id);
              const nombreDemandes = demandesEnAttente(a.id);
              const lieu = localisation(a);
              return (
                <article
                  key={a.id}
                  className={`bg-white rounded-2xl shadow-sm border border-black/5 overflow-hidden flex flex-col ${
                    a.estDesactive ? "opacity-70" : ""
                  }`}
                >
                  <PhotoCouverture
                    photos={a.photos}
                    protegees={a.photosProtegees}
                    className="h-36 w-full"
                    tailleIcone={32}
                  />
                  <div className="p-5 flex flex-col gap-3 flex-1">
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-xs font-semibold bg-immo-dark/5 px-2.5 py-1 rounded-full">
                        {longueDuree ? "Longue durée" : "Séjour court"}
                      </span>
                      <span
                        className={`text-xs font-medium px-2.5 py-1 rounded-full ${
                          a.estDesactive
                            ? "bg-red-100 text-red-700"
                            : a.enLocation
                              ? "bg-blue-100 text-blue-700"
                              : a.disponible
                                ? "bg-green-100 text-green-700"
                                : "bg-gray-100 text-gray-600"
                        }`}
                      >
                        {a.estDesactive
                          ? "Supprimé"
                          : a.enLocation
                            ? "Loué"
                            : a.disponible
                              ? "Disponible"
                              : "Retiré des annonces"}
                      </span>
                    </div>
                    <h2 className="font-bold flex items-start gap-1.5">
                      <MapPin size={16} className="text-immo-gold shrink-0 mt-0.5" />
                      {a.adresse}
                    </h2>
                    {(lieu || (!longueDuree && a.reservationInstantanee) || !!a.nombreFavoris) && (
                      <div className="flex flex-wrap items-center gap-2 -mt-1">
                        {lieu && <span className="text-xs text-gray-500">{lieu}</span>}
                        {!longueDuree && a.reservationInstantanee && <BadgeInstantanee petit />}
                        {!!a.nombreFavoris && (
                          <span
                            className="inline-flex items-center gap-1 text-xs text-gray-500"
                            title="Voyageurs ayant enregistré cette annonce"
                          >
                            <Heart size={12} className="text-red-500" fill="currentColor" />
                            {a.nombreFavoris} favori{a.nombreFavoris > 1 ? "s" : ""}
                          </span>
                        )}
                      </div>
                    )}
                    <Capacite appartement={a} compact />
                    {a.description && <p className="text-sm text-gray-500 line-clamp-2">{a.description}</p>}
                    <p className="text-sm">
                      {longueDuree ? (
                        <>
                          <span className="font-extrabold">{formaterMontant(a.loyerMensuel)}</span> / mois · avance{" "}
                          {a.dureeAvanceExigee} mois
                        </>
                      ) : (
                        <>
                          <span className="font-extrabold">{formaterMontant(a.prixParNuit)}</span> / nuit · max{" "}
                          {a.dureeMaxMois} mois
                        </>
                      )}
                    </p>
                    <div className="mt-auto pt-3 border-t border-black/5 flex items-center justify-between gap-2 text-sm">
                      {longueDuree ? (
                        <Link
                          to="/bailleur/candidatures"
                          className={`inline-flex items-center gap-1.5 font-semibold ${
                            nombreAEtudier > 0 ? "text-amber-700" : "text-gray-500"
                          } hover:text-immo-dark`}
                        >
                          <FileText size={15} />
                          {nombreAEtudier > 0
                            ? `${nombreAEtudier} candidature(s) à étudier`
                            : "Aucune candidature en attente"}
                        </Link>
                      ) : (
                        <Link
                          to="/bailleur/reservations"
                          className={`inline-flex items-center gap-1.5 font-semibold ${
                            nombreDemandes > 0 ? "text-amber-700" : "text-gray-500"
                          } hover:text-immo-dark`}
                        >
                          <CalendarDays size={15} />
                          {nombreDemandes > 0 ? `${nombreDemandes} demande(s) à traiter` : "Réservations"}
                        </Link>
                      )}
                      <div className="flex items-center gap-3 shrink-0">
                        <button
                          onClick={() => ouvrir(a)}
                          disabled={a.enLocation}
                          title={
                            a.enLocation
                              ? "Logement en cours de location : il ne peut pas être modifié tant que la location dure."
                              : undefined
                          }
                          className="inline-flex items-center gap-1 font-semibold text-gray-700 hover:text-immo-dark disabled:text-gray-300 disabled:cursor-not-allowed"
                        >
                          <Pencil size={14} />
                          Modifier
                        </button>
                        <Link
                          to={`/annonces/${longueDuree ? "longue-duree" : "sejour-court"}/${a.id}`}
                          className="text-immo-gold font-semibold"
                        >
                          Voir
                        </Link>
                      </div>
                    </div>
                    {!longueDuree && (
                      <button
                        onClick={() => {
                          setFormulaire(null);
                          setCalendrier(a);
                          window.scrollTo({ top: 0, behavior: "smooth" });
                        }}
                        className="inline-flex items-center justify-center gap-1.5 text-sm font-semibold border border-gray-300 rounded-lg py-2 hover:bg-gray-50"
                      >
                        <CalendarRange size={15} />
                        Calendrier et dates bloquées
                      </button>
                    )}
                    {a.estDesactive ? (
                      <div className="flex flex-col items-end gap-2">
                        <button
                          onClick={() => reactiver(a)}
                          disabled={suppressionEnCours === a.id}
                          className="inline-flex items-center gap-1 text-xs font-semibold text-green-700 hover:text-green-800 disabled:opacity-60"
                        >
                          <RotateCcw size={13} />
                          {suppressionEnCours === a.id ? "Réactivation..." : "Réactiver l'annonce"}
                        </button>
                        {erreursSuppression[a.id] && <MessageErreur>{erreursSuppression[a.id]}</MessageErreur>}
                      </div>
                    ) : (
                      !a.enLocation && (
                        <div className="flex flex-col items-end gap-2">
                          <button
                            onClick={() => supprimer(a)}
                            disabled={suppressionEnCours === a.id}
                            className="inline-flex items-center gap-1 text-xs font-semibold text-red-600 hover:text-red-700 disabled:opacity-60"
                          >
                            <Trash2 size={13} />
                            {suppressionEnCours === a.id ? "Suppression..." : "Supprimer"}
                          </button>
                          {erreursSuppression[a.id] && <MessageErreur>{erreursSuppression[a.id]}</MessageErreur>}
                        </div>
                      )
                    )}
                  </div>
                </article>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
