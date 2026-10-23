import { Fragment, useState } from "react";
import { CalendarRange, Pencil, Plus, RotateCcw, Trash2 } from "lucide-react";
import { useSearchParams } from "react-router-dom";
import { apiClient } from "../../api/client";
import { extraireNumero } from "../../components/candidatures/recherche";
import FormulaireAppartement from "../../components/appartements/FormulaireAppartement";
import PhotoCouverture from "../../components/appartements/PhotoCouverture";
import CalendrierLogement from "../../components/sejour/CalendrierLogement";
import { localisation } from "../../components/sejour/equipements";
import { Badge, EnTete, EtatListe, MessageErreur, Onglets, Pagination, Tableau } from "../../components/admin/ui";
import { useListeAOnglets } from "../../components/admin/pagination";
import {
  classeBoutonDanger,
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  formaterMontant,
  formaterReference,
  messageErreur,
  useChargement,
} from "../../components/admin/outils";
import type { AppartementDto } from "../../types/models";

type TypeOffre = "LongueDuree" | "SejourCourt";

type Filtre = "Tous" | TypeOffre | "Supprimes";
const ONGLETS = ["Tous", "LongueDuree", "SejourCourt", "Supprimes"] as const;
/** Onglet → paramètres de l'API : annonces publiées (supprimes=false) par type, ou supprimées (supprimes=true). */
const requeteOnglet = (f: Filtre) =>
  f === "Supprimes" ? "supprimes=true" : f === "Tous" ? "supprimes=false" : `supprimes=false&typeOffre=${f}`;
const LOCAL = {
  dansOnglet: (f: Filtre, a: AppartementDto) =>
    f === "Supprimes" ? !!a.estDesactive : !a.estDesactive && (f === "Tous" || a.typeOffre === f),
};

