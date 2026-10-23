// Test de charge ImmoPlus (k6) — contre le profil « echelle » branché sur la base JETABLE immoplus_charge.
// Lancement : infra/charge/lancer-charge.sh <utilisateurs virtuels> (voir README, section « Tests de charge »).
//
// Population simulée (proportions de VUS) :
//   visiteur  70 % : recherche d'annonces (filtres séjour court / longue durée), carte, fiche, avis, miniatures ;
//   locataire 20 % : ses réservations, non-lus, fil de messages, envoi d'un message (1 fois sur 4), favoris ;
//   hote       8 % : réservations reçues, tableau de bord, ses logements, candidatures ;
//   admin      2 % : listes paginées (utilisateurs, réservations, logements, locations, financements) avec recherche q ;
//   temps_reel   : connexions SignalR ouvertes (WebSocket) pour la moitié des locataires et hôtes simulés.
// Chaque utilisateur virtuel marque une pause de 1 à 4 s entre deux pages (« temps de lecture »).
// Les comptes sont tous en @test-charge.local (mot de passe connu de la base de charge) ; connexions faites UNE fois dans
// setup() et jetons réutilisés : la limitation des tentatives (échecs seulement) n'est jamais déclenchée.

import http from 'k6/http';
import ws from 'k6/ws';
import { check, sleep, fail } from 'k6';
import exec from 'k6/execution';
import { Counter } from 'k6/metrics';

// Réponses en erreur par famille (phase de test uniquement) : 503 = base saturée (Retry-After), autres 5xx, 4xx, 0 = délai/connexion.
const REPONSES_503 = new Counter('reponses_503');
const REPONSES_5XX = new Counter('reponses_5xx');
const REPONSES_4XX = new Counter('reponses_4xx');
const REPONSES_SANS_STATUT = new Counter('reponses_sans_statut');
function compterStatut(r) {
  if (!r) return;
  if (r.status === 0) REPONSES_SANS_STATUT.add(1);
  else if (r.status === 503) { REPONSES_503.add(1); REPONSES_5XX.add(1); }
  else if (r.status >= 500) REPONSES_5XX.add(1);
  else if (r.status >= 400) REPONSES_4XX.add(1);
}

const BASE = __ENV.BASE_URL || 'http://nginx-echelle/api';
const VUS = parseInt(__ENV.VUS || '100', 10);
const MONTEE = __ENV.MONTEE || '1m';
const DUREE = __ENV.DUREE || '3m';
const MOT_DE_PASSE = __ENV.MOT_DE_PASSE || 'ChargeImmo!2026';
const COMPTES = JSON.parse(open(__ENV.COMPTES || '/resultats/comptes.json'));

// Garde-fou : uniquement des comptes de la base de charge.
for (const e of [...COMPTES.locataires.map((l) => l.email), ...COMPTES.hotes, COMPTES.admin]) {
  if (!e.endsWith('@test-charge.local')) {
    throw new Error(`Compte hors base de charge refusé : ${e}`);
  }
}

const PARTS = { visiteur: 0.7, locataire: 0.2, hote: 0.08, admin: 0.02 };
const nb = (profil) => Math.max(1, Math.round(VUS * PARTS[profil]));
const NB_TEMPS_REEL = Math.max(1, Math.round((nb('locataire') + nb('hote')) / 2));

function scenario(fonction, cible) {
  return {
    executor: 'ramping-vus',
    exec: fonction,
    startVUs: 0,
    stages: [
      { duration: MONTEE, target: cible },
      { duration: DUREE, target: cible },
      { duration: '20s', target: 0 },
    ],
    gracefulRampDown: '15s',
    // Distingue les requêtes du test de celles de setup() (connexions initiales, exclues des résultats « phase:test »).
    tags: { phase: 'test' },
  };
}

