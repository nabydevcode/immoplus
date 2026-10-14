// Par défaut, l'API est jointe via le proxy du serveur Vite (/api) : fonctionne en local comme via un tunnel.
export const API_URL = import.meta.env.VITE_API_URL ?? "/api";

export class ApiError extends Error {
  /** Code HTTP de la réponse refusée (404, 403…). */
  readonly statut: number | null;
  constructor(message: string, statut: number | null = null) {
    super(message);
    this.statut = statut;
  }
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = localStorage.getItem("authToken");
  const headers = new Headers(options.headers);
  // Pour un envoi de fichiers (FormData), le navigateur fixe lui-même le Content-Type multipart.
  if (!(options.body instanceof FormData)) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const reponse = await fetch(`${API_URL}/${path}`, { ...options, headers });

  if (!reponse.ok) {
    let message = `Erreur ${reponse.status}`;
    try {
      const corps = await reponse.json();
      message = corps?.message ?? message;
    } catch {
      // réponse sans corps JSON exploitable
    }
    // Session refusée alors qu'on était connecté (compte désactivé par un admin, jeton expiré) :
    // on efface la session et on renvoie vers la connexion avec l'explication.
    if (reponse.status === 401 && token && !path.startsWith("utilisateur/connexion")) {
      for (const cle of ["authToken", "authId", "authNom", "authRole"]) localStorage.removeItem(cle);
      const raison = message.includes("désactivé") ? "desactive" : "session";
      const retour = encodeURIComponent(window.location.pathname + window.location.search);
      window.location.assign(`/login?raison=${raison}&retour=${retour}`);
    }
    throw new ApiError(message, reponse.status);
  }

  if (reponse.status === 204) return undefined as T;
  // Une réponse 200/201 peut aussi être vide (ex. action sans contenu à renvoyer).
  const texte = await reponse.text();
  return (texte ? JSON.parse(texte) : undefined) as T;
}

export const apiClient = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "POST", body: body ? JSON.stringify(body) : undefined }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "PUT", body: body ? JSON.stringify(body) : undefined }),
  delete: <T>(path: string) => request<T>(path, { method: "DELETE" }),
  postFormulaire: <T>(path: string, formulaire: FormData) => request<T>(path, { method: "POST", body: formulaire }),
};

/** Télécharge un fichier protégé (le lien direct ne porterait pas le jeton d'authentification). */
export async function telechargerFichier(path: string, nomFichier: string) {
  const token = localStorage.getItem("authToken");
  const reponse = await fetch(`${API_URL}/${path}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });
  if (!reponse.ok) {
    // Refus expliqué par l'API (ex. 409 « quittance disponible une fois le loyer entièrement payé »).
    let message = `Erreur ${reponse.status}`;
    try {
      const corps = await reponse.json();
      message = corps?.message ?? message;
    } catch {
      // réponse sans corps JSON exploitable
    }
    throw new ApiError(message, reponse.status);
  }

  const url = URL.createObjectURL(await reponse.blob());
  const lien = document.createElement("a");
  lien.href = url;
  lien.download = nomFichier;
  document.body.appendChild(lien);
  lien.click();
  lien.remove();
  // Libération différée : certains navigateurs lisent l'URL après le clic.
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** URL complète d'une photo d'annonce (servie publiquement par l'API). */
export const urlPhoto = (photo: { url: string }) => `${API_URL}/${photo.url}`;

/** URL complète d'une route de l'API (chemin relatif « a/b » ou « /a/b », ou URL déjà absolue). */
export const urlApi = (chemin: string) =>
  /^https?:\/\//.test(chemin) ? chemin : `${API_URL}/${chemin.replace(/^\//, "")}`;

/** Charge un fichier protégé (photo d'état des lieux) et renvoie une URL locale à libérer avec URL.revokeObjectURL. */
export async function chargerFichierProtege(chemin: string) {
  const token = localStorage.getItem("authToken");
  const reponse = await fetch(urlApi(chemin), { headers: token ? { Authorization: `Bearer ${token}` } : undefined });
  if (!reponse.ok) throw new ApiError(`Erreur ${reponse.status}`, reponse.status);
  return URL.createObjectURL(await reponse.blob());
}

/** Comme chargerFichierProtege, en renvoyant aussi le type du fichier (image ou PDF : aperçu adapté). */
export async function chargerFichierProtegeAvecType(chemin: string) {
  const token = localStorage.getItem("authToken");
  const reponse = await fetch(urlApi(chemin), { headers: token ? { Authorization: `Bearer ${token}` } : undefined });
  if (!reponse.ok) throw new ApiError(`Erreur ${reponse.status}`, reponse.status);
  const blob = await reponse.blob();
  return { url: URL.createObjectURL(blob), type: blob.type };
}
