import { useState, type FormEvent, type ReactNode } from "react";
import { Link, useParams } from "react-router-dom";
import { AlertCircle, ArrowLeft, CheckCircle2, FileUp, MapPin, Plus, Trash2 } from "lucide-react";
import { apiClient, ApiError } from "../api/client";
import { useAuth } from "../context/auth";
import { useAppartement } from "../hooks/useAppartement";
import EtapesLocation from "../components/candidatures/EtapesLocation";
import { referenceDossier } from "../components/candidatures/analyse";
import { useLienConnexion } from "../hooks/useLienConnexion";

const situations = [
  { valeur: "CDI", label: "CDI" },
  { valeur: "CDD", label: "CDD" },
  { valeur: "Independant", label: "Indépendant" },
  { valeur: "DebutActiviteRevenuFixe", label: "Début d'activité, revenu fixe" },
  { valeur: "Autre", label: "Autre" },
];

const typesDocument = [
  { valeur: "PieceIdentite", label: "Pièce d'identité" },
  { valeur: "JustificatifRevenu", label: "Justificatif de revenus" },
  { valeur: "JustificatifDomicile", label: "Justificatif de domicile" },
  { valeur: "Autre", label: "Autre" },
];

const TAILLE_MAX = 10 * 1024 * 1024;
const EXTENSIONS = [".pdf", ".jpg", ".jpeg", ".png"];

const classeChamp =
  "w-full border border-gray-300 rounded-lg px-4 py-3 mt-1.5 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-immo-gold/40 focus:border-immo-gold transition-shadow";

interface PieceJointe {
  cle: number;
  typeDocument: string;
  proprietaire: "Locataire" | "Garant";
  fichier: File | null;
}

const montant = (n: number) => `${Math.round(n).toLocaleString("fr-FR")} GNF`;

function Message({ children, retour }: { children: ReactNode; retour?: { lien: string; libelle: string } }) {
  return (
    <div className="px-6 py-16 max-w-xl mx-auto text-center text-gray-600">
      <p>{children}</p>
      {retour && (
        <Link
          to={retour.lien}
          className="inline-flex items-center gap-2 mt-4 text-immo-dark font-semibold text-sm hover:underline"
        >
          <ArrowLeft size={16} />
          {retour.libelle}
        </Link>
      )}
    </div>
  );
}

