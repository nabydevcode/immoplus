import { useState } from "react";
import { Download, Info } from "lucide-react";
import { telechargerFichier } from "../../api/client";
import { EnTete, EtatListe, MessageErreur } from "../../components/admin/ui";
import { classeBoutonSecondaire, formaterDateHeure, messageErreur, useChargement } from "../../components/admin/outils";
import { formaterTaille } from "../../components/documents/pdf";
import type { SauvegardeDto } from "../../types/models";

/**
 * Sauvegardes PostgreSQL (pg_dump), faites automatiquement chaque jour par le serveur (les 14 plus récentes sont
 * conservées). L'admin les consulte et les télécharge ; il ne les déclenche pas depuis le site.
 */
export default function Sauvegardes() {
  const { donnees, chargement, erreur } = useChargement<SauvegardeDto[]>("admin/sauvegardes");
  const [telechargement, setTelechargement] = useState<string | null>(null);
  const [erreurAction, setErreurAction] = useState<string | null>(null);

  const sauvegardes = [...(donnees ?? [])].sort((a, b) => b.date.localeCompare(a.date));

  const telecharger = async (nom: string) => {
    setErreurAction(null);
    setTelechargement(nom);
    try {
      await telechargerFichier(`admin/sauvegardes/${encodeURIComponent(nom)}`, nom);
    } catch (err) {
      setErreurAction(messageErreur(err, "Téléchargement impossible."));
    } finally {
      setTelechargement(null);
    }
  };

  return (
    <>
      <EnTete
        titre="Sauvegardes"
        description="Copies de la base de données, faites automatiquement chaque jour par le serveur."
      />

      <p className="flex items-start gap-2 text-sm text-gray-600 bg-white border border-black/5 rounded-xl px-4 py-3 mb-6">
        <Info size={16} className="text-immo-gold shrink-0 mt-0.5" />
        <span>
          Les 14 sauvegardes les plus récentes sont conservées sur le serveur (fichiers PostgreSQL « .dump »). Pour
          restaurer, utilisez le script <code className="text-xs">infra/restaurer-postgres.sh</code> décrit dans le
          README. Gardez aussi une copie hors du serveur.
        </span>
      </p>

      <div className="mb-4 flex flex-col gap-2" aria-live="polite">
        {erreurAction && <MessageErreur>{erreurAction}</MessageErreur>}
      </div>

      <EtatListe
        chargement={chargement}
        erreur={erreur}
        vide={sauvegardes.length === 0}
        messageVide="Aucune sauvegarde pour l'instant."
      >
        <ul className="bg-white rounded-2xl border border-black/5 shadow-sm divide-y divide-black/5">
          {sauvegardes.map((s, i) => (
            <li key={s.nom} className="flex items-center justify-between gap-3 px-4 sm:px-5 py-3">
              <div className="min-w-0">
                <p className="font-mono text-xs sm:text-sm truncate" title={s.nom}>
                  {s.nom}
                  {i === 0 && (
                    <span className="ml-2 font-sans text-[10px] font-bold uppercase text-green-700">Plus récente</span>
                  )}
                </p>
                <p className="text-xs text-gray-500 mt-0.5">
                  {formaterDateHeure(s.date)} · {formaterTaille(s.taille)}
                </p>
              </div>
              <button
                type="button"
                onClick={() => telecharger(s.nom)}
                disabled={telechargement !== null}
                className={`${classeBoutonSecondaire(true)} shrink-0`}
                aria-label={`Télécharger ${s.nom}`}
              >
                <Download size={14} />
                <span className="hidden sm:inline">
                  {telechargement === s.nom ? "Téléchargement..." : "Télécharger"}
                </span>
              </button>
            </li>
          ))}
        </ul>
      </EtatListe>
    </>
  );
}
