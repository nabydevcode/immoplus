import { useState, type FormEvent } from "react";
import { Lock, Unlock, X } from "lucide-react";
import { apiClient } from "../../api/client";
import type { AppartementDto } from "../../types/models";
import { MessageErreur } from "../admin/ui";
import { classeBoutonPrincipal, classeBoutonSecondaire, classeChamp, messageErreur } from "../admin/outils";
import Calendrier, { type EtatJour } from "./Calendrier";
import {
  ajouterJours,
  ajouterMois,
  ecartJours,
  jour,
  jourCourt,
  jourLocal,
  moisDe,
  premiereNuitOccupee,
} from "./dates";
import { useCalendrier } from "./useCalendrier";

function Legende() {
  return (
    <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-gray-500 mt-4">
      <span className="inline-flex items-center gap-1.5">
        <span className="w-3 h-3 rounded bg-blue-50 border border-blue-200" /> Réservé
      </span>
      <span className="inline-flex items-center gap-1.5">
        <span className="w-3 h-3 rounded bg-gray-200" /> Bloqué par vous
      </span>
      <span className="inline-flex items-center gap-1.5">
        <span className="w-3 h-3 rounded bg-immo-dark" /> Sélection
      </span>
    </div>
  );
}

/**
 * Calendrier d'un séjour court côté hôte (bailleur propriétaire ou admin) :
 * nuits réservées et bloquées, blocage d'une période avec un motif, déblocage.
 */
