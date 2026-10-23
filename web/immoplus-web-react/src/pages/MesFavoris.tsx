import { Link } from "react-router-dom";
import { Heart } from "lucide-react";
import { useAuth } from "../context/auth";
import { classeBoutonPrincipal } from "../components/admin/outils";
import { useListeProgressive } from "../components/admin/pagination";
import { MessageErreur, VoirPlus } from "../components/admin/ui";
import CarteAnnonce from "../components/appartements/CarteAnnonce";
import type { AppartementDto } from "../types/models";

const segment = (a: AppartementDto) => (a.typeOffre === "LongueDuree" ? "longue-duree" : "sejour-court");

/** Annonces enregistrées par l'utilisateur (y compris celles qui ne sont plus disponibles). */
export default function MesFavoris() {
  const { estConnecte, roleUtilisateur } = useAuth();
  const autorise = estConnecte && roleUtilisateur !== "Admin";
  const liste = useListeProgressive<AppartementDto>(autorise ? "favoris" : null, 24);
  const { chargement, erreur, total } = liste;

  if (!autorise) {
    return (
      <div className="px-6 py-16 max-w-xl mx-auto text-center text-gray-600">
        <Link to="/login?retour=%2Ffavoris" className="text-immo-gold font-semibold">
          Connectez-vous
        </Link>{" "}
        pour retrouver les annonces que vous avez enregistrées.
      </div>
    );
  }

  const favoris = liste.elements;

  return (
    <div className="px-6 py-10">
      <div className="max-w-6xl mx-auto">
        <h1 className="text-2xl md:text-3xl font-extrabold mb-1">Mes favoris</h1>
        <p className="text-gray-500 text-sm mb-8">
          {chargement
            ? "Chargement..."
            : `${total} annonce${total > 1 ? "s" : ""} enregistrée${total > 1 ? "s" : ""}. Touchez le cœur pour en retirer une.`}
        </p>

        {chargement ? null : erreur ? (
          <MessageErreur>{erreur}</MessageErreur>
        ) : favoris.length === 0 ? (
          <div className="bg-white rounded-2xl shadow-sm border border-black/5 py-14 px-6 text-center">
            <Heart size={32} className="mx-auto text-immo-gold mb-3" />
            <p className="text-gray-500 mb-4">
              Aucun favori pour l'instant. Touchez le cœur d'une annonce pour la retrouver ici.
            </p>
            <div className="flex flex-wrap justify-center gap-2">
              <Link to="/annonces/sejour-court" className={classeBoutonPrincipal()}>
                Séjours courts
              </Link>
              <Link to="/annonces/longue-duree" className={classeBoutonPrincipal()}>
                Longue durée
              </Link>
            </div>
          </div>
        ) : (
          <>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
              {favoris.map((a) => (
                <CarteAnnonce
                  key={a.id}
                  appartement={a}
                  lien={`/annonces/${segment(a)}/${a.id}`}
                  afficherIndisponible
                />
              ))}
            </div>
            <VoirPlus
              aPlus={liste.aPlus}
              chargement={liste.chargementPlus}
              onClick={liste.chargerPlus}
              affiches={favoris.length}
              total={total}
              libelle="Voir plus de favoris"
            />
          </>
        )}
      </div>
    </div>
  );
}
