# ImmoPlus

Plateforme immobilière : location longue durée (candidature/dossier/garant/financement) et séjours courts type Airbnb.

## Structure

- `api/ImmoPlus.Api/` — API ASP.NET Core Minimal API (.NET 10), EF Core + SQLite, JWT + BCrypt, Swagger.
- `api/ImmoPlus.Api.Tests/` — Tests automatisés de l'API de bout en bout (xUnit, voir « Lancer les tests »).
- `web/ImmoPlus.Web/` — Client Blazor WebAssembly + Tailwind CSS v3 (historique).
- `web/immoplus-web-react/` — Nouveau client React + Vite + TypeScript + Tailwind v4 (en cours de migration, local pour l'instant).
- `docs/schema.md` — Schéma du modèle de données.

Mono-repo avec séparation stricte : aucune référence croisée entre `api/` et `web/`. L'API est un service HTTP autonome, réutilisable par n'importe quel client (web actuel, mobile plus tard).

## Démarrer en développement

Deux terminaux en parallèle :

```bash
# Terminal 1 — API (http://localhost:5107, Swagger sur /swagger)
cd api/ImmoPlus.Api
dotnet run --launch-profile http

# Terminal 2 — Client (http://localhost:5290)
cd web/ImmoPlus.Web
npm install            # une seule fois
npm run build:css      # watch Tailwind
# dans un 3e terminal :
dotnet run --launch-profile http
```

Première mise en place de la base de données :

```bash
cd api/ImmoPlus.Api
dotnet tool install --global dotnet-ef   # si pas déjà installé
dotnet ef database update
```

### Avec Docker (API + client React)

Alternative à l'installation locale de .NET/Node : lance l'API et le nouveau client React dans des conteneurs, avec hot-reload (code monté en volume).

```bash
docker compose up
```

- API : http://localhost:5107 (Swagger sur /swagger)
- Client React : http://localhost:5173

Le fichier SQLite (`api/ImmoPlus.Api/immoplus.db`) reste sur la machine hôte (monté en volume), donc les données persistent entre les redémarrages. `docker compose down` arrête et supprime les conteneurs sans toucher à la base.

> ⚠️ Ne jamais modifier `immoplus.db` depuis macOS (sqlite3, DB Browser…) pendant que l'API tourne dans Docker : le mode WAL de SQLite ne se synchronise pas à travers le montage et la base se corrompt. Passer par le conteneur : `docker compose exec api sqlite3 immoplus.db`, ou arrêter l'API (`docker compose stop api`) avant d'ouvrir le fichier.

> Sur macOS, si Docker Desktop n'a pas accès au dossier du projet (erreur `operation not permitted` au démarrage), éviter de placer le repo dans `Desktop`/`Documents`/`Downloads` (protégés par défaut) ou autoriser Docker Desktop via Réglages Système → Confidentialité et sécurité → Accès complet au disque.

Le client Blazor (`web/ImmoPlus.Web`) n'est pas (encore) dockerisé.

### Récupérer un accès admin

Seul un administrateur peut créer un autre administrateur (Admin › Utilisateurs › « Créer un administrateur », route `POST /utilisateur/admin`) : l'inscription publique refuse le rôle Admin dès qu'il existe un admin actif. S'il n'en reste aucun (tous désactivés ou supprimés), l'inscription d'un compte Admin redevient possible et il est validé d'office.

En secours (mot de passe perdu par le dernier admin…), promouvoir un compte existant **depuis le conteneur** :

```bash
docker compose exec -T api sqlite3 immoplus.db "UPDATE Utilisateurs SET Role=0, Statut=1 WHERE Email='prenom.nom@exemple.com';"
```

(`Role=0` = Admin, `Statut=1` = Valide.) Le compte peut ensuite utiliser « Mot de passe oublié ». Ne jamais lancer cette commande avec le `sqlite3` de macOS sur `api/ImmoPlus.Api/immoplus.db` pendant que l'API tourne : la base se corromprait (voir l'avertissement ci-dessus).

### Sauvegardes de la base

L'API sauvegarde la base au démarrage puis toutes les 24 h (`VACUUM INTO` : copie cohérente à chaud, sans arrêter l'API) dans `api/ImmoPlus.Api/Sauvegardes/immoplus-yyyyMMdd-HHmm.db` (heure UTC, dossier exclu de Git). Les 14 plus récentes sont conservées. Au démarrage, si la dernière sauvegarde a moins de 24 h, l'API attend son échéance au lieu d'en refaire une. Réglages (`appsettings.json`, section `Sauvegardes`) : `Actif`, `Dossier` (relatif au dossier de la base), `Conserver`, `IntervalleHeures`.

L'admin peut aussi lister (`GET /admin/sauvegardes`), lancer (`POST /admin/sauvegardes`) et télécharger (`GET /admin/sauvegardes/{nom}`) les sauvegardes depuis l'espace admin.

Restaurer une sauvegarde (la base actuelle est remplacée : en garder une copie) :

```bash
docker compose stop api                       # 1. arrêter l'API (plus aucune écriture)
cd api/ImmoPlus.Api
mv immoplus.db immoplus-avant-restauration.db # 2. mettre la base actuelle de côté…
rm -f immoplus.db-wal immoplus.db-shm         #    … sans ses fichiers de journal éventuels
cp Sauvegardes/immoplus-20261013-2207.db immoplus.db   # 3. copier la sauvegarde choisie
cd ../.. && docker compose start api          # 4. redémarrer (les migrations manquantes s'appliquent seules)
```

Ces copies de fichiers se font API **arrêtée** : c'est la seule situation où l'on touche `immoplus.db` depuis macOS. Sur le VPS, même principe : `systemctl stop` du service de l'API, copie du fichier dans le dossier de publication, `systemctl start`.

