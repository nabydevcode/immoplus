import type { ReservationDto } from "../../types/models";
import { formaterDate, formaterMontant } from "../admin/outils";
import { formaterJour } from "./dates";
import { libellesAnnuleePar, resteARembourser } from "./reservations";

/** Qui a annulé, quand, pourquoi, et où en est le remboursement du voyageur. */
export default function InfoAnnulation({
  reservation: r,
  pourVoyageur,
}: {
  reservation: ReservationDto;
  pourVoyageur?: boolean;
}) {
  const aRembourser = r.montantARembourser ?? 0;
  const rembourse = r.montantRembourse ?? 0;
  const reste = resteARembourser(r);
  const auteur = r.annuleePar
    ? pourVoyageur && r.annuleePar === "Voyageur"
      ? "vous"
      : libellesAnnuleePar[r.annuleePar]
    : null;

  return (
    <div className="text-xs bg-red-50 text-red-900 rounded-lg px-3 py-2 flex flex-col gap-1">
      <p>
        {r.annuleePar === "Systeme" ? "Annulée automatiquement" : `Annulée${auteur ? ` par ${auteur}` : ""}`}
        {r.dateAnnulation ? ` le ${formaterDate(r.dateAnnulation)}` : ""}
        {r.motifAnnulation ? ` : ${r.motifAnnulation}` : "."}
      </p>
      {r.montantARembourser != null &&
        (aRembourser === 0 ? (
          <p>Aucun remboursement n'est dû.</p>
        ) : (
          <p>
            Remboursement prévu : <strong>{formaterMontant(aRembourser)}</strong>
            {rembourse > 0 && (
              <>
                {" "}
                · remboursé : <strong className="text-green-800">{formaterMontant(rembourse)}</strong>
                {r.dateRemboursement ? ` le ${formaterJour(r.dateRemboursement)}` : ""}
              </>
            )}
            {reste > 0 &&
              (pourVoyageur ? " · en cours de traitement par ImmoPlus." : ` · reste ${formaterMontant(reste)}.`)}
          </p>
        ))}
    </div>
  );
}
