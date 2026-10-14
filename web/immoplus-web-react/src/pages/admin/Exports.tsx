import { useState, type FormEvent } from "react";
import { CheckCircle2, Download, FileSpreadsheet } from "lucide-react";
import { telechargerFichier } from "../../api/client";
import { EnTete, MessageErreur } from "../../components/admin/ui";
import { classeBoutonPrincipal, classeChamp, messageErreur } from "../../components/admin/outils";
import { jourLocal } from "../../components/sejour/dates";

const TYPES = [
  {
    valeur: "paiements",
    libelle: "Paiements",
    detail: "Paiements enregistrés (apports, mensualités, loyers, séjours).",
  },
  { valeur: "loyers", libelle: "Loyers", detail: "Échéances de loyer, montants dus et payés, retards." },
  { valeur: "mensualites", libelle: "Mensualités", detail: "Échéancier des financements : dû, payé, ponctualité." },
  {
    valeur: "reservations",
    libelle: "Réservations",
    detail: "Séjours courts : dates, montants, statut, remboursements.",
  },
  {
    valeur: "utilisateurs",
    libelle: "Utilisateurs",
    detail: "Comptes : rôle, statut, inscription, identité vérifiée.",
  },
  { valeur: "locations", libelle: "Locations", detail: "Locations longue durée : début, fin, préavis, statut." },
] as const;

type TypeExport = (typeof TYPES)[number]["valeur"];

/** Exports Excel (.xlsx) pour la comptabilité et le suivi, sur une période facultative. */
export default function Exports() {
  const [type, setType] = useState<TypeExport>("paiements");
  const [du, setDu] = useState("");
  const [au, setAu] = useState("");
  const [enCours, setEnCours] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);
  const [succes, setSucces] = useState<string | null>(null);

  const telecharger = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    setSucces(null);
    if (du && au && au < du) return setErreur("La date de fin doit être après la date de début.");
    const periode = new URLSearchParams();
    if (du) periode.set("du", du);
    if (au) periode.set("au", au);
    const suffixe = periode.toString();
    const nom = `immoplus-${type}-${du || "debut"}-${au || jourLocal()}.xlsx`;
    setEnCours(true);
    try {
      await telechargerFichier(`admin/exports/${type}.xlsx${suffixe ? `?${suffixe}` : ""}`, nom);
      setSucces(`Export « ${nom} » téléchargé.`);
    } catch (err) {
      setErreur(messageErreur(err, "Impossible de générer l'export."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <>
      <EnTete
        titre="Exports"
        description="Téléchargez les données au format Excel (.xlsx) : montants en GNF, dates jj/mm/aaaa, filtres prêts à l'emploi."
      />

      <form
        onSubmit={telecharger}
        className="bg-white rounded-2xl border border-black/5 shadow-sm p-5 sm:p-6 flex flex-col gap-6 max-w-3xl"
      >
        <fieldset>
          <legend className="text-sm font-semibold text-gray-700 mb-3">Données à exporter</legend>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            {TYPES.map((t) => (
              <label
                key={t.valeur}
                className={`flex items-start gap-3 rounded-xl border p-3 cursor-pointer transition-colors ${
                  type === t.valeur ? "border-immo-gold bg-immo-gold/10" : "border-gray-200 hover:border-gray-300"
                }`}
              >
                <input
                  type="radio"
                  name="type"
                  value={t.valeur}
                  checked={type === t.valeur}
                  onChange={() => setType(t.valeur)}
                  className="mt-1 accent-immo-gold"
                />
                <span className="min-w-0">
                  <span className="block font-semibold text-sm">{t.libelle}</span>
                  <span className="block text-xs text-gray-500">{t.detail}</span>
                </span>
              </label>
            ))}
          </div>
        </fieldset>

        <fieldset>
          <legend className="text-sm font-semibold text-gray-700 mb-1">Période (facultatif)</legend>
          <p className="text-xs text-gray-500 mb-3">Sans date, tout l'historique est exporté.</p>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 max-w-md">
            <label className="text-xs font-semibold text-gray-600">
              Du
              <input type="date" value={du} onChange={(e) => setDu(e.target.value)} className={classeChamp} />
            </label>
            <label className="text-xs font-semibold text-gray-600">
              Au
              <input
                type="date"
                value={au}
                min={du || undefined}
                onChange={(e) => setAu(e.target.value)}
                className={classeChamp}
              />
            </label>
          </div>
        </fieldset>

        {erreur && <MessageErreur>{erreur}</MessageErreur>}
        {succes && (
          <p className="flex items-center gap-2 text-green-700 text-sm" role="status">
            <CheckCircle2 size={16} className="shrink-0" />
            {succes}
          </p>
        )}

        <div>
          <button type="submit" disabled={enCours} className={classeBoutonPrincipal()}>
            {enCours ? <FileSpreadsheet size={16} className="animate-pulse" /> : <Download size={16} />}
            {enCours ? "Génération..." : "Télécharger (.xlsx)"}
          </button>
        </div>
      </form>
    </>
  );
}
