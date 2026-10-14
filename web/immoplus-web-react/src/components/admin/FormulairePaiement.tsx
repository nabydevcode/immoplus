import { useState, type FormEvent } from "react";
import { apiClient } from "../../api/client";
import { MessageErreur } from "./ui";
import {
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterMontant,
  messageErreur,
  MODES_PAIEMENT,
} from "./outils";
import { jourLocal } from "../sejour/dates";

/**
 * Enregistre un paiement sur une mensualité, un loyer ou une réservation (exactement l'un des trois).
 * `montantSuggere` est le reste dû : l'API refuse un montant supérieur.
 */
export default function FormulairePaiement({
  cible,
  montantSuggere,
  onEnregistre,
  onAnnuler,
}: {
  cible: { mensualiteId: number } | { reservationId: number } | { loyerId: number };
  montantSuggere: number;
  onEnregistre: () => void;
  onAnnuler: () => void;
}) {
  const [montant, setMontant] = useState(String(Math.max(montantSuggere, 0)));
  const [modePaiement, setModePaiement] = useState(MODES_PAIEMENT[0]);
  const [reference, setReference] = useState("");
  const [aujourdhui] = useState(jourLocal);
  const [datePaiement, setDatePaiement] = useState(aujourdhui);
  const [erreur, setErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    if (!(Number(montant) > 0)) {
      setErreur("Le montant doit être supérieur à 0.");
      return;
    }
    if (montantSuggere > 0 && Number(montant) > montantSuggere) {
      setErreur(`Le montant dépasse le reste dû (${formaterMontant(montantSuggere)}).`);
      return;
    }
    if (!datePaiement) {
      setErreur("Indiquez la date du paiement.");
      return;
    }
    setEnCours(true);
    try {
      await apiClient.post("paiement/enregistrer", {
        ...cible,
        montant: Number(montant),
        datePaiement,
        modePaiement,
        reference: reference.trim() || null,
      });
      onEnregistre();
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'enregistrer le paiement."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <form onSubmit={envoyer} className="bg-white rounded-xl border border-black/5 p-4">
      <div className="grid sm:grid-cols-4 gap-3">
        <label className="text-xs font-medium text-gray-700">
          Montant (GNF)
          <input
            type="number"
            min="1"
            max={montantSuggere > 0 ? montantSuggere : undefined}
            required
            value={montant}
            onChange={(e) => setMontant(e.target.value)}
            className={classeChamp}
          />
        </label>
        <label className="text-xs font-medium text-gray-700">
          Mode
          <select value={modePaiement} onChange={(e) => setModePaiement(e.target.value)} className={classeChamp}>
            {MODES_PAIEMENT.map((m) => (
              <option key={m}>{m}</option>
            ))}
          </select>
        </label>
        <label className="text-xs font-medium text-gray-700">
          Date
          <input
            type="date"
            value={datePaiement}
            max={aujourdhui}
            required
            onChange={(e) => setDatePaiement(e.target.value)}
            className={classeChamp}
          />
        </label>
        <label className="text-xs font-medium text-gray-700">
          Référence (optionnel)
          <input value={reference} onChange={(e) => setReference(e.target.value)} className={classeChamp} />
        </label>
      </div>
      {erreur && (
        <div className="mt-3">
          <MessageErreur>{erreur}</MessageErreur>
        </div>
      )}
      <div className="flex gap-2 mt-4">
        <button type="submit" disabled={enCours} className={classeBoutonPrincipal(true)}>
          {enCours ? "Enregistrement..." : "Enregistrer le paiement"}
        </button>
        <button type="button" onClick={onAnnuler} className={classeBoutonSecondaire(true)}>
          Annuler
        </button>
      </div>
    </form>
  );
}