// Routes suivies une à une dans le résumé (seuil « max >= 0 » toujours vrai : il sert seulement à afficher la ligne).
const ROUTES = [
  'villes', 'annonces_sejour', 'annonces_longue', 'carte', 'fiche', 'avis', 'calendrier', 'photo_miniature', 'photo',
  'reservations_locataire', 'non_lus', 'messages', 'message_envoi', 'favoris', 'reservation_detail',
  'reservations_recues', 'tableau_bord_hote', 'logements_hote', 'candidatures_hote',
  'admin_utilisateurs', 'admin_reservations', 'admin_logements', 'admin_locations', 'admin_financements', 'admin_tableau_bord',
  'signalr_negociation',
];
const seuils = {
  'http_req_failed{phase:test}': ['rate<0.01'],
  'http_req_duration{phase:test}': ['p(95)<500'],
  'http_reqs{phase:test}': ['count>=0'],
  'reponses_503{phase:test}': ['count>=0'],
  'reponses_5xx{phase:test}': ['count>=0'],
  'reponses_4xx{phase:test}': ['count>=0'],
  'reponses_sans_statut{phase:test}': ['count>=0'],
  'http_req_duration{scenario:visiteur}': ['p(95)<500'],
  'http_req_duration{scenario:locataire}': ['p(95)<500'],
  'http_req_duration{scenario:hote}': ['p(95)<500'],
  'http_req_duration{scenario:admin}': ['p(95)<800'],
  'http_req_failed{scenario:visiteur}': ['rate<0.01'],
  'http_req_failed{scenario:locataire}': ['rate<0.01'],
  'http_req_failed{scenario:hote}': ['rate<0.01'],
  'http_req_failed{scenario:admin}': ['rate<0.01'],
  checks: ['rate>0.99'],
};
for (const r of ROUTES) {
  seuils[`http_req_duration{nom:${r}}`] = ['max>=0'];
}

export const options = {
  scenarios: {
    visiteur: scenario('visiteur', nb('visiteur')),
    locataire: scenario('locataire', nb('locataire')),
    hote: scenario('hote', nb('hote')),
    admin: scenario('admin', nb('admin')),
    temps_reel: scenario('tempsReel', NB_TEMPS_REEL),
  },
  thresholds: seuils,
  setupTimeout: '5m',
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
  // 304 (photo déjà en cache) est un succès ; tout le reste au-delà de 399 compte comme erreur.
  // (http.expectedStatuses est appliqué ci-dessous à chaque requête via http.setResponseCallback.)
  userAgent: 'ImmoPlus-k6-charge',
  // Corps des réponses ignorés sauf quand le scénario en a besoin (responseType: 'text') : mémoire de k6 divisée.
  discardResponseBodies: true,
  noConnectionReuse: false,
};

http.setResponseCallback(http.expectedStatuses({ min: 200, max: 399 }));

const JSON_ENTETES = { 'Content-Type': 'application/json' };
const auth = (jeton) => ({ Authorization: `Bearer ${jeton}` });
const hasard = (tableau) => tableau[Math.floor(Math.random() * tableau.length)];
const entre = (min, max) => min + Math.floor(Math.random() * (max - min + 1));
const pause = () => sleep(1 + Math.random() * 3);

const TEXTE = { corps: true };

function get(chemin, nom, jeton, options = {}) {
  const r = http.get(`${BASE}${chemin}`, {
    headers: jeton ? auth(jeton) : {}, tags: { nom }, responseType: options.corps ? 'text' : 'none',
  });
  compterStatut(r);
  check(r, { [`${nom} 2xx/304`]: (x) => (x.status >= 200 && x.status < 300) || x.status === 304 });
  return r;
}

function lireJson(r) {
  try {
    return r.json();
  } catch (_) {
    return null;
  }
}

function connexion(email) {
  const r = http.post(`${BASE}/utilisateur/connexion`, JSON.stringify({ email, motDePasse: MOT_DE_PASSE }),
    { headers: JSON_ENTETES, tags: { nom: 'connexion' }, responseType: 'text' });
  const d = lireJson(r);
  if (r.status !== 200 || !d || !d.token) {
    fail(`Connexion impossible pour ${email} (statut ${r.status})`);
  }
  return { id: d.id, jeton: d.token };
}

