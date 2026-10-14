import { useEffect, useRef, useState, type FormEvent } from "react";
import { AlertTriangle, CheckCircle2, Clock, FlaskConical, Loader2, Smartphone, XCircle } from "lucide-react";
import { apiClient } from "../../api/client";
import {
  classeBoutonPrincipal,
  classeBoutonSecondaire,
  classeChamp,
  formaterMontant,
  messageErreur,
} from "../admin/outils";
import { MessageErreur } from "../admin/ui";
import type { OperateurMobile, ReservationDto, TransactionMobileDto } from "../../types/models";
import {
  DUREE_SUIVI_MS,
  estSimulation,
  INTERVALLE_SUIVI_MS,
  normaliserTelephone,
  OPERATEURS,
  operateurDe,
  statutDe,
} from "./mobileMoney";

/** « Logo » texte de l'opérateur, à ses couleurs. */
function LogoOperateur({ operateur, petit = false }: { operateur: OperateurMobile; petit?: boolean }) {
  const o = OPERATEURS[operateur];
  return (
    <span
      className={`inline-flex items-center justify-center font-extrabold rounded-md shrink-0 ${
        petit ? "text-[11px] px-1.5 py-0.5" : "text-sm px-2.5 py-1.5 min-w-16"
      }`}
      style={{ backgroundColor: o.fond, color: o.texte }}
    >
      {o.court}
    </span>
  );
}

function FormulairePaiementMobile({
  reservation,
  reste,
  onInitiee,
  onFermer,
}: {
  reservation: ReservationDto;
  reste: number;
  onInitiee: (t: TransactionMobileDto) => void;
  onFermer: () => void;
}) {
  const [operateur, setOperateur] = useState<OperateurMobile>("OrangeMoney");
  const [telephone, setTelephone] = useState("");
  const [montant, setMontant] = useState(String(reste));
  const [erreur, setErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);

  const envoyer = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);
    const numero = normaliserTelephone(telephone);
    if (!numero) {
      setErreur("Numéro invalide : 9 chiffres commençant par 6 (ex. 620 00 00 00).");
      return;
    }
    const valeur = Number(montant);
    if (!Number.isFinite(valeur) || valeur <= 0 || valeur > reste) {
      setErreur(`Le montant doit être compris entre 1 et ${formaterMontant(reste)}.`);
      return;
    }
    setEnCours(true);
    try {
      const transaction = await apiClient.post<TransactionMobileDto>("paiement-mobile/initier", {
        reservationId: reservation.id,
        operateur,
        telephone: numero,
        montant: valeur,
      });
      onInitiee(transaction);
    } catch (err) {
      setErreur(messageErreur(err, "Impossible de lancer le paiement."));
    } finally {
      setEnCours(false);
    }
  };

  return (
    <form onSubmit={envoyer} className="flex flex-col gap-4">
      <fieldset>
        <legend className="text-sm font-medium text-gray-700 mb-2">Opérateur</legend>
        <div className="grid grid-cols-1 min-[420px]:grid-cols-2 gap-2">
          {(Object.keys(OPERATEURS) as OperateurMobile[]).map((cle) => (
            <label
              key={cle}
              className={`flex items-center gap-3 rounded-xl border p-3 cursor-pointer transition-colors ${
                operateur === cle ? "border-immo-gold bg-immo-gold/10" : "border-gray-200 hover:bg-gray-50"
              }`}
            >
              <input
                type="radio"
                name={`operateur-${reservation.id}`}
                checked={operateur === cle}
                onChange={() => setOperateur(cle)}
                className="accent-immo-gold w-4 h-4 shrink-0"
              />
              <LogoOperateur operateur={cle} />
              <span className="text-sm font-semibold text-gray-800">{OPERATEURS[cle].nom}</span>
            </label>
          ))}
        </div>
      </fieldset>
      <div className="grid sm:grid-cols-2 gap-4">
        <label className="text-sm font-medium text-gray-700">
          Numéro {OPERATEURS[operateur].nom}
          <span className="flex items-stretch mt-1">
            <span className="flex items-center px-3 rounded-l-lg border border-r-0 border-gray-300 bg-gray-50 text-sm text-gray-500">
              +224
            </span>
            <input
              type="tel"
              inputMode="numeric"
              autoComplete="tel-national"
              value={telephone}
              onChange={(e) => setTelephone(e.target.value)}
              placeholder="620 00 00 00"
              className={`${classeChamp} !mt-0 rounded-l-none`}
            />
          </span>
        </label>
        <label className="text-sm font-medium text-gray-700">
          Montant (GNF)
          <input
            type="number"
            min={1}
            max={reste}
            value={montant}
            onChange={(e) => setMontant(e.target.value)}
            className={classeChamp}
          />
          <span className="block text-xs text-gray-400 font-normal mt-1">Reste dû : {formaterMontant(reste)}</span>
        </label>
      </div>
      {erreur && <MessageErreur>{erreur}</MessageErreur>}
      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={enCours} className={classeBoutonPrincipal()}>
          <Smartphone size={16} />
          {enCours ? "Envoi de la demande..." : `Payer ${formaterMontant(Number(montant) || 0)}`}
        </button>
        <button type="button" onClick={onFermer} className={classeBoutonSecondaire()}>
          Annuler
        </button>
      </div>
    </form>
  );
}

