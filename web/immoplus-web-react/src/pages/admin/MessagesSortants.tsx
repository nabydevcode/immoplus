import { useState } from "react";
import { ChevronLeft, ChevronRight, Info, Mail, MessageSquareText } from "lucide-react";
import { Badge, EnTete, EtatListe, Onglets, Tableau } from "../../components/admin/ui";
import { classeBoutonSecondaire, formaterDateHeure, useChargement } from "../../components/admin/outils";
import type { MessageSortantDto, PagedResultDto, StatutMessageSortant } from "../../types/models";

const TAILLE_PAGE = 20;

type Filtre = "" | StatutMessageSortant;

const onglets: { valeur: Filtre; label: string }[] = [
  { valeur: "", label: "Tous" },
  { valeur: "EnAttente", label: "En attente" },
  { valeur: "Envoye", label: "Envoyés" },
  { valeur: "Echec", label: "En échec" },
  { valeur: "Abandonne", label: "Abandonnés" },
];

const statuts: Record<string, { label: string; ton: "attente" | "succes" | "danger" | "neutre" | "info" }> = {
  EnAttente: { label: "En attente", ton: "attente" },
  Envoye: { label: "Envoyé", ton: "succes" },
  Echec: { label: "Échec", ton: "danger" },
  Abandonne: { label: "Abandonné", ton: "neutre" },
};

const statutDe = (m: MessageSortantDto) => statuts[m.statut] ?? { label: m.statut, ton: "info" as const };

/** Objet de l'email ou texte du SMS, référence et événement déclencheur. */
const contenuDe = (m: MessageSortantDto) => (m.type === "Sms" ? m.texte : m.sujet) ?? "-";
const evenementDe = (m: MessageSortantDto) => m.evenement.replace(/-/g, " ");

function IconeType({ type }: { type: string }) {
  return type === "Sms" ? (
    <MessageSquareText size={14} className="shrink-0 text-gray-500" aria-label="SMS" />
  ) : (
    <Mail size={14} className="shrink-0 text-gray-500" aria-label="Email" />
  );
}

/** Ligne « tentatives / prochain essai / envoi » commune aux cartes et au tableau. */
function Suivi({ m }: { m: MessageSortantDto }) {
  return (
    <span className="text-xs text-gray-500">
      {m.tentatives} tentative{m.tentatives > 1 ? "s" : ""}
      {m.dateEnvoi && ` · envoyé le ${formaterDateHeure(m.dateEnvoi)}`}
      {!m.dateEnvoi && m.prochaineTentative && ` · prochain essai le ${formaterDateHeure(m.prochaineTentative)}`}
      {!m.dateEnvoi &&
        !m.prochaineTentative &&
        m.dateDerniereTentative &&
        ` · dernier essai le ${formaterDateHeure(m.dateDerniereTentative)}`}
    </span>
  );
}