## Vrais SMS en local (téléphone Android)

Par défaut, les SMS sont **simulés** (Admin › SMS envoyés). Pour envoyer de vrais SMS avec ta propre carte SIM :

1. Installer sur un téléphone Android l'application open source **SMS Gateway for Android** (capcom6, « SMS Gate »), lui accorder la permission d'envoyer des SMS et activer le **serveur local** : elle affiche une adresse (ex. `http://192.168.1.20:8080`), un identifiant et un mot de passe. Le téléphone et le Mac doivent être sur le **même Wi-Fi**.
2. Copier `api/ImmoPlus.Api/sms.env.exemple` en `api/ImmoPlus.Api/sms.env` (non versionné) et y mettre l'adresse, l'identifiant, le mot de passe et les numéros autorisés.
3. `docker compose up -d api`.

Sécurité : seuls les numéros de `Sms__NumerosAutorises__N` reçoivent un vrai SMS (les autres sont seulement tracés), sauf si `Sms__EnvoyerATous=true`. Chaque SMS est facturé sur le forfait du téléphone. Téléphone éteint ou injoignable : le SMS est marqué « Échec », l'action continue normalement. Pour revenir à la simulation : supprimer `sms.env` puis `docker compose up -d api`.

## Lancer les tests

Les tests automatisés de l'API (`api/ImmoPlus.Api.Tests`, xUnit) démarrent l'API complète en mémoire (`WebApplicationFactory`) et l'appellent par HTTP, comme le front :

- base SQLite **temporaire** créée pour chaque classe de tests (dossier temporaire du système, supprimé à la fin) : `api/ImmoPlus.Api/immoplus.db` n'est jamais ouverte, l'API Docker peut tourner en même temps ;
- emails envoyés à un faux serveur SMTP interne aux tests (rien ne part vers MailHog) ;
- services planifiés (rappels, sauvegarde) désactivés : les tests les déclenchent eux-mêmes ; les délais (24 h, 48 h, fin de séjour, préavis) sont simulés en reculant les dates dans la base de test.

Sur le Mac (SDK .NET 10) :

```bash
cd api/ImmoPlus.Api.Tests
dotnet test                                   # tous les tests (≈ 1 min)
dotnet test --filter "FullyQualifiedName~AnnulationTests"   # une seule classe
```

Dans Docker (sans SDK local), avec un conteneur jetable — les volumes anonymes sur `bin/` et `obj/` évitent de mélanger les fichiers de compilation Linux et macOS :

```bash
docker run --rm -v "$PWD/api:/src" \
  -v /src/ImmoPlus.Api/bin -v /src/ImmoPlus.Api/obj -v /src/ImmoPlus.Api.Tests/bin -v /src/ImmoPlus.Api.Tests/obj \
  -w /src/ImmoPlus.Api.Tests mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
```

Le dossier de tests est à côté de `api/ImmoPlus.Api` (contexte de build Docker de l'API) : il n'entre pas dans l'image de l'API. Un test marqué `Skip = "Bug : …"` signale un bug connu de l'API, à corriger avant de le réactiver.

## Notes de sécurité (dev → prod)

- La clé JWT (`Jwt:Cle` dans `appsettings.json`) est une valeur de développement. En production, elle est remplacée via la variable d'environnement `Jwt__Cle` définie dans le service systemd (jamais commitée).
- Les origines CORS (`Cors:OriginesAutorisees`) sont définies par environnement (`appsettings.Production.json` pour la prod).
- Les documents uploadés sont stockés localement dans `api/ImmoPlus.Api/Storage/documents/` (hors `wwwroot`, jamais servi en statique — accès uniquement via route authentifiée). Ce dossier est exclu de Git.

## Déploiement (VPS Hostinger 168.231.77.68)

L'API et le client sont déployés séparément sur le même VPS que l'ancien projet, sans toucher à `financement-api` :

| | Ancien projet (financement) | ImmoPlus |
|---|---|---|
| API | port 5000, reverse-proxy nginx → port 80 | port 5050, exposé directement |
| Client | non déployé | fichiers statiques nginx, port 8081 |
| Repo VPS | `/var/www/financement-location-api-git` | `/var/www/immoplus-git` |
| Service systemd | `financement-api` | `immoplus-api` |

URLs actuelles (IP + port, pas encore de domaine/HTTPS — voir "Reste à faire") :
- API : http://168.231.77.68:5050 (Swagger : /swagger)
- Client : http://168.231.77.68:8081

**Redéployer l'API après un push sur `main` :**
```bash
ssh root@168.231.77.68
cd /var/www/immoplus-git && git pull
cd api/ImmoPlus.Api && dotnet publish -c Release -o ./publish
systemctl restart immoplus-api   # les migrations EF Core s'appliquent automatiquement au démarrage
```

**Redéployer le client** (build en local puis transfert, car `npm`/Tailwind ne sont pas installés sur le VPS) :
```bash
# En local, dans web/ImmoPlus.Web
npx tailwindcss -i ./wwwroot/css/tailwind-input.css -o ./wwwroot/css/tailwind-output.css --minify
dotnet publish -c Release -o ./publish
rsync -az ./publish/wwwroot/ root@168.231.77.68:/var/www/immoplus-web/
```

## Reste à faire

- **Nom de domaine + HTTPS** : bloqué tant qu'aucun domaine n'est choisi/acheté — dès qu'un domaine pointe vers `168.231.77.68`, ajouter un reverse-proxy nginx (port 80/443) devant l'API et le client, puis `certbot` pour le TLS.
- Migration SQLite → PostgreSQL en production (PostgreSQL est déjà installé sur le VPS mais pas encore branché à ImmoPlus).
- Restyler/finaliser des parcours secondaires si besoin (ex. tableau de bord Admin dédié).