export default function Candidater() {
  const { type = "longue-duree", id } = useParams<{ type: string; id: string }>();
  const { estConnecte, roleUtilisateur } = useAuth();
  const lienConnexion = useLienConnexion();
  const { appartement, chargement, erreur: erreurChargement } = useAppartement(id);

  const [apport, setApport] = useState("");
  const [situation, setSituation] = useState("CDI");
  const [avecGarant, setAvecGarant] = useState(false);
  const [garant, setGarant] = useState({ nom: "", telephone: "", email: "", situationProfessionnelle: "CDI" });
  const [pieces, setPieces] = useState<PieceJointe[]>([
    { cle: 1, typeDocument: "PieceIdentite", proprietaire: "Locataire", fichier: null },
  ]);
  const [erreur, setErreur] = useState<string | null>(null);
  const [enCours, setEnCours] = useState(false);
  const [resultat, setResultat] = useState<{ id: number; envoyes: string[]; echecs: string[] } | null>(null);

  if (chargement) return <Message>Chargement...</Message>;
  if (erreurChargement || !appartement)
    return (
      <Message retour={{ lien: `/annonces/${type}`, libelle: "Retour aux annonces" }}>
        {erreurChargement ?? "Annonce introuvable."}
      </Message>
    );

  const retour = `/annonces/${type}/${id}`;
  const retourAnnonce = { lien: retour, libelle: "Retour à l'annonce" };
  if (appartement.typeOffre !== "LongueDuree") {
    return (
      <Message>
        Ce logement est un séjour court :{" "}
        <Link to={`/annonces/sejour-court/${id}/reserver`} className="text-immo-gold font-semibold">
          réservez-le directement
        </Link>
        .
      </Message>
    );
  }
  if (!estConnecte) {
    return (
      <Message>
        <Link to={lienConnexion} className="text-immo-gold font-semibold">
          Connectez-vous
        </Link>{" "}
        avec un compte Locataire pour candidater.
      </Message>
    );
  }
  if (roleUtilisateur !== "Locataire")
    return <Message retour={retourAnnonce}>Seul un compte Locataire peut déposer une candidature.</Message>;
  if (!appartement.disponible || appartement.estDesactive)
    return <Message retour={retourAnnonce}>Cet appartement n'est plus disponible.</Message>;

  const loyer = appartement.loyerMensuel ?? 0;
  const dureeAvance = appartement.dureeAvanceExigee ?? 0;
  const avanceExigee = loyer * dureeAvance;
  const apportMinimum = Math.min(loyer * 2, avanceExigee);
  const apportSaisi = Number(apport) || 0;
  const montantFinance = Math.max(avanceExigee - apportSaisi, 0);
  const nombreMensualites = Math.max(dureeAvance - Math.floor(apportSaisi / (loyer || 1)), 0);

  const modifierPiece = (cle: number, modif: Partial<PieceJointe>) =>
    setPieces((liste) => liste.map((p) => (p.cle === cle ? { ...p, ...modif } : p)));

  const choisirFichier = (cle: number, fichier: File | null) => {
    setErreur(null);
    if (fichier) {
      const extension = fichier.name.slice(fichier.name.lastIndexOf(".")).toLowerCase();
      if (!EXTENSIONS.includes(extension)) {
        setErreur(`${fichier.name} : formats acceptés PDF, JPG, PNG.`);
        return;
      }
      if (fichier.size > TAILLE_MAX) {
        setErreur(`${fichier.name} dépasse 10 Mo.`);
        return;
      }
    }
    modifierPiece(cle, { fichier });
  };

  const soumettre = async (e: FormEvent) => {
    e.preventDefault();
    setErreur(null);

    if (apportSaisi < apportMinimum) {
      setErreur(`L'apport initial doit être d'au moins ${montant(apportMinimum)} (2 mois de loyer).`);
      return;
    }
    if (avecGarant && (!garant.nom.trim() || !garant.telephone.trim())) {
      setErreur("Renseignez au moins le nom et le téléphone du garant.");
      return;
    }

    setEnCours(true);
    try {
      const candidature = await apiClient.post<{ id: number }>("candidature/creer", {
        appartementId: appartement.id,
        apportInitial: apportSaisi,
        situationProfessionnelle: situation,
        garant: avecGarant
          ? {
              ...garant,
              nom: garant.nom.trim(),
              telephone: garant.telephone.trim(),
              email: garant.email.trim() || null,
            }
          : null,
      });

      // Le dossier est créé : on envoie ensuite les justificatifs un par un.
      const envoyes: string[] = [];
      const echecs: string[] = [];
      for (const piece of pieces) {
        if (!piece.fichier) continue;
        const formulaire = new FormData();
        formulaire.append("fichier", piece.fichier);
        formulaire.append("typeDocument", piece.typeDocument);
        formulaire.append("proprietaire", avecGarant ? piece.proprietaire : "Locataire");
        try {
          await apiClient.postFormulaire(`candidature/${candidature.id}/documents`, formulaire);
          envoyes.push(piece.fichier.name);
        } catch (err) {
          echecs.push(`${piece.fichier.name}${err instanceof ApiError ? ` (${err.message})` : ""}`);
        }
      }
      setResultat({ id: candidature.id, envoyes, echecs });
    } catch (err) {
      setErreur(err instanceof ApiError ? err.message : "Impossible d'envoyer la candidature. Réessayez plus tard.");
    } finally {
      setEnCours(false);
    }
  };

  if (resultat) {
    return (
      <div className="px-6 py-16 max-w-xl mx-auto">
        <div className="bg-white rounded-2xl shadow-sm border border-black/5 p-8 flex flex-col items-center text-center gap-4">
          <span className="flex items-center justify-center w-14 h-14 rounded-full bg-green-50 text-green-600">
            <CheckCircle2 size={28} />
          </span>
          <h1 className="text-2xl font-extrabold">Candidature envoyée</h1>
          <p className="text-gray-500 text-sm">
            Votre dossier n° {referenceDossier(resultat.id)} pour{" "}
            <strong className="text-gray-700">{appartement.adresse}</strong> a bien été transmis au bailleur. Un email
            de confirmation vient de vous être envoyé, et vous serez prévenu par email à chaque étape.
          </p>
          {resultat.envoyes.length > 0 && (
            <p className="text-sm text-gray-600">{resultat.envoyes.length} justificatif(s) joint(s).</p>
          )}
          {resultat.echecs.length > 0 && (
            <div className="text-sm text-red-600 text-left w-full bg-red-50 rounded-lg p-3">
              <p className="font-semibold mb-1">Ces fichiers n'ont pas pu être envoyés :</p>
              <ul className="list-disc pl-5">
                {resultat.echecs.map((e) => (
                  <li key={e}>{e}</li>
                ))}
              </ul>
            </div>
          )}
          <Link
            to="/mes-candidatures"
            className="w-full text-center bg-immo-gold text-immo-dark font-semibold py-3 rounded-lg hover:bg-immo-gold-light transition-colors mt-2"
          >
            Suivre mon dossier
          </Link>
          <Link to="/annonces/longue-duree" className="text-sm font-semibold text-gray-600 hover:text-immo-dark">
            Voir d'autres annonces
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="px-6 py-10">
      <div className="max-w-6xl mx-auto">
        <Link
          to={retour}
          className="inline-flex items-center gap-2 text-gray-600 text-sm font-medium hover:text-immo-dark mb-6"
        >
          <ArrowLeft size={16} />
          Retour à l'annonce
        </Link>

        <h1 className="text-2xl md:text-3xl font-extrabold mb-1">Déposer ma candidature</h1>
        <p className="text-gray-500 text-sm mb-8 flex items-center gap-1.5">
          <MapPin size={14} />
          {appartement.adresse}
        </p>

        <div className="grid lg:grid-cols-3 gap-8">
          <form onSubmit={soumettre} className="lg:col-span-2 flex flex-col gap-6">
            <section className="bg-white rounded-2xl shadow-sm border border-black/5 p-6 flex flex-col gap-4">
              <h2 className="font-bold">Ma situation</h2>
              <label className="text-sm font-medium text-gray-700">
                Apport que vous verserez (GNF)
                <input
                  type="number"
                  min={0}
                  value={apport}
                  onChange={(e) => setApport(e.target.value)}
                  placeholder={String(apportMinimum)}
                  className={classeChamp}
                />
                <span className="block text-xs text-gray-500 mt-1.5 font-normal">
                  Montant que vous vous engagez à verser si le logement vous est attribué, après la visite (minimum{" "}
                  {montant(apportMinimum)}). Rien n'est à payer maintenant. Le reste de l'avance peut être financé et
                  remboursé en mensualités.
                </span>
              </label>
              <label className="text-sm font-medium text-gray-700">
                Situation professionnelle
                <select value={situation} onChange={(e) => setSituation(e.target.value)} className={classeChamp}>
                  {situations.map((s) => (
                    <option key={s.valeur} value={s.valeur}>
                      {s.label}
                    </option>
                  ))}
                </select>
              </label>
            </section>

            <section className="bg-white rounded-2xl shadow-sm border border-black/5 p-6 flex flex-col gap-4">
              <label className="flex items-center gap-2 text-sm font-bold cursor-pointer">
                <input
                  type="checkbox"
                  checked={avecGarant}
                  onChange={(e) => setAvecGarant(e.target.checked)}
                  className="accent-immo-gold w-4 h-4"
                />
                Ajouter un garant à mon dossier
              </label>
              {avecGarant && (
                <div className="grid sm:grid-cols-2 gap-4">
                  <label className="text-sm font-medium text-gray-700">
                    Nom du garant
                    <input
                      value={garant.nom}
                      onChange={(e) => setGarant({ ...garant, nom: e.target.value })}
                      className={classeChamp}
                    />
                  </label>
                  <label className="text-sm font-medium text-gray-700">
                    Téléphone
                    <input
                      type="tel"
                      value={garant.telephone}
                      onChange={(e) => setGarant({ ...garant, telephone: e.target.value })}
                      className={classeChamp}
                    />
                  </label>
                  <label className="text-sm font-medium text-gray-700">
                    Email (optionnel)
                    <input
                      type="email"
                      value={garant.email}
                      onChange={(e) => setGarant({ ...garant, email: e.target.value })}
                      className={classeChamp}
                    />
                  </label>
                  <label className="text-sm font-medium text-gray-700">
                    Situation professionnelle
                    <select
                      value={garant.situationProfessionnelle}
                      onChange={(e) => setGarant({ ...garant, situationProfessionnelle: e.target.value })}
                      className={classeChamp}
                    >
                      {situations.map((s) => (
                        <option key={s.valeur} value={s.valeur}>
                          {s.label}
                        </option>
                      ))}
                    </select>
                  </label>
                </div>
              )}
            </section>

            <section className="bg-white rounded-2xl shadow-sm border border-black/5 p-6 flex flex-col gap-4">
              <div>
                <h2 className="font-bold">Justificatifs</h2>
                <p className="text-xs text-gray-500 mt-1">
                  PDF, JPG ou PNG, 10 Mo maximum par fichier. Recommandé pour accélérer l'étude du dossier.
                </p>
              </div>
              {pieces.map((piece) => (
                <div
                  key={piece.cle}
                  className="grid sm:grid-cols-[1fr_1fr_auto] gap-3 items-end border-b border-black/5 pb-4 last:border-0 last:pb-0"
                >
                  <label className="text-sm font-medium text-gray-700">
                    Type
                    <select
                      value={piece.typeDocument}
                      onChange={(e) => modifierPiece(piece.cle, { typeDocument: e.target.value })}
                      className={classeChamp}
                    >
                      {typesDocument.map((t) => (
                        <option key={t.valeur} value={t.valeur}>
                          {t.label}
                        </option>
                      ))}
                    </select>
                  </label>
                  {avecGarant ? (
                    <label className="text-sm font-medium text-gray-700">
                      Concerne
                      <select
                        value={piece.proprietaire}
                        onChange={(e) =>
                          modifierPiece(piece.cle, { proprietaire: e.target.value as PieceJointe["proprietaire"] })
                        }
                        className={classeChamp}
                      >
                        <option value="Locataire">Moi</option>
                        <option value="Garant">Mon garant</option>
                      </select>
                    </label>
                  ) : (
                    <span className="hidden sm:block" />
                  )}
                  <button
                    type="button"
                    onClick={() => setPieces((liste) => liste.filter((p) => p.cle !== piece.cle))}
                    className="text-gray-400 hover:text-red-600 p-3"
                    aria-label="Retirer ce justificatif"
                  >
                    <Trash2 size={18} />
                  </button>
                  <label className="sm:col-span-3 flex items-center gap-3 border border-dashed border-gray-300 rounded-lg px-4 py-3 text-sm cursor-pointer hover:border-immo-gold">
                    <FileUp size={18} className="text-immo-gold shrink-0" />
                    <span className={piece.fichier ? "text-gray-800 truncate" : "text-gray-500"}>
                      {piece.fichier ? piece.fichier.name : "Choisir un fichier"}
                    </span>
                    <input
                      type="file"
                      accept={EXTENSIONS.join(",")}
                      onChange={(e) => choisirFichier(piece.cle, e.target.files?.[0] ?? null)}
                      className="hidden"
                    />
                  </label>
                </div>
              ))}
              <button
                type="button"
                onClick={() =>
                  setPieces((liste) => [
                    ...liste,
                    { cle: Date.now(), typeDocument: "JustificatifRevenu", proprietaire: "Locataire", fichier: null },
                  ])
                }
                className="inline-flex items-center gap-1.5 text-sm font-semibold text-immo-dark hover:text-immo-gold self-start"
              >
                <Plus size={16} />
                Ajouter un justificatif
              </button>
            </section>

            {erreur && (
              <p className="flex items-center gap-2 text-red-600 text-sm">
                <AlertCircle size={16} className="shrink-0" />
                {erreur}
              </p>
            )}

            <button
              type="submit"
              disabled={enCours}
              className="w-full sm:w-auto sm:self-start bg-immo-gold text-immo-dark font-semibold px-8 py-3 rounded-lg hover:bg-immo-gold-light transition-colors disabled:opacity-60"
            >
              {enCours ? "Envoi en cours..." : "Envoyer ma candidature"}
            </button>
          </form>

          <aside className="bg-white rounded-2xl shadow-sm border border-black/5 p-6 h-fit flex flex-col gap-3 text-sm">
            <h2 className="font-bold mb-1">Récapitulatif</h2>
            <div className="flex justify-between">
              <span className="text-gray-500">Loyer mensuel</span>
              <span className="font-semibold">{montant(loyer)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-gray-500">Avance exigée</span>
              <span className="font-semibold">
                {montant(avanceExigee)} <span className="text-gray-400 font-normal">({dureeAvance} mois)</span>
              </span>
            </div>
            <div className="flex justify-between">
              <span className="text-gray-500">Votre apport</span>
              <span className="font-semibold">{montant(apportSaisi)}</span>
            </div>
            <hr className="border-black/10" />
            {apportSaisi >= avanceExigee && apportSaisi > 0 ? (
              <p className="text-green-700">Votre apport couvre toute l'avance : aucun financement nécessaire.</p>
            ) : apportSaisi >= apportMinimum ? (
              <>
                <div className="flex justify-between">
                  <span className="text-gray-500">Montant à financer</span>
                  <span className="font-semibold">{montant(montantFinance)}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-500">Mensualités estimées</span>
                  <span className="font-semibold">
                    {nombreMensualites} × ~{montant((montantFinance / (nombreMensualites || 1)) * 1.03)}
                  </span>
                </div>
                <p className="text-xs text-gray-400">
                  Estimation avec 3 % de frais. Le taux final (2 à 5 %) est fixé à l'acceptation.
                </p>
              </>
            ) : (
              <p className="text-gray-500">
                Saisissez un apport d'au moins {montant(apportMinimum)} pour voir la simulation.
              </p>
            )}
            <div className="border-t border-black/10 pt-4 mt-2">
              <h2 className="font-bold mb-3">Les étapes</h2>
              <EtapesLocation />
            </div>
          </aside>
        </div>
      </div>
    </div>
  );
}
