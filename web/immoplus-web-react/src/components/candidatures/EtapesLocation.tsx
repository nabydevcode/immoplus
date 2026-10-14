const etapes = [
  { titre: "Candidature", detail: "Dossier, apport prévu, garant, justificatifs" },
  { titre: "Étude du dossier", detail: "Par le bailleur" },
  { titre: "Visite de l'appartement", detail: "3 candidats retenus maximum" },
  { titre: "Attribution", detail: "Le bailleur choisit après les visites" },
  { titre: "Paiement de l'apport", detail: "Seulement si le logement vous est attribué" },
  { titre: "Remise des clés", detail: "Début de la location" },
];

/** Étapes d'une location longue durée, présentées au candidat avant qu'il postule. */
export default function EtapesLocation() {
  return (
    <ol className="space-y-2.5">
      {etapes.map((e, i) => (
        <li key={e.titre} className="flex gap-3 text-sm">
          <span className="flex items-center justify-center w-6 h-6 rounded-full bg-immo-gold/15 text-immo-dark text-xs font-bold shrink-0">
            {i + 1}
          </span>
          <span>
            <span className="font-semibold">{e.titre}</span>
            <span className="block text-xs text-gray-500">{e.detail}</span>
          </span>
        </li>
      ))}
    </ol>
  );
}