export function setup() {
  const villes = lireJson(http.get(`${BASE}/appartement/villes`, { responseType: 'text' })) || [];
  if (villes.length === 0) {
    fail('Aucune ville : la base de charge est-elle remplie ?');
  }
  const locataires = COMPTES.locataires.map((l) => ({ ...connexion(l.email), reservations: l.reservations }));
  const hotes = COMPTES.hotes.map((e) => connexion(e));
  const admin = connexion(COMPTES.admin);
  // Quelques n° d'annonces récentes pour les fiches (en plus de celles trouvées par la recherche).
  const liste = lireJson(http.get(`${BASE}/appartement/lister?taille=100`, { responseType: 'text' }));
  const annonces = (liste && liste.donnees ? liste.donnees : []).map((a) => a.id);
  return { villes, locataires, hotes, admin, annonces };
}

// Centre de chaque ville (moyenne des coordonnées des annonces, calculée par lancer-charge.sh) pour la vue carte.
const CENTRES = COMPTES.centres || {};

function dateDans(jours) {
  const d = new Date(Date.now() + jours * 86400000);
  return d.toISOString().slice(0, 10);
}

function voirAnnonces(r, jeton) {
  const d = lireJson(r);
  const annonces = d && d.donnees ? d.donnees : [];
  // Le navigateur charge les miniatures visibles (chargement différé) : les 4 premières.
  for (const a of annonces.slice(0, 4)) {
    if (a.photos && a.photos.length > 0) {
      get(`/${a.photos[0].urlMiniature}`, 'photo_miniature', jeton);
    }
  }
  return annonces;
}

function consulterFiche(id, jeton, sejour) {
  const r = get(`/appartement/${id}`, 'fiche', jeton, TEXTE);
  const a = lireJson(r);
  if (a && a.photos && a.photos.length > 0) {
    get(`/${a.photos[0].url}`, 'photo', jeton);
  }
  get(`/appartement/${id}/avis?taille=5`, 'avis', jeton);
  if (sejour) {
    get(`/appartement/${id}/calendrier?du=${dateDans(0)}&au=${dateDans(90)}`, 'calendrier', jeton);
  }
}

export function visiteur(data) {
  const v = hasard(data.villes);
  get('/appartement/villes', 'villes');
  pause();

  const sejour = Math.random() < 0.6;
  let chemin;
  if (sejour) {
    chemin = `/appartement/lister?typeOffre=SejourCourt&disponible=true&ville=${encodeURIComponent(v.ville)}`;
    if (Math.random() < 0.5) {
      const debut = entre(3, 60);
      chemin += `&arrivee=${dateDans(debut)}&depart=${dateDans(debut + entre(2, 7))}&voyageurs=${entre(1, 4)}`;
    }
    if (Math.random() < 0.3 && v.quartiers && v.quartiers.length > 0) {
      chemin += `&quartier=${encodeURIComponent(hasard(v.quartiers))}`;
    }
  } else {
    chemin = `/appartement/lister?typeOffre=LongueDuree&disponible=true&ville=${encodeURIComponent(v.ville)}`;
    if (Math.random() < 0.5) {
      chemin += `&loyerMax=${entre(15, 60) * 100000}&chambresMin=${entre(1, 3)}`;
    }
  }
  const annonces = voirAnnonces(get(chemin, sejour ? 'annonces_sejour' : 'annonces_longue', null, TEXTE));
  pause();

  // Page 2 une fois sur trois.
  if (Math.random() < 0.33) {
    voirAnnonces(get(`${chemin}&page=2`, sejour ? 'annonces_sejour' : 'annonces_longue', null, TEXTE));
    pause();
  }

  // Vue carte une fois sur trois : zone d'environ 10 km autour du centre de la ville.
  if (Math.random() < 0.33) {
    const [lat, lng] = CENTRES[v.ville] || [9.54, -13.68];
    const dl = 0.03 + Math.random() * 0.07;
    get(`/appartement/lister?typeOffre=${sejour ? 'SejourCourt' : 'LongueDuree'}&nord=${lat + dl}&sud=${lat - dl}&est=${lng + dl}&ouest=${lng - dl}&taille=200`, 'carte');
    pause();
  }

  // Une ou deux fiches.
  const ids = annonces.length > 0 ? annonces.map((a) => a.id) : data.annonces;
  for (let i = 0; i < entre(1, 2) && ids.length > 0; i++) {
    consulterFiche(hasard(ids), null, sejour);
    pause();
  }
}