/** Boîte d'envoi des emails et SMS : suivi des envois en arrière-plan et des nouvelles tentatives. */
export default function MessagesSortants() {
  const [statut, setStatut] = useState<Filtre>("");
  const [page, setPage] = useState(1);
  const { donnees, chargement, erreur } = useChargement<PagedResultDto<MessageSortantDto>>(
    `admin/messages-sortants?page=${page}&taille=${TAILLE_PAGE}${statut ? `&statut=${statut}` : ""}`,
  );
  const messages = donnees?.donnees ?? [];
  const totalPages = Math.max(donnees?.totalPages ?? 1, 1);

  const changerStatut = (s: Filtre) => {
    setStatut(s);
    setPage(1);
  };

  return (
    <>
      <EnTete
        titre="Messages sortants"
        description={`Boîte d'envoi des emails et SMS${donnees ? ` · ${donnees.totalElements} message${donnees.totalElements > 1 ? "s" : ""}` : ""}.`}
      />

      <p className="flex items-start gap-2 text-sm bg-blue-50 text-blue-900 border border-blue-100 rounded-xl px-4 py-3 mb-6">
        <Info size={16} className="shrink-0 mt-0.5" />
        <span>
          Les emails et SMS partent en arrière-plan ; en cas d'échec, nouvel essai après 1 min, 5 min, 30 min, 2 h, puis
          abandon.
        </span>
      </p>

      <Onglets valeur={statut} options={onglets} onChange={changerStatut} />

      <EtatListe
        chargement={chargement}
        erreur={erreur}
        vide={messages.length === 0}
        messageVide="Aucun message dans cette catégorie."
      >
        {/* Mobile : une carte par message ; à partir de md : tableau. */}
        <ul className="md:hidden flex flex-col gap-3">
          {messages.map((m) => {
            const s = statutDe(m);
            return (
              <li key={m.id} className="bg-white rounded-2xl border border-black/5 shadow-sm p-4 text-sm">
                <div className="flex items-start justify-between gap-3">
                  <p className="min-w-0 flex items-center gap-1.5">
                    <IconeType type={m.type} />
                    <span className="font-semibold truncate">{m.destinataire}</span>
                  </p>
                  <Badge ton={s.ton}>{s.label}</Badge>
                </div>
                <p className="mt-2 text-gray-700 wrap-break-word">{contenuDe(m)}</p>
                {m.reference && <p className="text-xs text-gray-500 mt-1">{m.reference}</p>}
                {m.derniereErreur && <p className="text-xs text-red-600 mt-1 wrap-break-word">{m.derniereErreur}</p>}
                <p className="mt-2">
                  <Suivi m={m} />
                </p>
                <p className="mt-1 text-xs text-gray-400">
                  Créé le {formaterDateHeure(m.dateCreation)} · {evenementDe(m)}
                </p>
              </li>
            );
          })}
        </ul>
        <div className="hidden md:block">
          <Tableau entetes={["Créé le", "Destinataire", "Événement", "Contenu", "Statut"]}>
            {messages.map((m) => {
              const s = statutDe(m);
              return (
                <tr key={m.id} className="border-b border-black/5 last:border-0 align-top">
                  <td className="px-5 py-3 whitespace-nowrap text-gray-600">{formaterDateHeure(m.dateCreation)}</td>
                  <td className="px-5 py-3 max-w-60 break-all">
                    <span className="flex items-center gap-1.5">
                      <IconeType type={m.type} />
                      {m.destinataire}
                    </span>
                  </td>
                  <td className="px-5 py-3 whitespace-nowrap text-gray-600">
                    {evenementDe(m)}
                    {m.reference && <span className="block text-xs text-gray-500">{m.reference}</span>}
                  </td>
                  <td className="px-5 py-3 min-w-56 max-w-md text-gray-700 wrap-break-word">
                    {contenuDe(m)}
                    {m.derniereErreur && <p className="text-xs text-red-600 mt-1">{m.derniereErreur}</p>}
                  </td>
                  <td className="px-5 py-3 min-w-44">
                    <Badge ton={s.ton}>{s.label}</Badge>
                    <span className="block mt-1">
                      <Suivi m={m} />
                    </span>
                  </td>
                </tr>
              );
            })}
          </Tableau>
        </div>
        {totalPages > 1 && (
          <div className="flex items-center justify-center gap-3 mt-4 text-sm">
            <button
              type="button"
              onClick={() => setPage((p) => Math.max(p - 1, 1))}
              disabled={page <= 1}
              className={classeBoutonSecondaire(true)}
            >
              <ChevronLeft size={14} />
              Précédents
            </button>
            <span className="text-gray-500">
              Page {page} / {totalPages}
            </span>
            <button
              type="button"
              onClick={() => setPage((p) => Math.min(p + 1, totalPages))}
              disabled={page >= totalPages}
              className={classeBoutonSecondaire(true)}
            >
              Suivants
              <ChevronRight size={14} />
            </button>
          </div>
        )}
      </EtatListe>
    </>
  );
}