export default function CalendrierLogement({
  appartement,
  onFermer,
}: {
  appartement: AppartementDto;
  onFermer: () => void;
}) {
  const [aujourdhui] = useState(jourLocal);
  const [mois, setMois] = useState(() => moisDe(jourLocal()));
  const calendrier = useCalendrier(appartement.id, aujourdhui, mois);
  const { nuits } = calendrier;
  // Première et dernière nuit bloquées (incluses).
  const [debut, setDebut] = useState<string | null>(null);
  const [fin, setFin] = useState<string | null>(null);
  const [motif, setMotif] = useState("");
  const [enCours, setEnCours] = useState<"blocage" | number | null>(null);
  const [erreur, setErreur] = useState<string | null>(null);

  const choixFin = debut !== null && fin === null;
  const limite = choixFin ? premiereNuitOccupee(nuits, debut) : null;

  const etatJour = (iso: string): EtatJour => {
    if (iso < aujourdhui) return { ton: "passe", cliquable: false };
    const periode = nuits.get(iso);
    if (periode?.type === "Reservee") return { ton: "reserve", cliquable: false, titre: "Réservé par un voyageur" };
    if (periode) return { ton: "bloque", cliquable: false, titre: periode.motif ?? "Bloqué" };
    if (choixFin && iso >= debut && limite && iso > limite) return { ton: "libre", cliquable: false };
    return { ton: "libre", cliquable: true };
  };

  const choisir = (iso: string) => {
    setErreur(null);
    if (choixFin && iso >= debut) setFin(iso);
    else {
      setDebut(iso);
      setFin(null);
    }
  };

  const bloquer = async (e: FormEvent) => {
    e.preventDefault();
    if (!debut) return;
    setErreur(null);
    setEnCours("blocage");
    try {
      await apiClient.post(`appartement/${appartement.id}/indisponibilites`, {
        dateDebut: debut,
        dateFin: ajouterJours(fin ?? debut, 1),
        motif: motif.trim() || null,
      });
      setDebut(null);
      setFin(null);
      setMotif("");
      calendrier.recharger();
    } catch (err) {
      setErreur(messageErreur(err, "Impossible de bloquer ces dates."));
    } finally {
      setEnCours(null);
    }
  };

  const debloquer = async (id: number) => {
    setErreur(null);
    setEnCours(id);
    try {
      await apiClient.delete(`appartement/indisponibilites/${id}`);
      calendrier.recharger();
    } catch (err) {
      setErreur(messageErreur(err, "Impossible de débloquer ces dates."));
    } finally {
      setEnCours(null);
    }
  };

  const blocages = (calendrier.donnees ?? [])
    .filter((p) => p.type === "Bloquee" && p.id !== null)
    .sort((a, b) => a.debut.localeCompare(b.debut));
  const nuitsSelection = debut ? ecartJours(debut, fin ?? debut) + 1 : 0;

  return (
    <section className="bg-white rounded-2xl border border-black/5 shadow-sm p-5 sm:p-6 mb-6">
      <div className="flex items-start justify-between gap-3 mb-5">
        <div className="min-w-0">
          <h2 className="font-extrabold text-lg">Calendrier</h2>
          <p className="text-sm text-gray-500 wrap-break-word">{appartement.adresse}</p>
        </div>
        <button type="button" onClick={onFermer} className="text-gray-400 hover:text-gray-700" aria-label="Fermer">
          <X size={20} />
        </button>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2 min-w-0">
          <Calendrier
            mois={mois}
            onChangerMois={setMois}
            moisMin={moisDe(aujourdhui)}
            moisMax={ajouterMois(moisDe(aujourdhui), 13)}
            etatJour={etatJour}
            debut={debut}
            fin={fin ?? debut}
            onChoisir={choisir}
            proprietaire
          />
          <Legende />
        </div>

        <div className="flex flex-col gap-5">
          <form onSubmit={bloquer} className="flex flex-col gap-3">
            <h3 className="font-bold text-sm flex items-center gap-1.5">
              <Lock size={15} className="text-immo-gold" />
              Bloquer des dates
            </h3>
            {debut ? (
              <p className="text-sm text-gray-700">
                Nuits du <strong>{jourCourt(debut)}</strong>
                {fin && fin !== debut && (
                  <>
                    {" "}
                    au <strong>{jourCourt(fin)}</strong>
                  </>
                )}{" "}
                ({nuitsSelection} nuit{nuitsSelection > 1 ? "s" : ""})
              </p>
            ) : (
              <p className="text-sm text-gray-500">
                Cliquez sur la première nuit à bloquer, puis sur la dernière (une seule nuit : un seul clic).
              </p>
            )}
            <label className="text-sm font-medium text-gray-700">
              Motif <span className="text-gray-400 font-normal">(facultatif, visible par vous seul)</span>
              <input
                value={motif}
                onChange={(e) => setMotif(e.target.value)}
                maxLength={200}
                placeholder="Ex. travaux, usage personnel"
                className={classeChamp}
              />
            </label>
            <div className="flex flex-wrap gap-2">
              <button type="submit" disabled={!debut || enCours !== null} className={classeBoutonPrincipal(true)}>
                {enCours === "blocage" ? "Blocage..." : "Bloquer ces dates"}
              </button>
              {debut && (
                <button
                  type="button"
                  onClick={() => {
                    setDebut(null);
                    setFin(null);
                  }}
                  className={classeBoutonSecondaire(true)}
                >
                  Effacer
                </button>
              )}
            </div>
          </form>

          {erreur && <MessageErreur>{erreur}</MessageErreur>}
          {calendrier.erreur && <MessageErreur>{calendrier.erreur}</MessageErreur>}

          <div>
            <h3 className="font-bold text-sm mb-2">Dates bloquées</h3>
            {blocages.length === 0 ? (
              <p className="text-sm text-gray-500">Aucune date bloquée à venir.</p>
            ) : (
              <ul className="flex flex-col gap-2">
                {blocages.map((b) => {
                  const derniere = ajouterJours(jour(b.fin), -1);
                  return (
                    <li
                      key={b.id}
                      className="flex items-start justify-between gap-2 text-sm border border-black/5 rounded-lg px-3 py-2"
                    >
                      <div className="min-w-0">
                        <p className="font-semibold">
                          {jourCourt(b.debut)}
                          {derniere !== jour(b.debut) && ` → ${jourCourt(derniere)}`}
                        </p>
                        {b.motif && <p className="text-xs text-gray-500 wrap-break-word">{b.motif}</p>}
                      </div>
                      <button
                        type="button"
                        onClick={() => debloquer(b.id!)}
                        disabled={enCours !== null}
                        className="inline-flex items-center gap-1 text-xs font-semibold text-gray-600 hover:text-immo-dark shrink-0 disabled:opacity-50"
                      >
                        <Unlock size={13} />
                        {enCours === b.id ? "..." : "Débloquer"}
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        </div>
      </div>
    </section>
  );
}