export function locataire(data) {
  const moi = data.locataires[(exec.vu.idInTest - 1) % data.locataires.length];
  const j = moi.jeton;
  const liste = lireJson(get(`/reservation/lister?filtre=${hasard(['Toutes', 'AVenir', 'Actives', 'Passees'])}&taille=20`, 'reservations_locataire', j, TEXTE));
  get('/messages/non-lus', 'non_lus', j);
  pause();

  const ids = moi.reservations.length > 0 ? moi.reservations
    : (liste && liste.donnees ? liste.donnees.map((r) => r.id) : []);
  if (ids.length > 0) {
    const id = hasard(ids);
    get(`/reservation/${id}`, 'reservation_detail', j);
    get(`/reservation/${id}/messages?taille=30`, 'messages', j);
    pause();
    // Écrit un message une fois sur quatre, seulement dans une conversation ouverte (liste préparée).
    if (moi.reservations.length > 0 && Math.random() < 0.25) {
      const r = http.post(`${BASE}/reservation/${id}/messages`,
        JSON.stringify({ contenu: `Bonjour, je confirme mon arrivée en fin de journée. Merci ! (essai de charge ${exec.scenario.iterationInTest})` }),
        { headers: { ...JSON_ENTETES, ...auth(j) }, tags: { nom: 'message_envoi' } });
      compterStatut(r);
      check(r, { 'message_envoi 201': (x) => x.status === 201 });
      pause();
    }
  }

  if (Math.random() < 0.3) {
    get('/favoris?taille=20', 'favoris', j);
    pause();
  }
  // Retour sur une annonce (fiche connectée).
  if (Math.random() < 0.3 && data.annonces.length > 0) {
    consulterFiche(hasard(data.annonces), j, true);
    pause();
  }
}

export function hote(data) {
  const moi = data.hotes[(exec.vu.idInTest - 1) % data.hotes.length];
  const j = moi.jeton;
  get(`/reservation/lister?filtre=${hasard(['Demandes', 'AVenir', 'Actives', 'Toutes'])}&taille=20`, 'reservations_recues', j);
  get('/messages/non-lus', 'non_lus', j);
  pause();
  if (Math.random() < 0.5) {
    get('/bailleur/tableau-de-bord', 'tableau_bord_hote', j);
    pause();
  }
  get(`/appartement/lister?bailleurId=${moi.id}&taille=20`, 'logements_hote', j);
  pause();
  if (Math.random() < 0.3) {
    get('/candidature/lister?taille=20', 'candidatures_hote', j);
    pause();
  }
}

export function admin(data) {
  const j = data.admin.jeton;
  const q = hasard(['locataire1', 'bailleur2', 'Diallo', 'Camara', 'Kaloum', '12', '45']);
  get('/admin/tableau-de-bord', 'admin_tableau_bord', j);
  pause();
  get(`/utilisateur/lister?page=${entre(1, 50)}&taille=20`, 'admin_utilisateurs', j);
  get(`/utilisateur/lister?q=${encodeURIComponent(q)}&taille=20`, 'admin_utilisateurs', j);
  pause();
  get(`/reservation/lister?filtre=Toutes&page=${entre(1, 100)}&taille=20`, 'admin_reservations', j);
  get(`/reservation/lister?filtre=Toutes&q=${encodeURIComponent(q)}&taille=20`, 'admin_reservations', j);
  pause();
  get(`/appartement/lister?q=${encodeURIComponent(q)}&taille=20`, 'admin_logements', j);
  get(`/location/lister?page=${entre(1, 10)}&taille=20`, 'admin_locations', j);
  get(`/financement/lister?taille=20&q=${encodeURIComponent(q)}`, 'admin_financements', j);
  pause();
}

