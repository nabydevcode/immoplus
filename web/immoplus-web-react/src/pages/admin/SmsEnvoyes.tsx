import { useState } from "react";
import { ChevronLeft, ChevronRight, FlaskConical } from "lucide-react";
import { Badge, EnTete, EtatListe, Tableau } from "../../components/admin/ui";
import { classeBoutonSecondaire, formaterDateHeure, useChargement } from "../../components/admin/outils";
import type { NotificationSmsDto, PagedResultDto } from "../../types/models";

const TAILLE_PAGE = 20;

// L'API peut sérialiser les énumérations en nombre ou en texte.
const canaux: Record<string, string> = { Sms: "SMS", WhatsApp: "WhatsApp", "0": "SMS", "1": "WhatsApp" };
const statuts: Record<string, { label: string; ton: "succes" | "danger" | "info" }> = {
  Envoye: { label: "Envoyé", ton: "succes" },
  Echec: { label: "Échec", ton: "danger" },
  Simule: { label: "Simulé", ton: "info" },
  "0": { label: "Envoyé", ton: "succes" },
  "1": { label: "Échec", ton: "danger" },
  "2": { label: "Simulé", ton: "info" },
};

/** Historique des SMS / WhatsApp envoyés aux utilisateurs (mode simulation tant qu'aucun fournisseur n'est branché). */
export default function SmsEnvoyes() {
  const [page, setPage] = useState(1);
  const { donnees, chargement, erreur } = useChargement<PagedResultDto<NotificationSmsDto>>(
    `admin/sms?page=${page}&taille=${TAILLE_PAGE}`,
  );
  const sms = donnees?.donnees ?? [];
  const totalPages = Math.max(donnees?.totalPages ?? 1, 1);

  return (
    <>
      <EnTete
        titre="SMS envoyés"
        description={`Notifications courtes envoyées par SMS ou WhatsApp, en plus des emails${
          donnees ? ` · ${donnees.totalElements} au total` : ""
        }.`}
      />

      <p className="flex items-start gap-2 text-sm bg-blue-50 text-blue-900 border border-blue-100 rounded-xl px-4 py-3 mb-6">
        <FlaskConical size={16} className="shrink-0 mt-0.5" />
        <span>
          <strong>Simulation : aucun SMS réel n'est envoyé.</strong> Aucun fournisseur SMS/WhatsApp n'est encore branché
          ; les messages sont enregistrés ici tels qu'ils seraient envoyés. Les utilisateurs peuvent les désactiver dans
          leur profil.
        </span>
      </p>

      <EtatListe chargement={chargement} erreur={erreur} vide={sms.length === 0} messageVide="Aucun SMS envoyé.">
        {/* Mobile : une carte par message ; à partir de md : tableau. */}
        <ul className="md:hidden flex flex-col gap-3">
          {sms.map((s) => {
            const statut = statuts[String(s.statut)] ?? { label: String(s.statut), ton: "info" as const };
            return (
              <li key={s.id} className="bg-white rounded-2xl border border-black/5 shadow-sm p-4 text-sm">
                <div className="flex items-start justify-between gap-3">
                  <p className="min-w-0">
                    <span className="block font-semibold truncate">{s.utilisateurNom ?? s.telephone}</span>
                    <span className="text-xs text-gray-500">
                      {s.utilisateurNom && `${s.telephone || "-"} · `}
                      {canaux[String(s.canal)] ?? String(s.canal)}
                    </span>
                  </p>
                  <Badge ton={statut.ton}>{statut.label}</Badge>
                </div>
                <p className="mt-2 text-gray-700 wrap-break-word">{s.texte}</p>
                {s.erreur && <p className="text-xs text-red-600 mt-1">{s.erreur}</p>}
                <p className="mt-2 text-xs text-gray-400">
                  {formaterDateHeure(s.dateEnvoi)} · {s.evenement.replace(/-/g, " ")}
                </p>
              </li>
            );
          })}
        </ul>
        <div className="hidden md:block">
          <Tableau entetes={["Date", "Destinataire", "Canal", "Événement", "Message", "Statut"]}>
            {sms.map((s) => {
              const statut = statuts[String(s.statut)] ?? { label: String(s.statut), ton: "info" as const };
              return (
                <tr key={s.id} className="border-b border-black/5 last:border-0 align-top">
                  <td className="px-5 py-3 whitespace-nowrap text-gray-600">{formaterDateHeure(s.dateEnvoi)}</td>
                  <td className="px-5 py-3 whitespace-nowrap">
                    {s.utilisateurNom && <span className="block font-semibold">{s.utilisateurNom}</span>}
                    <span className="text-gray-500">{s.telephone || "-"}</span>
                  </td>
                  <td className="px-5 py-3 whitespace-nowrap">{canaux[String(s.canal)] ?? String(s.canal)}</td>
                  <td className="px-5 py-3 whitespace-nowrap text-gray-600">{s.evenement.replace(/-/g, " ")}</td>
                  <td className="px-5 py-3 min-w-64 max-w-md text-gray-700 wrap-break-word">
                    {s.texte}
                    {s.erreur && <p className="text-xs text-red-600 mt-1">{s.erreur}</p>}
                  </td>
                  <td className="px-5 py-3">
                    <Badge ton={statut.ton}>{statut.label}</Badge>
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
