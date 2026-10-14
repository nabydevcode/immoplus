import type { ReactNode } from "react";
import { AlertCircle, Inbox } from "lucide-react";

export function EnTete({ titre, description, action }: { titre: string; description: string; action?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-4 mb-8">
      <div>
        <h1 className="text-2xl md:text-3xl font-extrabold mb-1">{titre}</h1>
        <p className="text-gray-500 text-sm">{description}</p>
      </div>
      {action}
    </div>
  );
}

export function Badge({
  children,
  ton,
}: {
  children: ReactNode;
  ton: "attente" | "succes" | "danger" | "neutre" | "info";
}) {
  const classes = {
    attente: "bg-amber-100 text-amber-700",
    succes: "bg-green-100 text-green-700",
    danger: "bg-red-100 text-red-700",
    neutre: "bg-immo-dark/5 text-gray-700",
    info: "bg-blue-100 text-blue-700",
  }[ton];
  return (
    <span className={`text-xs font-medium px-2.5 py-1 rounded-full whitespace-nowrap ${classes}`}>{children}</span>
  );
}

export function MessageErreur({ children }: { children: ReactNode }) {
  return (
    <p className="flex items-center gap-2 text-red-600 text-sm">
      <AlertCircle size={16} className="shrink-0" />
      {children}
    </p>
  );
}

/** Affiche le chargement, l'erreur ou l'état vide ; sinon rend `children`. */
export function EtatListe({
  chargement,
  erreur,
  vide,
  messageVide,
  children,
}: {
  chargement: boolean;
  erreur: string | null;
  vide: boolean;
  messageVide: string;
  children: ReactNode;
}) {
  if (chargement) return <p className="text-gray-500 text-sm">Chargement...</p>;
  if (erreur) return <MessageErreur>{erreur}</MessageErreur>;
  if (vide) {
    return (
      <div className="bg-white rounded-2xl border border-black/5 shadow-sm py-14 flex flex-col items-center gap-2 text-gray-500">
        <Inbox size={28} />
        <p className="text-sm">{messageVide}</p>
      </div>
    );
  }
  return <>{children}</>;
}

export function Tableau({ entetes, children }: { entetes: string[]; children: ReactNode }) {
  return (
    <div className="bg-white rounded-2xl border border-black/5 shadow-sm overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-gray-500 border-b border-black/5">
            {entetes.map((e, i) => (
              <th key={i} className="px-5 py-3 font-medium whitespace-nowrap">
                {e}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

export function Onglets<T extends string>({
  valeur,
  options,
  onChange,
}: {
  valeur: T;
  /** pastille : nombre affiché en rouge à côté du libellé (ex. messages non lus), masqué si 0. */
  options: { valeur: T; label: string; pastille?: number; pastilleTitre?: string }[];
  onChange: (v: T) => void;
}) {
  return (
    <div className="inline-flex flex-wrap gap-1 mb-6 bg-white border border-black/5 rounded-xl p-1 shadow-sm">
      {options.map((o) => (
        <button
          key={o.valeur}
          type="button"
          onClick={() => onChange(o.valeur)}
          aria-pressed={valeur === o.valeur}
          className={`inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm font-semibold transition-colors ${
            valeur === o.valeur ? "bg-immo-dark text-white" : "text-gray-600 hover:text-immo-dark"
          }`}
        >
          {o.label}
          {!!o.pastille && (
            <span
              className="min-w-[18px] h-[18px] px-1 rounded-full bg-red-600 text-white text-[10px] font-bold leading-none inline-flex items-center justify-center"
              title={
                o.pastilleTitre ??
                `${o.pastille} message${o.pastille > 1 ? "s" : ""} non lu${o.pastille > 1 ? "s" : ""}`
              }
            >
              {o.pastille > 9 ? "9+" : o.pastille}
              <span className="sr-only">
                {" "}
                {o.pastilleTitre ?? `message${o.pastille > 1 ? "s" : ""} non lu${o.pastille > 1 ? "s" : ""}`}
              </span>
            </span>
          )}
        </button>
      ))}
    </div>
  );
}