function SuiviTransaction({
  transaction,
  onChange,
  onRecommencer,
  onFermer,
}: {
  transaction: TransactionMobileDto;
  onChange: (t: TransactionMobileDto) => void;
  onRecommencer: () => void;
  onFermer: () => void;
}) {
  const statut = statutDe(transaction);
  const operateur = operateurDe(transaction);
  const [suiviArrete, setSuiviArrete] = useState(false);
  const [relance, setRelance] = useState(0);
  const [simulation, setSimulation] = useState<boolean | null>(null);
  const [erreur, setErreur] = useState<string | null>(null);
  const majRef = useRef(onChange);
  useEffect(() => {
    majRef.current = onChange;
  });

  // Suivi du statut toutes les 3 secondes, pendant 10 minutes au plus.
  useEffect(() => {
    if (statut !== "EnAttente") return;
    const debut = Date.now();
    let annule = false;
    const minuteur = window.setInterval(() => {
      if (Date.now() - debut > DUREE_SUIVI_MS) {
        window.clearInterval(minuteur);
        setSuiviArrete(true);
        return;
      }
      apiClient
        .get<TransactionMobileDto>(`paiement-mobile/${transaction.id}`)
        .then((t) => {
          if (!annule) majRef.current(t);
        })
        .catch(() => {
          // Réseau instable : on réessaie au prochain passage.
        });
    }, INTERVALLE_SUIVI_MS);
    return () => {
      annule = true;
      window.clearInterval(minuteur);
    };
  }, [transaction.id, statut, relance]);

  const simuler = async (succes: boolean) => {
    setErreur(null);
    setSimulation(succes);
    try {
      onChange(await apiClient.post<TransactionMobileDto>(`paiement-mobile/${transaction.id}/simuler`, { succes }));
    } catch (err) {
      setErreur(messageErreur(err, "La simulation a échoué."));
    } finally {
      setSimulation(null);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        <LogoOperateur operateur={operateur} />
        <div className="min-w-0">
          <p className="font-bold">Paiement de {formaterMontant(transaction.montant)}</p>
          <p className="text-xs text-gray-500">
            {OPERATEURS[operateur].nom} · {transaction.telephone}
          </p>
        </div>
      </div>

      {statut === "EnAttente" && (
        <div className="rounded-xl bg-blue-50 text-blue-900 p-4 flex flex-col gap-2" role="status">
          <p className="font-semibold flex items-center gap-2">
            {suiviArrete ? (
              <Clock size={18} className="shrink-0" />
            ) : (
              <Loader2 size={18} className="animate-spin shrink-0" />
            )}
            {suiviArrete ? "Toujours en attente de votre validation" : "En attente de validation sur votre téléphone"}
          </p>
          <p className="text-sm">{transaction.instructions}</p>
          {suiviArrete ? (
            <button
              type="button"
              onClick={() => {
                setSuiviArrete(false);
                setRelance((n) => n + 1);
              }}
              className={`${classeBoutonSecondaire(true)} self-start bg-white`}
            >
              Vérifier à nouveau
            </button>
          ) : (
            <p className="text-xs text-blue-800/80">
              Le statut se met à jour automatiquement. Vous pouvez quitter cette page : le paiement sera enregistré dès
              sa validation.
            </p>
          )}
        </div>
      )}

      {statut === "EnAttente" && estSimulation(transaction) && (
        <div className="rounded-xl border-2 border-dashed border-amber-300 bg-amber-50 p-4 flex flex-col gap-3">
          <p className="text-sm text-amber-900 flex items-start gap-2">
            <FlaskConical size={17} className="shrink-0 mt-0.5" />
            <span>
              <strong>Simulation locale</strong> : aucun opérateur n'est contacté et aucun argent n'est débité.
              Choisissez la réponse de l'opérateur.
            </span>
          </p>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              disabled={simulation !== null}
              onClick={() => simuler(true)}
              className={classeBoutonPrincipal(true)}
            >
              <CheckCircle2 size={14} />
              {simulation === true ? "Validation..." : "Simuler la validation"}
            </button>
            <button
              type="button"
              disabled={simulation !== null}
              onClick={() => simuler(false)}
              className={`${classeBoutonSecondaire(true)} bg-white`}
            >
              <XCircle size={14} />
              {simulation === false ? "Échec..." : "Simuler un échec"}
            </button>
          </div>
        </div>
      )}

      {statut === "Reussie" && transaction.messageErreur && (
        // Paiement reçu après l'annulation de la réservation : l'opérateur a débité, un remboursement est prévu.
        <div className="rounded-xl bg-amber-50 text-amber-900 p-4" role="status">
          <p className="font-semibold flex items-center gap-2">
            <AlertTriangle size={18} className="shrink-0" />
            Paiement reçu, remboursement prévu
          </p>
          <p className="text-sm mt-1">
            {formaterMontant(transaction.montant)} ont été débités. {transaction.messageErreur}
          </p>
        </div>
      )}
      {statut === "Reussie" && !transaction.messageErreur && (
        <div className="rounded-xl bg-green-50 text-green-800 p-4" role="status">
          <p className="font-semibold flex items-center gap-2">
            <CheckCircle2 size={18} className="shrink-0" />
            Paiement reçu
          </p>
          <p className="text-sm mt-1">
            {formaterMontant(transaction.montant)} ont été enregistrés sur votre réservation. Une confirmation vous est
            envoyée par e-mail.
          </p>
        </div>
      )}
      {statut === "Echouee" && (
        <div className="rounded-xl bg-red-50 text-red-800 p-4" role="alert">
          <p className="font-semibold flex items-center gap-2">
            <XCircle size={18} className="shrink-0" />
            Paiement échoué
          </p>
          <p className="text-sm mt-1">
            {transaction.messageErreur || "L'opérateur a refusé le paiement."} Aucun montant n'a été débité.
          </p>
        </div>
      )}
      {statut === "Expiree" && (
        <div className="rounded-xl bg-amber-50 text-amber-900 p-4" role="alert">
          <p className="font-semibold flex items-center gap-2">
            <Clock size={18} className="shrink-0" />
            Délai dépassé
          </p>
          <p className="text-sm mt-1">Le paiement n'a pas été validé dans les 10 minutes. Vous pouvez recommencer.</p>
        </div>
      )}

      {erreur && (
        <div className="flex flex-col gap-2">
          <MessageErreur>{erreur}</MessageErreur>
          {statut === "EnAttente" && (
            // Ex. transaction expirée entre deux vérifications : on propose d'en relancer une.
            <button type="button" onClick={onRecommencer} className={`${classeBoutonSecondaire(true)} self-start`}>
              Relancer un paiement
            </button>
          )}
        </div>
      )}
      {transaction.referenceFournisseur && (
        <p className="text-xs text-gray-400 break-all">Référence opérateur : {transaction.referenceFournisseur}</p>
      )}

      {statut !== "EnAttente" && (
        <div className="flex flex-wrap gap-2">
          {statut !== "Reussie" && (
            <button type="button" onClick={onRecommencer} className={classeBoutonPrincipal(true)}>
              Réessayer
            </button>
          )}
          <button type="button" onClick={onFermer} className={classeBoutonSecondaire(true)}>
            Fermer
          </button>
        </div>
      )}
    </div>
  );
}

/**
 * Paiement d'une réservation par Orange Money ou MTN Mobile Money : saisie du numéro et du montant,
 * puis attente de la validation sur le téléphone (statut relu toutes les 3 s).
 */
export default function PaiementMobile({
  reservation,
  onPaye,
  onFermer,
}: {
  reservation: ReservationDto;
  /** Appelé quand un paiement est réussi (pour relire la réservation et l'historique). */
  onPaye: () => void;
  onFermer: () => void;
}) {
  const reste = Math.max(reservation.montantTotal - reservation.montantPaye, 0);
  const [transaction, setTransaction] = useState<TransactionMobileDto | null>(null);
  const [reprise, setReprise] = useState(true);

  // Reprend le suivi d'une transaction encore en attente (page rechargée, ou paiement lancé depuis un autre appareil).
  useEffect(() => {
    let annule = false;
    apiClient
      .get<TransactionMobileDto | undefined>(`reservation/${reservation.id}/paiement-mobile/en-cours`)
      .then((t) => {
        if (!annule && t) setTransaction(t);
      })
      .catch(() => {})
      .finally(() => {
        if (!annule) setReprise(false);
      });
    return () => {
      annule = true;
    };
  }, [reservation.id]);

  const mettreAJour = (t: TransactionMobileDto) => {
    const statut = statutDe(t);
    const avant = transaction ? statutDe(transaction) : null;
    setTransaction(t);
    if (statut === "Reussie" && avant !== "Reussie") onPaye();
  };

  return (
    <section className="border border-black/10 rounded-xl p-4 flex flex-col gap-4">
      <h3 className="font-bold flex items-center gap-2">
        <Smartphone size={17} className="text-immo-gold" />
        Payer par mobile money
      </h3>
      {reprise ? (
        <p className="text-sm text-gray-500">Chargement...</p>
      ) : transaction ? (
        <SuiviTransaction
          transaction={transaction}
          onChange={mettreAJour}
          onRecommencer={() => setTransaction(null)}
          onFermer={onFermer}
        />
      ) : reste <= 0 ? (
        <p className="text-sm text-gray-500">Cette réservation est entièrement payée.</p>
      ) : (
        <FormulairePaiementMobile reservation={reservation} reste={reste} onInitiee={mettreAJour} onFermer={onFermer} />
      )}
    </section>
  );
}