export default function Appartements() {
  const [params, setParams] = useSearchParams();
  const [filtre, setFiltre] = useState<Filtre>("Tous");
  const idCible = extraireNumero(params.get("q") ?? "");
  const liste = useListeAOnglets<AppartementDto, Filtre>({
    base: idCible ? null : "appartement/lister",
    onglet: filtre,
    onglets: ONGLETS,
    requeteOnglet,
    mode: "pages",
    local: LOCAL,
  });
  // Lien direct (?q=n° d'annonce, depuis le journal ou un signalement) : seule cette annonce est lue et affichée.
  const cible = useChargement<AppartementDto>(idCible ? `appartement/${idCible}` : null);
  const { compteurs } = liste;
  const chargement = idCible ? cible.chargement : liste.chargement;
  const erreur = idCible ? cible.erreur : liste.erreur;
  /** Mise à jour locale d'une annonce (suppression, réactivation, modification). */
  const setDonnees = (maj: (liste: AppartementDto[]) => AppartementDto[]) => {
    if (idCible) cible.setDonnees((a) => (a ? (maj([a])[0] ?? a) : a));
    else liste.setElements(maj);
  };
  // null : formulaire fermé ; "ajout" : nouvel appartement ; sinon l'annonce en cours de modification.
  const [formulaire, setFormulaire] = useState<null | "ajout" | AppartementDto>(null);
  const [avertissement, setAvertissement] = useState<string | null>(null);
  const [calendrier, setCalendrier] = useState<AppartementDto | null>(null);

  const [erreurSuppression, setErreurSuppression] = useState<{ id: number; message: string } | null>(null);
  const [suppressionEnCours, setSuppressionEnCours] = useState<number | null>(null);

  const supprimer = async (a: AppartementDto) => {
    if (
      !window.confirm(
        `Supprimer ce logement ?\n\n${a.adresse}\n\nIl disparaîtra des annonces, mais son historique et ses photos sont conservés : il pourra être réactivé.`,
      )
    )
      return;
    setErreurSuppression(null);
    setSuppressionEnCours(a.id);
    try {
      await apiClient.delete(`appartement/${a.id}`);
      setDonnees((l) => l.map((x) => (x.id === a.id ? { ...x, estDesactive: true } : x)));
    } catch (err) {
      setErreurSuppression({ id: a.id, message: messageErreur(err, "Impossible de supprimer ce logement.") });
    } finally {
      setSuppressionEnCours(null);
    }
  };

  const reactiver = async (a: AppartementDto) => {
    setErreurSuppression(null);
    setSuppressionEnCours(a.id);
    try {
      const misAJour = await apiClient.post<AppartementDto>(`appartement/${a.id}/reactiver`);
      setDonnees((l) => l.map((x) => (x.id === a.id ? misAJour : x)));
    } catch (err) {
      setErreurSuppression({ id: a.id, message: messageErreur(err, "Impossible de réactiver ce logement.") });
    } finally {
      setSuppressionEnCours(null);
    }
  };

  const affiches = idCible ? (cible.donnees ? [cible.donnees] : []) : liste.elements;
  const nombre = (f: Filtre) => (compteurs[f] !== undefined ? ` (${compteurs[f]})` : "");

  return (
    <>
      <EnTete
        titre="Appartements"
        description="Tous les biens publiés sur la plateforme."
        action={
          !formulaire && (
            <button onClick={() => setFormulaire("ajout")} className={classeBoutonPrincipal()}>
              <Plus size={16} />
              Ajouter
            </button>
          )
        }
      />

      {formulaire && (
        <FormulaireAppartement
          key={formulaire === "ajout" ? "ajout" : formulaire.id}
          appartement={formulaire === "ajout" ? undefined : formulaire}
          onAnnuler={() => setFormulaire(null)}
          onEnregistre={(a, avertissementPhotos) => {
            setAvertissement(avertissementPhotos ?? null);
            setDonnees((l) => (formulaire === "ajout" ? [a, ...l] : l.map((x) => (x.id === a.id ? a : x))));
            setFormulaire(null);
          }}
        />
      )}

      {calendrier && (
        <CalendrierLogement key={calendrier.id} appartement={calendrier} onFermer={() => setCalendrier(null)} />
      )}

      {avertissement && (
        <div className="mb-4">
          <MessageErreur>{avertissement}</MessageErreur>
        </div>
      )}

      {idCible && (
        <p className="flex flex-wrap items-center gap-2 text-sm mb-4">
          <span className="bg-immo-gold/15 text-immo-dark font-semibold px-3 py-1.5 rounded-full">
            Annonce n° {formaterReference(idCible)} uniquement
          </span>
          <button type="button" onClick={() => setParams({})} className="underline text-gray-600 font-semibold">
            Voir toutes les annonces
          </button>
        </p>
      )}

      <Onglets
        valeur={filtre}
        onChange={(v) => {
          setFiltre(v);
          if (idCible) setParams({});
        }}
        options={[
          { valeur: "Tous", label: `Publiés${nombre("Tous")}` },
          { valeur: "LongueDuree", label: `Longue durée${nombre("LongueDuree")}` },
          { valeur: "SejourCourt", label: `Séjour court${nombre("SejourCourt")}` },
          { valeur: "Supprimes", label: `Supprimés${nombre("Supprimes")}` },
        ]}
      />

      <EtatListe chargement={chargement} erreur={erreur} vide={affiches.length === 0} messageVide="Aucun appartement.">
        <Tableau entetes={["", "Adresse", "Bailleur", "Type", "Prix", "Disponibilité", ""]}>
          {affiches.map((a) => (
            <Fragment key={a.id}>
              <tr className={`${erreurSuppression?.id === a.id ? "" : "border-b"} border-black/5 last:border-0`}>
                <td className="pl-5 py-3 w-20">
                  <PhotoCouverture
                    photos={a.photos}
                    protegees={a.photosProtegees}
                    className="w-16 h-12 rounded-md"
                    tailleIcone={16}
                    badge360={false}
                  />
                </td>
                <td className="px-5 py-3">
                  <p className="font-semibold">{a.adresse}</p>
                  {localisation(a) && <p className="text-xs text-gray-500">{localisation(a)}</p>}
                  {a.description && <p className="text-xs text-gray-500 line-clamp-1">{a.description}</p>}
                </td>
                <td className="px-5 py-3 text-gray-600 whitespace-nowrap">{a.bailleurNom}</td>
                <td className="px-5 py-3">
                  <Badge ton="neutre">{a.typeOffre === "LongueDuree" ? "Longue durée" : "Séjour court"}</Badge>
                </td>
                <td className="px-5 py-3 whitespace-nowrap">
                  {a.typeOffre === "LongueDuree" ? (
                    <>
                      {formaterMontant(a.loyerMensuel)} / mois
                      <p className="text-xs text-gray-500">Avance : {a.dureeAvanceExigee ?? "-"} mois</p>
                    </>
                  ) : (
                    <>
                      {formaterMontant(a.prixParNuit)} / nuit
                      <p className="text-xs text-gray-500">Max : {a.dureeMaxMois ?? "-"} mois</p>
                    </>
                  )}
                </td>
                <td className="px-5 py-3">
                  <Badge
                    ton={
                      a.estDesactive || a.bailleurDesactive
                        ? "danger"
                        : a.enLocation
                          ? "info"
                          : a.disponible
                            ? "succes"
                            : "neutre"
                    }
                  >
                    {a.estDesactive
                      ? "Supprimé"
                      : a.bailleurDesactive
                        ? "Hôte désactivé"
                        : a.enLocation
                          ? "Loué"
                          : a.disponible
                            ? "Disponible"
                            : "Retiré"}
                  </Badge>
                </td>
                <td className="px-5 py-3 text-right">
                  <div className="flex flex-col items-end gap-1.5">
                    <button
                      onClick={() => {
                        setCalendrier(null);
                        setFormulaire(a);
                        window.scrollTo({ top: 0, behavior: "smooth" });
                      }}
                      disabled={a.enLocation}
                      title={
                        a.enLocation
                          ? "Logement en cours de location : il ne peut pas être modifié tant que la location dure."
                          : undefined
                      }
                      className={`${classeBoutonSecondaire(true)} disabled:cursor-not-allowed`}
                    >
                      <Pencil size={14} />
                      Modifier
                    </button>
                    {a.typeOffre === "SejourCourt" && (
                      <button
                        onClick={() => {
                          setFormulaire(null);
                          setCalendrier(a);
                          window.scrollTo({ top: 0, behavior: "smooth" });
                        }}
                        className={classeBoutonSecondaire(true)}
                      >
                        <CalendarRange size={14} />
                        Calendrier
                      </button>
                    )}
                    {a.estDesactive && (
                      <button
                        onClick={() => reactiver(a)}
                        disabled={suppressionEnCours === a.id}
                        className={classeBoutonPrincipal(true)}
                      >
                        <RotateCcw size={14} />
                        Réactiver
                      </button>
                    )}
                    {!a.enLocation && !a.estDesactive && (
                      <button
                        onClick={() => supprimer(a)}
                        disabled={suppressionEnCours === a.id}
                        className={classeBoutonDanger(true)}
                      >
                        <Trash2 size={14} />
                        {suppressionEnCours === a.id ? "Suppression..." : "Supprimer"}
                      </button>
                    )}
                  </div>
                </td>
              </tr>
              {erreurSuppression?.id === a.id && (
                <tr className="border-b border-black/5 last:border-0 bg-red-50/60">
                  <td colSpan={7} className="px-5 py-2.5">
                    <MessageErreur>{erreurSuppression.message}</MessageErreur>
                  </td>
                </tr>
              )}
            </Fragment>
          ))}
        </Tableau>
        {!idCible && (
          <Pagination page={liste.page} totalPages={liste.totalPages} onChange={liste.setPage} total={liste.total} />
        )}
      </EtatListe>
    </>
  );
}
