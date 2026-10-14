import { Award, CalendarCheck2, MessageCircle, ShieldCheck, Star, UserRound } from "lucide-react";
import type { AppartementDto, PolitiqueAnnulation } from "../../types/models";
import { formaterNote, libelleDelaiReponse, moisAnnee } from "../avis/notes";
import { NOTE_POLITIQUE, POLITIQUES } from "./politiques";
import Signaler from "../signalement/Signaler";

/** Badge Superhôte (carte d'annonce et fiche). */
export function BadgeSuperhote({ petit }: { petit?: boolean }) {
  return (
    <span
      className={`inline-flex items-center gap-1 bg-immo-dark text-immo-gold font-semibold rounded-full whitespace-nowrap ${
        petit ? "text-[11px] px-2 py-0.5" : "text-xs px-3 py-1.5"
      }`}
    >
      <Award size={petit ? 11 : 13} />
      Superhôte
    </span>
  );
}

/** Bloc « Votre hôte » de la fiche : nom, badges de confiance, réactivité, ancienneté. */
export function BlocHote({ appartement, signalable }: { appartement: AppartementDto; signalable?: boolean }) {
  const hote = appartement.hote;
  const nom = hote?.nom ?? appartement.bailleurNom;
  const delai = libelleDelaiReponse(hote?.delaiReponseHeures);
  const depuis = moisAnnee(hote?.membreDepuis);
  const details = [
    hote?.tauxReponse != null
      ? { icone: MessageCircle, texte: `Taux de réponse : ${Math.round(hote.tauxReponse)} %` }
      : null,
    delai ? { icone: CalendarCheck2, texte: `Répond généralement ${delai}` } : null,
  ].filter((d) => d !== null);

  return (
    <section className="bg-white rounded-2xl border border-black/5 shadow-sm p-5 sm:p-6">
      <div className="flex items-center gap-4">
        <span className="relative flex items-center justify-center w-14 h-14 rounded-full bg-immo-dark text-immo-gold shrink-0">
          <UserRound size={26} />
          {hote?.superhote && (
            <span className="absolute -bottom-1 -right-1 flex items-center justify-center w-6 h-6 rounded-full bg-immo-gold text-immo-dark border-2 border-white">
              <Award size={12} />
            </span>
          )}
        </span>
        <div className="min-w-0">
          <h2 className="font-extrabold text-lg wrap-break-word">
            {appartement.typeOffre === "LongueDuree" ? "Le bailleur" : "Votre hôte"} : {nom}
          </h2>
          {depuis && <p className="text-sm text-gray-500">Membre depuis {depuis}</p>}
        </div>
      </div>

      {hote && (
        <>
          <div className="flex flex-wrap gap-2 mt-4">
            {hote.superhote && <BadgeSuperhote />}
            {hote.identiteVerifiee && (
              <span className="inline-flex items-center gap-1 bg-immo-sage-light text-immo-sage text-xs font-semibold px-3 py-1.5 rounded-full whitespace-nowrap">
                <ShieldCheck size={13} />
                Identité vérifiée
              </span>
            )}
            {hote.noteMoyenne != null && hote.nombreAvis > 0 && (
              <span className="inline-flex items-center gap-1 bg-immo-dark/5 text-xs font-semibold px-3 py-1.5 rounded-full whitespace-nowrap">
                <Star size={12} className="text-immo-gold" fill="currentColor" />
                {formaterNote(hote.noteMoyenne)} · {hote.nombreAvis} avis
              </span>
            )}
          </div>
          {hote.superhote && (
            <p className="text-sm text-gray-600 mt-3">
              Les Superhôtes sont des hôtes expérimentés et très bien notés, qui répondent vite et n'annulent pas.
            </p>
          )}
          {details.length > 0 && (
            <ul className="flex flex-col gap-1.5 mt-3 text-sm text-gray-700">
              {details.map(({ icone: Icone, texte }) => (
                <li key={texte} className="flex items-center gap-2">
                  <Icone size={16} className="text-immo-gold shrink-0" />
                  {texte}
                </li>
              ))}
            </ul>
          )}
        </>
      )}
      {signalable && (
        <div className="mt-4">
          <Signaler cible="Utilisateur" cibleId={hote?.id ?? appartement.bailleurId} />
        </div>
      )}
    </section>
  );
}

/** Conditions d'annulation de l'annonce, expliquées. */
export function SectionPolitique({ politique }: { politique: PolitiqueAnnulation }) {
  const p = POLITIQUES[politique];
  return (
    <section>
      <h2 className="font-extrabold text-lg mb-1">Conditions d'annulation</h2>
      <p className="text-sm text-gray-700 mb-3">
        Politique <strong>{p.libelle.toLowerCase()}</strong> : {p.resume.charAt(0).toLowerCase() + p.resume.slice(1)}
      </p>
      <ul className="flex flex-col gap-1.5 text-sm text-gray-600 list-disc pl-5">
        {p.regles.map((r) => (
          <li key={r}>{r}</li>
        ))}
      </ul>
      <p className="text-xs text-gray-500 mt-3">{NOTE_POLITIQUE}</p>
    </section>
  );
}
