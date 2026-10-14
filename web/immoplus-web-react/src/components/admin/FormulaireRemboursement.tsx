import { useState, type FormEvent } from "react";
import { apiClient } from "../../api/client";
import type { ReservationDto } from "../../types/models";
import { MessageErreur } from "./ui";
import {
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterMontant,
  messageErreur,
  MODES_PAIEMENT,
} from "./outils";
import { resteARembourser } from "../sejour/reservations";
import { jourLocal } from "../sejour/dates";

/** Enregistre le remboursement versé au voyageur après une annulation (admin). */
export default function FormulaireRemboursement({
  reservation,
  onEnregistre,
  onAnnuler,
}: {
  reservation: ReservationDto;
  onEnregistre: () => void;
  onAnnuler: () => void;
}) {
  const maximum = resteARembourser(reservation);
  const [montant, setMontant] = useState(String(maximum));
  const [modePaiement, setModePaiement] = useState(MODES_PAIEMENT[0]);
  const [reference, setReference] = useState("");
  const [aujourdhui] = useState(jourLocal);
  const [date, setDate] = useState(aujourdhui);
  const [erreur, setErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    const valeur = Number(montant);
    if (!(valeur > 0)) {
      setErreur("Le montant doit être supérieur à 0.");
      return;
    }
    if (valeur > maximum) {
      setErreur(`Le montant ne peut pas dépasser ${formaterMontant(maximum)}.`);
      return;
    }
    setEnCours(true);
    try {
      await apiClient.post(`reservation/${reservation.id}/remboursement`, {
        montant: valeur,
        modePaiement,
        reference: reference.trim() || null,
        date,
      });
      onEnregistre();
    } catch (err) {
      setErreur(messageErreur(err, "Impossible d'enregistrer le remboursement."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <form onSubmit={envoyer} className="bg-white rounded-xl border border-black/5 p-4">
      <p className="text-sm text-gray-600 mb-3">
        Remboursement à verser à {reservation.locataireNom} :{" "}
        <strong className="text-gray-800">{formaterMontant(maximum)}</strong>
        {reservation.montantRembourse ? ` (déjà remboursé : ${formaterMontant(reservation.montantRembourse)})` : ""}. Le
        voyageur est prévenu par e-mail.
      </p>
      <div className="grid sm:grid-cols-4 gap-3">
        <label className="text-xs font-medium text-gray-700">
          Montant (GNF)
          <input
            type="number"
            min="1"
            max={maximum}
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
            value={date}
            max={aujourdhui}
            onChange={(e) => setDate(e.target.value)}
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
          {enCours ? "Enregistrement..." : "Enregistrer le remboursement"}
        </button>
        <button type="button" onClick={onAnnuler} className={classeBoutonSecondaire(true)}>
          Annuler
        </button>
      </div>
    </form>
  );
}