// Connexion temps réel SignalR (protocole JSON) : négociation par nginx, puis WebSocket gardé ouvert ~1 min, ping toutes
// les 15 s comme le client navigateur. Les messages envoyés par les locataires arrivent ici (« nouveauMessage »).
export function tempsReel(data) {
  const idx = exec.vu.idInTest - 1;
  const comptes = [...data.locataires, ...data.hotes];
  const moi = comptes[idx % comptes.length];
  const n = http.post(`${BASE}/hubs/messages/negotiate?negotiateVersion=1`, null,
    { headers: auth(moi.jeton), tags: { nom: 'signalr_negociation' }, responseType: 'text' });
  compterStatut(n);
  const d = lireJson(n);
  if (n.status !== 200 || !d || !d.connectionToken) {
    check(n, { 'signalr_negociation 200': () => false });
    sleep(5);
    return;
  }
  const url = `${BASE.replace(/^http/, 'ws')}/hubs/messages?id=${d.connectionToken}&access_token=${moi.jeton}`;
  const SEP = String.fromCharCode(0x1e);
  const res = ws.connect(url, { tags: { nom: 'signalr_ws' } }, (socket) => {
    socket.on('open', () => {
      socket.send(`{"protocol":"json","version":1}${SEP}`);
      socket.setInterval(() => socket.send(`{"type":6}${SEP}`), 15000);
      socket.setTimeout(() => socket.close(), 45000 + Math.random() * 30000);
    });
  });
  check(res, { 'signalr_ws 101': (r) => r && r.status === 101 });
}

export function handleSummary(data) {
  const sortie = __ENV.SORTIE || `/resultats/resume-${VUS}vu.json`;
  return { [sortie]: JSON.stringify(data, null, 1), stdout: texteResume(data) };
}

function texteResume(data) {
  const m = data.metrics;
  const f = (x) => (x === undefined ? '-' : x.toFixed(1));
  const lignes = [`\nPalier ${VUS} utilisateurs virtuels`];
  const d = m['http_req_duration{phase:test}'].values;
  const n = m['http_reqs{phase:test}'].values;
  lignes.push(`requêtes (hors setup) : ${n.count} (${n.rate.toFixed(1)}/s) ; échecs : ${(m['http_req_failed{phase:test}'].values.rate * 100).toFixed(2)} %`);
  const c = (k) => (m[`${k}{phase:test}`] ? m[`${k}{phase:test}`].values.count : 0);
  lignes.push(`erreurs : 503 ${c('reponses_503')} ; 5xx (503 compris) ${c('reponses_5xx')} ; 4xx ${c('reponses_4xx')} ; sans réponse (délai) ${c('reponses_sans_statut')}`);
  lignes.push(`durée (ms) p50 ${f(d.med)} p95 ${f(d['p(95)'])} p99 ${f(d['p(99)'])} max ${f(d.max)}`);
  for (const k of Object.keys(m).filter((x) => x.startsWith('http_req_duration{') && !x.includes('expected_response') && !x.includes('phase:')).sort()) {
    const v = m[k].values;
    lignes.push(`  ${k.slice(18, -1).padEnd(28)} n=${String(v.count).padStart(7)} p50 ${f(v.med).padStart(7)} p95 ${f(v['p(95)']).padStart(7)} p99 ${f(v['p(99)']).padStart(7)}`);
  }
  return lignes.join('\n') + '\n';
}
