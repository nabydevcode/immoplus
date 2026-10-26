# ImmoPlus

Plateforme immobilière : location longue durée (candidature/dossier/garant/financement) et séjours courts type Airbnb.

## Structure

- `api/ImmoPlus.Api/` — API ASP.NET Core Minimal API (.NET 10), EF Core + PostgreSQL, JWT + BCrypt, Swagger.
- `api/ImmoPlus.Api.Tests/` — Tests automatisés de l'API de bout en bout (xUnit, voir « Lancer les tests »).
- `web/ImmoPlus.Web/` — Client Blazor WebAssembly + Tailwind CSS v3 (historique).
- `web/immoplus-web-react/` — Nouveau client React + Vite + TypeScript + Tailwind v4 (en cours de migration, local pour l'instant).
- `docs/schema.md` — Schéma du modèle de données.
- `infra/` — Services locaux : PostgreSQL (sauvegarde `pg_dump`, restauration), stockage S3 Garage (configuration, initialisation, sauvegarde des fichiers), Redis, profil `echelle` (nginx), modèles des fichiers de secrets `*.env.exemple`.

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

Première mise en place de la base de données (PostgreSQL tourne dans Docker même dans ce mode : voir « Base de données PostgreSQL » ; hors conteneur, l'API s'y connecte sur `localhost:5433`, chaîne à fournir par `ConnectionStrings__Default`) :

```bash
cd api/ImmoPlus.Api
dotnet tool install --global dotnet-ef   # si pas déjà installé
dotnet ef database update
```

### Avec Docker (API + client React)

Alternative à l'installation locale de .NET/Node : lance l'API et le nouveau client React dans des conteneurs, avec hot-reload (code monté en volume).

```bash
cp infra/postgres.env.exemple infra/postgres.env   # une seule fois : y mettre un mot de passe (voir ci-dessous)
./infra/initialiser-secrets.sh                      # une seule fois : secrets Garage/S3, Redis, profil echelle, supervision (aléatoires, jamais affichés)
docker compose up
```

- API : http://localhost:5107 (Swagger sur /swagger)
- Client React : http://localhost:5173
- Emails capturés (MailHog) : http://localhost:8025
- PostgreSQL : service `postgres` (port 5433 du Mac, en local uniquement) + sauvegarde quotidienne (`sauvegarde-db`)
- Stockage de fichiers S3 (Garage) : http://localhost:9000 + sauvegarde quotidienne (`sauvegarde-fichiers`) — voir « Stockage des fichiers (S3 / Garage) »
- Redis : `localhost:6379` (mot de passe obligatoire) — voir « Redis »

Les données sont dans le volume Docker `immoplus_postgres-data` : elles persistent entre les redémarrages. `docker compose down` arrête et supprime les conteneurs sans toucher aux données ; **`docker compose down -v` supprime aussi le volume, donc toute la base** (à ne jamais lancer sans sauvegarde récente).

> Sur macOS, si Docker Desktop n'a pas accès au dossier du projet (erreur `operation not permitted` au démarrage), éviter de placer le repo dans `Desktop`/`Documents`/`Downloads` (protégés par défaut) ou autoriser Docker Desktop via Réglages Système → Confidentialité et sécurité → Accès complet au disque.

Le client Blazor (`web/ImmoPlus.Web`) n'est pas (encore) dockerisé.

### Parcours d'inscription (confirmation de l'email, puis validation par l'admin)

1. **Inscription** (`POST /utilisateur/inscription`, Locataire ou Bailleur) : compte `EnAttente`, `emailConfirme=false` ; email « Confirmez votre adresse email » avec un lien `{Frontend}/confirmer-email?jeton=…` valable 48 h (jeton aléatoire de 32 octets, seul son hash SHA-256 est en base). Aucune alerte aux admins à ce stade.
2. **Confirmation** (`POST /utilisateur/confirmer-email` `{ jeton }`, public) : `emailConfirme=true` et `dateConfirmationEmail` ; chaque admin actif reçoit l'email (et le SMS) « Nouveau compte à valider : {nom} ({rôle}) » avec un lien vers `/admin/utilisateurs?q={email}`. Réponse `200 { message, statut }` ; un second clic sur le même lien répond « déjà confirmée » (200, sans nouvel effet) ; lien inconnu, remplacé par un renvoi ou expiré → 400 invitant à demander un nouvel email.
3. **Renvoi** (`POST /utilisateur/renvoyer-confirmation` `{ email }`, public) : toujours 200 avec le même message (ne révèle pas si le compte existe) ; nouvel email seulement pour un compte en attente non confirmé (l'ancien lien ne sert plus), 3 renvois par adresse et par heure au plus (compteurs Redis partagés si Redis est actif).
4. **Validation** (Admin › Utilisateurs, onglet « À valider » = `statut=EnAttente&emailConfirme=true`) : `POST /utilisateur/{id}/valider` répond 409 tant que l'email n'est pas confirmé ; sinon email « Votre compte ImmoPlus est validé » (lien de connexion) + SMS. Un refus envoie « Votre inscription n'a pas été retenue » si l'adresse est confirmée. Les comptes non confirmés restent visibles (onglet « Email non confirmé », `emailConfirme=false`) et ne sont jamais supprimés automatiquement.
5. **Connexion** avant confirmation : `409 { "message": "Confirmez d'abord votre adresse email : un lien vous a été envoyé.", "code": "EmailNonConfirme" }` ; confirmé mais pas encore validé : message habituel « en attente de validation ».

Confirmés d'office (aucun email de confirmation) : les comptes antérieurs à cette étape (migration `ConfirmationEmail`), le premier compte / l'amorçage admin et les admins créés par un admin. Journal des actions : `utilisateur.confirmer-email`, `utilisateur.renvoyer-confirmation`.

### Récupérer un accès admin

Seul un administrateur peut créer un autre administrateur (Admin › Utilisateurs › « Créer un administrateur », route `POST /utilisateur/admin`) : l'inscription publique refuse le rôle Admin dès qu'il existe un admin actif. S'il n'en reste aucun (tous désactivés ou supprimés), l'inscription d'un compte Admin redevient possible et il est validé d'office.

En secours (mot de passe perdu par le dernier admin…), promouvoir un compte existant **depuis le conteneur** :

```bash
docker compose exec -T postgres psql -U immoplus immoplus -c \
  "UPDATE \"Utilisateurs\" SET \"Role\"=0, \"Statut\"=1, \"EmailConfirme\"=TRUE WHERE lower(\"Email\")=lower('prenom.nom@exemple.com');"
```

(`Role=0` = Admin, `Statut=1` = Valide ; la commande doit afficher `UPDATE 1`.) Le compte peut ensuite utiliser « Mot de passe oublié ». En PostgreSQL, les noms de tables et de colonnes créés par EF Core gardent leurs majuscules : il faut les écrire entre guillemets doubles (`"Utilisateurs"`, `"Email"`).

## Base de données PostgreSQL

En développement, la base est un PostgreSQL 17 dans Docker Compose :

| | Valeur |
|---|---|
| Service compose | `postgres` (image `postgres:17-alpine`) |
| Base / utilisateur | `immoplus` / `immoplus` |
| Mot de passe | dans `infra/postgres.env` (non versionné, modèle `infra/postgres.env.exemple`) |
| Depuis les autres conteneurs | `Host=postgres;Port=5432` |
| Depuis le Mac (DBeaver, psql…) | `localhost:5433` (écoute sur 127.0.0.1 seulement ; 5432 est souvent déjà pris par un PostgreSQL local — autre port : `POSTGRES_PORT_HOTE=5434 docker compose up -d postgres`) |
| Données | volume Docker `immoplus_postgres-data` |
| Sauvegardes | `api/ImmoPlus.Api/Sauvegardes/postgres/` (non versionné) |
| Connexions | `max_connections=150` (commande du service ; 100 par défaut) ; pool Npgsql de l'API de dev limité à **10** (`BaseDeDonnees__TaillePoolMax=10` dans `docker-compose.yml`), instances du profil echelle à 40 chacune : 10 + 2 × 40 + sauvegardes + psql reste sous 150 |

**Première installation** — créer le fichier de mot de passe (le même mot de passe deux fois : pour le compte PostgreSQL et dans la chaîne de connexion de l'API) :

```bash
cp infra/postgres.env.exemple infra/postgres.env
chmod 600 infra/postgres.env
openssl rand -hex 24          # puis coller la valeur à la place des deux « a-remplir »
docker compose up -d
```

Le mot de passe n'est pris en compte par PostgreSQL qu'à la **création** du volume. Pour le changer ensuite : `docker compose exec postgres psql -U immoplus -d postgres -c "ALTER USER immoplus PASSWORD '…';"`, puis mettre à jour les deux lignes de `infra/postgres.env` et `docker compose up -d`.

**Démarrer / état** :

```bash
docker compose up -d postgres            # la base seule
docker compose ps postgres               # doit afficher (healthy)
docker compose logs -f postgres
```

**Se connecter** (console SQL dans le conteneur, rien à installer sur le Mac) :

```bash
docker compose exec postgres psql -U immoplus immoplus
# une requête directe :
docker compose exec -T postgres psql -U immoplus immoplus -c 'SELECT count(*) FROM "Utilisateurs";'
```

Commandes utiles dans `psql` : `\dt` (tables), `\d "Reservations"` (structure d'une table), `\q` (quitter).

### Sauvegardes de la base

Le service `sauvegarde-db` fait un `pg_dump -Fc` (format compressé de PostgreSQL, sauvegarde cohérente à chaud, sans arrêter l'API) dès qu'il n'existe pas de sauvegarde de moins de 24 h — il vérifie toutes les heures —, dans `api/ImmoPlus.Api/Sauvegardes/postgres/immoplus-AAAAMMJJ-HHMM.dump` (heure UTC). Les sauvegardes de plus de 14 jours sont supprimées. Les fichiers sont lisibles par le seul propriétaire (données personnelles). Script : `infra/sauvegarde-postgres.sh`.

```bash
docker compose logs sauvegarde-db                                              # historique des sauvegardes
docker compose exec sauvegarde-db /bin/sh /infra/sauvegarde-postgres.sh une-fois   # sauvegarde immédiate
ls -lt api/ImmoPlus.Api/Sauvegardes/postgres/
```

Ce dossier est aussi visible par l'API (`/src/Sauvegardes/postgres` dans le conteneur) : l'espace admin liste et télécharge ces sauvegardes (`GET /admin/sauvegardes`, `GET /admin/sauvegardes/{nom}`).

**Restaurer** une sauvegarde (la base actuelle est remplacée ; le script en fait d'abord une copie de sécurité `immoplus-avant-restauration-….dump`, arrête l'API pendant l'opération et la redémarre) :

```bash
infra/restaurer-postgres.sh                     # sans argument : liste les sauvegardes disponibles
infra/restaurer-postgres.sh api/ImmoPlus.Api/Sauvegardes/postgres/immoplus-20261019-0300.dump
```

Pour simplement **vérifier** qu'une sauvegarde est lisible, sans toucher à la base ni à l'API, la restaurer dans une base à part puis la supprimer :

```bash
BASE=essai_restauration infra/restaurer-postgres.sh --oui api/ImmoPlus.Api/Sauvegardes/postgres/immoplus-20261019-0300.dump
docker compose exec -T postgres psql -U immoplus essai_restauration -c 'SELECT count(*) FROM "Utilisateurs";'
docker compose exec -T postgres dropdb -U immoplus essai_restauration
```

### Ancienne base SQLite (conservée pour mémoire)

Jusqu'au passage à PostgreSQL, l'API utilisait SQLite : le fichier `api/ImmoPlus.Api/immoplus.db` (et ses anciennes sauvegardes `api/ImmoPlus.Api/Sauvegardes/immoplus-*.db`) est conservé, non versionné, comme référence de la migration des données ; il n'est plus utilisé par l'API. Ne jamais l'ouvrir en écriture. Pour l'inspecter, travailler sur une copie faite depuis le conteneur (`docker compose exec -T api sqlite3 immoplus.db ".backup /tmp/copie.db"`), jamais avec le `sqlite3` de macOS pendant qu'un conteneur l'a ouvert (le mode WAL de SQLite ne se synchronise pas à travers le montage Docker et la base se corrompt).

La migration des données SQLite → PostgreSQL se fait avec l'outil `api/ImmoPlus.MigrationDonnees` (Id conservés, séquences recalées, une seule transaction, contrôles avant/après : lignes, derniers Id, sommes des montants, utilisateurs par rôle/statut, comparaison ligne à ligne). Il refuse une base cible non vide (option `--remplacer` pour la vider d'abord) et `immoplus.db` elle-même (il ne lit qu'une copie) :

```bash
docker compose exec -T api sqlite3 immoplus.db ".backup '/src/copie-migration.db'"
mv api/ImmoPlus.Api/copie-migration.db /tmp/copie-migration.db
dotnet run --project api/ImmoPlus.MigrationDonnees -- --source /tmp/copie-migration.db \
  --cible "Host=localhost;Port=5433;Database=immoplus;Username=immoplus" --fichier-env infra/postgres.env
# --controle-seul : refaire uniquement les contrôles (code de sortie 0 si tout est identique)
```

## Stockage des fichiers (S3 / Garage)

Photos, miniatures, documents, états des lieux et pièces d'identité peuvent être rangés dans un **stockage objet compatible S3** au lieu du dossier `api/ImmoPlus.Api/Storage/`, pour que plusieurs instances de l'API voient les mêmes fichiers. En local, c'est **Garage** (https://garagehq.deuxfleurs.fr, licence AGPL, image officielle `dxflrs/garage`) : MinIO, d'abord envisagé, ne publie plus d'images Docker officielles. Pour l'API, c'est du S3 standard : passer plus tard à un autre fournisseur (S3 managé, autre serveur) ne demande que de changer `Stockage__S3__Url` et les clés.

| | Valeur |
|---|---|
| Service compose | `garage` (+ `garage-init`, qui prépare tout puis s'arrête) |
| API S3 | `http://garage:9000` dans Docker, `http://localhost:9000` depuis le Mac (127.0.0.1 seulement ; autre port : `S3_PORT_HOTE=9010`) |
| Bucket | `immoplus`, **privé** (Garage refuse tout accès anonyme ; aucun mode « site web » activé) |
| Région / adressage | `us-east-1`, chemin (`path-style`) : `http://garage:9000/immoplus/photos/12/…` |
| Clé de l'API | `immoplus-api` : lecture + écriture sur ce seul bucket, ne peut créer aucun bucket (`infra/stockage.env`) |
| Clé de sauvegarde | `immoplus-sauvegarde` : **lecture seule** sur ce bucket (`infra/garage.env`) |
| Données | volume Docker `immoplus_garage-data` (supprimé par `docker compose down -v` !) |
| Sauvegardes | `api/ImmoPlus.Api/Sauvegardes/fichiers/` (non versionné) |

Les clés des objets reprennent les chemins relatifs de `Storage/` (`photos/{appartementId}/…`, `miniatures/…`, `documents/{candidatureId}/…`, `etats-des-lieux/{reservationId}/…`, `identites/{userId}/…`) : les chemins enregistrés en base restent valables.

**Secrets** (jamais versionnés, droits 600) : `./infra/initialiser-secrets.sh` crée `infra/garage.env`, `infra/stockage.env`, `infra/redis.env`, `infra/echelle.env`, `infra/metriques.env` et `infra/grafana.env` avec des valeurs aléatoires, sans rien afficher et sans jamais écraser un fichier existant (modèles commentés : `infra/*.env.exemple`). Changer une clé S3 : modifier le fichier puis `docker compose up -d garage-init` et redémarrer l'API ; l'ancienne clé reste dans Garage tant qu'on ne la supprime pas (`docker compose exec garage /garage key delete --yes <identifiant>`).

**État actuel : l'API de développement est en `Stockage__Mode=S3` depuis le 19/10/2026**, après copie contrôlée des 42 fichiers existants (SHA-256 identiques). Le dossier `Storage/` est conservé tel quel comme filet de sécurité mais **n'est plus mis à jour** : les nouveaux fichiers ne sont que dans le bucket (sauvegardés par `sauvegarde-fichiers`). Retour arrière : remettre `Stockage__Mode=Disque` puis `docker compose up -d api` — après avoir recopié dans `Storage/` les fichiers envoyés depuis la bascule (miroir `Sauvegardes/fichiers/courant/`), sinon ils n'apparaîtront plus.

Dans le code, tout passe par l'interface `IStockageFichiers` (`Services/StockageFichiers.cs`) : `StockageDisque` (dossier `Storage/`) ou `StockageS3` (AWSSDK.S3, adressage par chemin), choisie par `Stockage:Mode`. Les routes et les droits ne changent pas : l'API reste le seul point d'accès aux fichiers (bucket privé).

**Copier les fichiers existants vers le bucket** (outil `api/ImmoPlus.CopieFichiers`, rejouable, ne modifie ni ne supprime jamais les originaux) :

```bash
dotnet run --project api/ImmoPlus.CopieFichiers -- --source api/ImmoPlus.Api/Storage \
  --fichier-env infra/stockage.env --url http://localhost:9000 [--rapport copie.csv]
# --controle-seul : ne copie rien, compare seulement ; --rapport : liste clé;taille;SHA-256 (aucun contenu)
```

Il copie `photos/`, `miniatures/`, `documents/`, `etats-des-lieux/` et `identites/` en gardant les chemins comme clés (un objet déjà présent avec la même taille et le même SHA-256 n'est pas renvoyé), puis **relit chaque objet depuis le bucket** et compare taille et SHA-256 à l'original. Code de sortie 0 et « RÉSULTAT : copie IDENTIQUE » seulement si tout concorde ; ne passer en S3 qu'à cette condition. Les secrets de `infra/stockage.env` ne sont jamais affichés.

**État, contenu du bucket** (pas de console web avec Garage : lignes de commande) :

```bash
docker compose ps garage                                   # (healthy)
docker compose logs garage-init                            # « prêt : bucket immoplus privé… »
docker compose exec garage /garage status                  # nœud et capacité
docker compose exec garage /garage bucket info immoplus    # nombre d'objets, taille, clés autorisées
docker compose exec garage /garage key list
# Lister les fichiers (clé lecture seule, rien n'est affiché des secrets) :
docker compose run --rm --entrypoint /bin/sh sauvegarde-fichiers -c \
  'RCLONE_CONFIG_S_TYPE=s3 RCLONE_CONFIG_S_PROVIDER=Other RCLONE_CONFIG_S_ENDPOINT=$S3_URL RCLONE_CONFIG_S_REGION=$S3_REGION \
   RCLONE_CONFIG_S_ACCESS_KEY_ID=$SAUVEGARDE_S3_CLE_ACCES RCLONE_CONFIG_S_SECRET_ACCESS_KEY=$SAUVEGARDE_S3_CLE_SECRETE \
   rclone tree s:immoplus --level 2'
```

Depuis le Mac, n'importe quel client S3 (Cyberduck, `aws` CLI…) fonctionne sur `http://localhost:9000` avec la clé de `infra/stockage.env`, région `us-east-1`, adressage par chemin.

### Sauvegarde des fichiers

Le service `sauvegarde-fichiers` (image `rclone/rclone`, script `infra/sauvegarde-fichiers.sh`) copie le bucket une fois par 24 h (vérification toutes les heures) dans `api/ImmoPlus.Api/Sauvegardes/fichiers/` :

- `courant/` : **miroir** exact du bucket à la dernière sauvegarde (mêmes chemins que `Storage/`) ;
- `versions/AAAAMMJJ-HHMM/` : les fichiers **modifiés ou supprimés** depuis la sauvegarde précédente, dans leur ancienne version ; conservés **14 jours** (un fichier effacé par erreur reste donc récupérable 14 jours) ;
- `derniere-sauvegarde` : date (UTC) de la dernière sauvegarde réussie.

Il n'a qu'une clé en lecture seule : une erreur ou une compromission de ce service ne peut rien effacer dans le stockage. Fichiers lisibles par le seul propriétaire (pièces d'identité).

```bash
docker compose logs sauvegarde-fichiers                                                  # historique
docker compose exec sauvegarde-fichiers /bin/sh /infra/sauvegarde-fichiers.sh une-fois   # sauvegarde immédiate
docker compose exec sauvegarde-fichiers /bin/sh /infra/sauvegarde-fichiers.sh controle   # miroir identique au bucket ? (code 0 = oui)
```

**Restaurer** (avec la clé de l'API, qui seule peut écrire) — `copy` ajoute ou remplace les fichiers sans rien supprimer du bucket :

```bash
# tout le miroir :
docker compose run --rm --env-from-file infra/stockage.env --entrypoint /bin/sh sauvegarde-fichiers -c \
  'RCLONE_CONFIG_S_TYPE=s3 RCLONE_CONFIG_S_PROVIDER=Other RCLONE_CONFIG_S_ENDPOINT=$S3_URL RCLONE_CONFIG_S_REGION=$S3_REGION \
   RCLONE_CONFIG_S_ACCESS_KEY_ID=$Stockage__S3__CleAcces RCLONE_CONFIG_S_SECRET_ACCESS_KEY=$Stockage__S3__CleSecrete \
   rclone copy /sauvegardes/courant s:immoplus --dry-run'      # retirer --dry-run après vérification
# un fichier précis depuis une ancienne version : remplacer la source par
#   /sauvegardes/versions/20261019-0300/photos/12/photo.jpg  et la destination par  s:immoplus/photos/12/
```

## Redis

Redis sert à l'API quand **plusieurs instances** tournent : relais des messages en temps réel (backplane SignalR, canal `immoplus-signalr`), compteurs de tentatives de connexion et de « mot de passe oublié » partagés (clés `immoplus:tentatives:…`, mêmes règles : 5 échecs par email ou 20 par IP en 15 min), cache de 30 s du statut des comptes (effacé à chaque désactivation / réactivation, donc effet immédiat sur toutes les instances), cache de 2 min des badges des hôtes (`immoplus:hote:…`) et cache de sortie des listes publiques (`immoplus-cache:…`).

**Préfixe** : toutes ces clés et ce canal commencent par `Redis__Prefixe` (`immoplus` par défaut, donc inchangés) : deux environnements peuvent partager un même Redis avec deux préfixes différents (ex. `immoplus-charge`), sans mélanger cache, compteurs ni temps réel. Sans Redis (`Redis__Connexion` vide), l'API fonctionne comme avant, en une seule instance (compteurs en mémoire, statut lu en base). Redis momentanément injoignable : l'API démarre quand même et se reconnecte seule ; le limiteur se replie sur la mémoire locale.

Les **tâches planifiées** ne dépendent pas de Redis : un verrou consultatif PostgreSQL (`pg_try_advisory_lock`, libéré automatiquement si l'instance s'arrête) et la table `ExecutionsPlanifiees` (date et instance du dernier passage) garantissent un seul exécutant sur l'ensemble des instances, même après un redémarrage :
- **rappels** des échéances et purges : **un passage par 24 h**, chaque instance vérifie toutes les 15 min (`Rappels__VerificationMinutes`) si un passage est dû ;
- **maintenance** des réservations et des loyers (demandes sans réponse depuis 48 h refusées, impayées depuis 24 h annulées, séjours finis terminés, loyers du mois créés) : **toutes les 2 min** (`Maintenance__IntervalleMinutes`), par lots de 100 (`Maintenance__TailleLot`), 5 lots au plus par catégorie et par passage (`Maintenance__LotsMaxParPassage`) ; `Maintenance__Actif=false` la coupe. Ces traitements ne se font plus pendant les lectures : la liste admin des réservations ne traite que les lignes affichées, la recherche par dates ignore déjà en SQL les impayées expirées, le tableau de bord admin ne crée plus les loyers. Mesuré sur la base de charge : arriéré de 636 demandes et 1 100 séjours absorbé en 3 passages (18 s, 6 s, 2 s) hors de toute requête, contre un premier appel de la liste admin de 42 s auparavant.

**État actuel : Redis est activé sur l'API de développement depuis le 19/10/2026.**

| | Valeur |
|---|---|
| Service compose | `redis` (image officielle `redis:8.8-alpine`, persistance AOF `appendonly yes`) |
| Depuis Docker / depuis le Mac | `redis:6379` / `localhost:6379` (127.0.0.1 seulement ; autre port : `REDIS_PORT_HOTE=6380`) |
| Mot de passe | `REDIS_PASSWORD` dans `infra/redis.env` (obligatoire, aucun accès sans) |
| Données | volume Docker `immoplus_redis-data` (données reconstituables : compteurs, cache, verrous) |

```bash
docker compose ps redis                                            # (healthy)
docker compose exec redis sh -c 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli ping'      # PONG
docker compose exec redis sh -c 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --scan | head'   # quelques clés
```

**Activer / désactiver Redis pour l'API de développement** : dans `infra/redis.env`, remplir `Redis__Connexion=redis:6379,password=<valeur de REDIS_PASSWORD>,abortConnect=false` (une ligne modèle commentée est dans le fichier), puis `docker compose up -d api`. Retour arrière : vider la valeur.

## Boîte d'envoi des emails et SMS (outbox)

Les emails et SMS ne sont plus envoyés pendant la requête : `NotificationServices` et `SmsServices` les enregistrent dans la table **`MessagesSortants`**, dans la même transaction que l'action métier (si l'action est annulée, le message disparaît avec elle). Un service de fond présent sur chaque instance (`EnvoiMessagesHostedService`, vérification toutes les 2 s : `Boite__IntervalleSecondes`) les dépile avec `SELECT … FOR UPDATE SKIP LOCKED` : plusieurs instances se partagent la file sans jamais envoyer deux fois le même message. Délai normal : une à deux secondes.

- Échec d'envoi (SMTP en panne…) : nouvel essai après 1 min, 5 min, 30 min puis 2 h ; **abandon au 5e échec** (statut `Abandonne`, erreur journalisée). L'utilisateur n'est jamais bloqué : par exemple « mot de passe oublié » répond normalement même si le SMTP est en panne.
- Statuts : `EnAttente`, `Envoye`, `Echec` (nouvel essai prévu), `Abandonne`. Les traces SMS (`NotificationsSms`, Admin › SMS envoyés) restent alimentées.
- Les rappels marquent une échéance « rappelée » quand l'email est **mis en file** (même transaction) : pas de double rappel.
- Messages envoyés ou abandonnés conservés 90 jours (purge avec les rappels quotidiens).
- Consultation (Admin) : `GET /admin/messages-sortants?statut=&page=&taille=` (du plus récent au plus ancien, 100 par page au plus).
- **SMTP non configuré** (`Smtp__Host` vide) : les emails sont **quand même enregistrés** et restent `EnAttente`, sans consommer de tentative ni être abandonnés ; ils partent dès que le SMTP est configuré (après redémarrage de l'API). Les SMS ne sont pas bloqués derrière eux. Un avertissement est écrit au démarrage.
- **Relance** (Admin, page « Messages sortants ») : `POST /admin/messages-sortants/relancer?statut=Abandonne` (ou `Echec`) remet en file tous les messages de ce statut avec 5 nouvelles tentatives et un envoi immédiat ; `POST /admin/messages-sortants/{id}/relancer` pour un seul message (409 s'il n'est ni abandonné ni en échec). Réponse `{ "relances": n }`, action tracée dans le journal (`message-sortant.relancer`).

### Réglages SMTP (MailKit)

| Réglage | Rôle |
|---|---|
| `Smtp__Host` | serveur (vide = emails conservés en attente) |
| `Smtp__Port` | 25 par défaut ; 465 (SSL implicite), 587 (STARTTLS), 1025 (MailHog) |
| `Smtp__Securite` | `Auto` (défaut : 465 → SSL implicite, 587 → STARTTLS, 25 / 1025 → aucun, autre port → STARTTLS si proposé), `Aucune`, `SslImplicite`, `StartTls` ; valeur inconnue refusée |
| `Smtp__Utilisateur` / `Smtp__MotDePasse` | authentification si l'identifiant est renseigné (mot de passe jamais journalisé) |
| `Smtp__Expediteur` / `Smtp__NomExpediteur` | adresse et nom affiché (« ImmoPlus » par défaut) |

Délai de 30 s par envoi. En développement : `mailhog:1025`, sans authentification ni TLS (rien à changer). Le journal de démarrage indique l'hôte, le port, le chiffrement retenu et si une authentification est faite.

```bash
docker compose exec -T postgres psql -U immoplus immoplus -c 'SELECT "Statut", count(*) FROM "MessagesSortants" GROUP BY 1'   # 0 EnAttente, 1 Envoye, 2 Echec, 3 Abandonne
```

### Copie dans « Envoyés » (IMAP)

Avec `Imap__Actif=true`, chaque email **parti** (SMTP accepté, pas les SMS) est aussi déposé par **IMAP APPEND** dans le dossier des messages envoyés de la boîte de l'expéditeur, avec le drapeau « lu » (`\Seen`), la même date et le même `Message-Id` que l'email reçu : l'équipe retrouve dans son webmail tout ce que le site a envoyé (pas de copie cachée, rien de visible pour le destinataire).

| Réglage | Rôle |
|---|---|
| `Imap__Actif` | `false` par défaut (développement, tests : aucune connexion IMAP) |
| `Imap__Host` / `Imap__Port` | serveur IMAP ; port 993 par défaut |
| `Imap__Securite` | comme `Smtp__Securite` : `Auto` (993 → SSL implicite, 143 → STARTTLS, autre → STARTTLS si proposé), `Aucune`, `SslImplicite`, `StartTls` |
| `Imap__Utilisateur` / `Imap__MotDePasse` | vides = ceux du SMTP (même boîte) |
| `Imap__Dossier` | vide = détection : dossier spécial `\Sent` annoncé par le serveur (SPECIAL-USE), sinon `Sent`, `INBOX.Sent`, `Sent Messages`, `Envoyés` ; sinon nom exact du dossier |

- **Un échec de la copie ne touche jamais l'email** (déjà parti, statut `Envoye`, jamais renvoyé) : avertissement dans le journal (sans mot de passe ni contenu), trace dans `MessagesSortants` (`CopieEnvoyes` : vide / `Ok` / `Echec` / `Abandonnee`, `CopieTentatives`, `CopieErreur`) et **nouvel essai de la copie seule** après 1 min, 5 min puis 30 min ; abandon au 4e échec. Aucun dossier trouvé : erreur « renseignez Imap:Dossier ».
- Une connexion IMAP par instance, **réutilisée** d'un email à l'autre (rouverte si le serveur l'a coupée, après 5 min d'inactivité ou si les réglages changent) ; délai de 30 s. La copie se fait juste après l'envoi SMTP, sous le même verrou du message (pas de double copie entre instances).
- Page admin « Messages sortants » : champs `copieEnvoyes`, `copieTentatives`, `copieErreur` ; métrique `immoplus_boite_envoi_copie_echecs` (emails dont la copie est en échec ou abandonnée).
- Le journal de démarrage indique l'hôte, le port, le chiffrement, le compte et le dossier de la copie IMAP quand elle est active.

```bash
docker compose exec -T postgres psql -U immoplus immoplus -c 'SELECT "CopieEnvoyes", count(*) FROM "MessagesSortants" WHERE "Type" = 0 GROUP BY 1'   # vide, 0 Ok, 1 Echec, 2 Abandonnee
```

## Pagination, performances et observabilité (étape 3)

### Listes paginées
Toutes les listes qui grossissent renvoient `PagedResultDto` : `{ donnees, pageActuelle, taillePage, totalElements, totalPages }`, avec `page` (1 par défaut) et `taille` (20 par défaut, 100 au plus ; au-delà la taille est plafonnée, pas refusée). Le tri est toujours stable (terminé par le n°). Un compteur d'onglet se lit avec `taille=1` → `totalElements`.

| Route | Paramètres (en plus de `page`, `taille`) | Tri |
|---|---|---|
| `GET /appartement/lister` | filtres existants (`typeOffre`, `disponible`, `ville`, `quartier`, dates, `voyageurs`, `equipements`, `prixMax`, `loyerMax`, `chambresMin`, `avanceMax`, `bailleurId`, bornes `nord/sud/est/ouest`) + `supprimes`, `q` ; `taille` ≤ 200 avec les 4 bornes | plus récentes d'abord (n° décroissant) |
| `GET /reservation/lister` | `filtre` (`Demandes`, `Actives`, `AVenir`, `AEvaluer`, `Passees`, `ARembourser`, `Toutes`), `statut`, `q` (n°, voyageur, adresse) | propre à l'onglet (voir Swagger) |
| `GET /location/lister` | `statut`, `filtre=AEvaluer`, `q` | début puis n°, décroissants |
| `GET /financement/lister` | `statut`, `q` (n° de financement ou de location, nom, adresse) | n° décroissant |
| `GET /candidature/lister` | `statut`, `appartementId`, `q` (page/taille désormais facultatifs) | dépôt puis n°, décroissants |
| `GET /favoris` | — (`/favoris/ids` reste complet) | ajout le plus récent d'abord |
| `GET /admin/identites` | `statut` | en attente d'abord, puis envoi décroissant |
| `GET /appartement/{id}/avis` | `reservationId` ; renvoie toujours `ListeAvisDto` (moyennes et `nombre` sur **tous** les avis) + `pageActuelle`, `taillePage`, `totalPages` | plus récents d'abord |
| `GET /reservation/{id}/messages` | `taille` (30 par défaut), `page` (1 = les plus récents) ou `avant={id}` (curseur) ; chaque page en ordre chronologique | — |

`GET /messages/non-lus?filtre=…` compte les non-lus d'un seul onglet de réservations.

### Index et requêtes
Migration `IndexEtPerformances` : index composites des listes (participant + date + n°), chevauchement de dates (`AppartementId, DateArrivee, DateDepart`), messages (`ReservationId, Id` et non-lus partiels), avis, favoris, candidatures, index partiels des traitements automatiques, et **index trigrammes** (`pg_trgm`) pour les recherches « contient » sans accents : la fonction `immoplus_sans_accents()` (enveloppe IMMUTABLE de `unaccent`, appelée par `AppDbContext.SansAccents` dans les requêtes EF) est indexée sur la ville, le quartier, l'adresse et le nom ; email et téléphone ont aussi un index trigrammes. La disponibilité sur des dates est calculée en SQL (`NOT EXISTS`), les moyennes d'avis et les badges des hôtes par agrégats SQL (badges gardés 2 min pour les listes **et la fiche**, dans Redis s'il est configuré, sinon en mémoire ; effacés à la vérification d'identité de l'hôte). Liste des annonces : hôte réduit à son nom et son statut, et pour le public seulement la photo de couverture et le premier panorama 360° (une requête `DISTINCT ON`) ; le propriétaire et l'admin reçoivent toutes les photos. Non-lus : réservations prises selon le rôle (voyageur → `LocataireId`, hôte → ses logements, index `IX_Reservations_AppartementId_Id`). Tableau de bord admin : retards comptés et additionnés en SQL, seules les 10 lignes affichées sont lues. Requêtes de plus de 200 ms : avertissement dans le journal avec le SQL, sans les valeurs (`Journaux__SeuilRequeteLenteMs`).

### Cache et compression
- Réponses compressées (Brotli, sinon Gzip), sauf SignalR (`/hubs`) et les images.
- Photos publiques : `Cache-Control: public, max-age=86400, immutable` + `ETag` (réponse 304 sans relire S3) ; photos protégées (annonce supprimée, hôte désactivé) : `private, no-store`.
- Cache de sortie de 30 s (dans Redis s'il est configuré, sinon en mémoire) pour `/appartement/lister`, `/appartement/villes` et `/appartement/{id}/avis` **sans jeton** ; vidé avant la réponse de toute modification d'annonce, de photo, de calendrier, d'avis, de compte d'hôte ou de signalement. L'en-tête `X-Correlation-Id` n'est pas mis en cache : une réponse resservie porte l'identifiant de sa propre requête.

### Santé, journaux, métriques
- Base saturée ou injoignable (pool Npgsql épuisé, « too many clients », délai dépassé, connexion perdue) : **503** avec `Retry-After: 5` et `{"message":"Service momentanément indisponible, réessayez dans quelques instants"}`, journalisé en Error (avant : 400 avec un message technique d'EF Core). Les 400 métier ne changent pas.
- Pool Npgsql : `Maximum Pool Size` = `BaseDeDonnees__TaillePoolMax` (**40** par défaut) si la chaîne de connexion n'en fixe pas déjà une (sinon la sienne prime). Npgsql en ouvrirait jusqu'à 100 par instance, au-delà de `max_connections=100` dès 2 instances. Règle (local comme production) : **(nombre d'instances × pool) + sauvegardes + marge ≤ `max_connections`** ; en local : API de dev à 10, PostgreSQL à 150 (voir « Base de données PostgreSQL »), variable documentée dans `infra/postgres.env.exemple`.
- DataProtection n'est utilisé par rien (JWT, pas de cookie) : clés éphémères en mémoire, plus d'avertissement « keys … may not be persisted » au démarrage.
- `GET /sante/vivant` : le processus répond (200). `GET /sante/pret` : PostgreSQL, Redis s'il est configuré, S3 en mode S3 (200 `{"statut":"Ok",…}` ou 503 `"Indisponible"`), JSON sans secret ; pour nginx, les healthchecks compose ou systemd.
- Journaux JSON sur la console (une ligne par événement, `Journaux__Format=Texte` pour un affichage lisible), avec l'identifiant de corrélation : en-tête `X-Correlation-Id` repris (s'il est sûr) ou généré, renvoyé dans la réponse, présent dans toutes les lignes de la requête ; une ligne par requête (méthode, modèle de route, statut, durée, n° d'utilisateur ; jamais de chaîne de requête, de jeton, de mot de passe ni de contenu de message). Les requêtes SQL ne sont plus journalisées une à une (`Microsoft.EntityFrameworkCore.Database.Command` = Warning).
- `GET /metriques` (format Prometheus, OpenTelemetry) : durées des requêtes par route (`http_server_request_duration_seconds`, histogramme → p50/p95/p99 avec `histogram_quantile`), statuts et erreurs, connexions SignalR (`signalr_server_active_connections`, dès la première connexion), requêtes SQL Npgsql (`db_client_operation_duration_seconds`, connexions du pool), processus (CPU, mémoire, GC, threads), boîte d'envoi (`immoplus_boite_envoi_en_attente`, `immoplus_boite_envoi_abandonnes`, `immoplus_boite_envoi_copie_echecs`). Accès : avec `Metriques__Jeton` renseigné, en-tête `Authorization: Bearer <jeton>` obligatoire ; sinon uniquement depuis la boucle locale et les réseaux privés (Docker, LAN) ; 404 pour les autres. nginx ne doit pas publier `/metriques`.

```bash
curl -s http://localhost:5107/sante/pret
curl -s http://localhost:5107/metriques | grep http_server_request_duration_seconds_count | head
```

### Données de charge (`api/ImmoPlus.DonneesCharge`)
Remplit une base **jetable** (le nom doit commencer par `immoplus_charge` ou `immoplus_perf` ; `immoplus` est refusée) avec, par défaut, 100 000 utilisateurs (5 000 bailleurs), 20 000 logements (moitié séjour court, 15 villes guinéennes, coordonnées), 60 000 photos (5 objets de référence partagés), 50 000 réservations avec paiements, 200 000 messages, ~26 000 avis, 10 000 candidatures (~1 700 locations avec financement, mensualités et loyers), 50 000 favoris. Insertion par COPY binaire, schéma créé par les migrations de l'API. Durée mesurée : **≈ 25 s** (base de 120 Mo). Tous les comptes sont en `@test-charge.local`, mot de passe `ChargeImmo!2026` (`admin@`, `bailleur1@` — 300+ logements, `locataire1@` — réservations, candidatures, favoris).

```bash
# bucket jetable (une fois) : clé de l'API en lecture/écriture
docker compose exec garage /garage bucket create immoplus-charge
docker compose exec garage /garage bucket allow --read --write immoplus-charge --key immoplus-api
# remplissage (--remplacer : recrée la base ; volumes : --utilisateurs, --logements, --reservations, --messages…)
dotnet run --project api/ImmoPlus.DonneesCharge -- \
  --cible "Host=localhost;Port=5433;Database=immoplus_charge;Username=immoplus" --fichier-env infra/postgres.env \
  --s3-fichier-env infra/stockage.env --s3-url http://localhost:9000 --remplacer
# nettoyage
docker compose exec -T postgres psql -U immoplus postgres -c 'DROP DATABASE immoplus_charge WITH (FORCE)'
```
Pour brancher une API sur cette base : `ConnectionStrings__Default=…Database=immoplus_charge…` et `Stockage__S3__Bucket=immoplus-charge`.

## Profil « echelle » : plusieurs instances de l'API

Pour vérifier que l'API fonctionne **à plusieurs instances** (fichiers dans S3, SignalR et compteurs via Redis, tâches planifiées exécutées une seule fois), le profil `echelle` ajoute :

- `api-echelle-1` et `api-echelle-2` : l'API compilée en **Release** (`api/ImmoPlus.Api/Dockerfile.production` : `dotnet publish`, image `aspnet` sans SDK, utilisateur non root, `ASPNETCORE_ENVIRONMENT=Production`), sans port publié ; `Stockage__Mode=S3` et Redis toujours actifs ; même base PostgreSQL que l'API de dev ; clé JWT propre au profil (`infra/echelle.env`), emails vers MailHog, SMS simulés (pas de `sms.env`), paiement mobile désactivé, rappels coupés par défaut ;
- `nginx-echelle` : le **build de production du client React** + répartiteur sur **http://localhost:8088** (127.0.0.1 seulement ; autre port : `ECHELLE_PORT_HOTE=8089`), même organisation que la future production : `/api/` est transmis aux instances en retirant le préfixe, tourniquet (round-robin) pour les requêtes ordinaires, WebSockets SignalR avec affinité par jeton (la négociation et la connexion d'un même utilisateur arrivent sur la même instance), `X-Forwarded-For` / `X-Forwarded-Proto` transmis. L'en-tête de réponse `X-Instance-Api` indique l'instance qui a répondu. Configuration : `infra/echelle/nginx.conf`. nginx attend que les deux instances soient « healthy » (`/sante/pret`), garde en cache les photos publiques d'annonces (en-tête `X-Cache: HIT/MISS`, jamais les photos protégées) et répond 404 à `/api/metriques`. Pour brancher ce profil sur la base de charge plutôt que sur la base de dev : voir « Tests de charge (k6) ».

Le mode développement normal (`api` en `dotnet watch`, `web` sur 5173) reste le défaut et n'est pas modifié.

```bash
# Démarrer (construit les images ; démarre aussi postgres, garage, redis, mailhog s'ils ne tournent pas)
docker compose --profile echelle up -d --build api-echelle-1 api-echelle-2 nginx-echelle
docker compose --profile echelle ps
docker compose --profile echelle logs -f api-echelle-1 api-echelle-2

# Tester
open http://localhost:8088                                          # le site, servi par nginx
for i in 1 2 3 4; do curl -s -o /dev/null -D - http://localhost:8088/api/appartement/villes | grep -i x-instance-api; done
docker compose --profile echelle logs nginx-echelle                 # chaque requête avec l'instance qui l'a servie

# Pour tester aussi le verrou des rappels planifiés (même base que le dev !) :
ECHELLE_RAPPELS_ACTIFS=true docker compose --profile echelle up -d api-echelle-1 api-echelle-2

# Arrêter (sans toucher au reste)
docker compose --profile echelle stop api-echelle-1 api-echelle-2 nginx-echelle
docker compose --profile echelle rm -f api-echelle-1 api-echelle-2 nginx-echelle
```

Vérifié le 19/10/2026 : message en temps réel entre deux comptes connectés à deux instances différentes (avec négociation + affinité, et en WebSocket direct `skipNegotiation`), limitation des connexions partagée, photo envoyée via une instance et lue via l'autre, miniatures (SkiaSharp) et PDF (QuestPDF) générés sur l'image Release, rappels exécutés une seule fois, invalidation immédiate du statut d'un compte désactivé. Derrière nginx dans Docker, l'adresse du client n'est reconnue que si le réseau du répartiteur figure dans `ProxysDeConfiance__Reseaux__0` (ex. `172.16.0.0/12`) ; sinon toutes les requêtes semblent venir de nginx et la limite « 20 échecs par IP » est commune à tous les clients.

Les jetons de connexion du profil (`localhost:8088`) et ceux du dev (`localhost:5173`) ne sont pas interchangeables (clés JWT différentes) : se reconnecter. Pour vérifier le temps réel entre instances, connecter deux comptes de test dans deux navigateurs (ou une fenêtre privée) et regarder `X-Instance-Api` (outils de développement › Réseau) : l'affinité étant calculée sur le jeton, deux comptes peuvent tomber sur la même instance — reconnecter l'un d'eux (nouveau jeton) jusqu'à obtenir deux instances différentes.

## Supervision (profil « supervision » : Prometheus + Grafana)

Prometheus collecte `/metriques` des API toutes les 10 s directement sur le réseau Docker (instances `api-echelle-1/2` avec le jeton `Metriques__Jeton` d'`infra/metriques.env`, API de dev `api:5107` sans jeton) ; Grafana affiche le tableau de bord **« ImmoPlus — API »** (page d'accueil) : requêtes/s par instance, latences p50/p95/p99 globales et par route (tableau des routes trié par p95), taux d'erreurs (5xx + 499 ; la base saturée renvoie 503 depuis le 22/10/2026, les 400 ne sont plus comptés) et réponses par code HTTP, instances en ligne, boîte d'envoi (en attente/abandonnés), connexions SignalR, requêtes SQL (p95) et pool de connexions PostgreSQL (utilisées/max), CPU, mémoire, pool de threads, ramasse-miettes, connexions Kestrel. Tout est publié sur **127.0.0.1 uniquement**.

```bash
./infra/initialiser-secrets.sh                       # crée infra/metriques.env et infra/grafana.env s'ils manquent
docker compose --profile supervision up -d           # Prometheus http://localhost:9090, Grafana http://localhost:3000
# identifiant/mot de passe Grafana : infra/grafana.env (pris en compte à la première création du volume grafana-data)
docker compose --profile supervision stop prometheus grafana     # arrêter (historique conservé dans prometheus-data, 15 jours / 2 Go)
```

- Configuration : `infra/supervision/prometheus.yml`, `infra/supervision/grafana/provisioning/` (source de données), `infra/supervision/grafana/tableaux/immoplus-api.json` (tableau de bord, en lecture seule dans Grafana : le modifier dans le fichier).
- Le jeton n'est jamais écrit dans `prometheus.yml` : le point d'entrée du conteneur le copie dans un fichier temporaire (`credentials_file`).
- **Healthchecks compose sur `/sante/pret`** : `api` (curl), `api-echelle-1/2` (requête HTTP écrite avec bash, l'image aspnet n'ayant ni curl ni wget), `prometheus`, `grafana`. `nginx-echelle` ne démarre qu'une fois les deux instances « healthy ». `docker compose ps` affiche `(healthy)` / `(unhealthy)`.
- `/metriques` n'est jamais publié par nginx : `infra/echelle/nginx.conf` répond 404 à `/api/metriques` (sinon la requête arriverait à l'API depuis l'adresse privée de la passerelle Docker). En production, mettre le même bloc dans le site nginx et renseigner `Metriques__Jeton`.

## Tests de charge (k6)

Scénarios réalistes joués par [k6](https://grafana.com/docs/k6/) (image officielle `grafana/k6`) contre le profil `echelle` (nginx + 2 instances Release) **branché sur la base jetable `immoplus_charge`, le bucket `immoplus-charge` et un Redis à part (`redis-charge`)** par la surcharge `infra/charge/compose.charge.yml` — jamais sur la base `immoplus` ni le bucket `immoplus` (les instances refusent de démarrer sinon, et le script vérifie leur configuration avant chaque palier). Le Redis séparé évite que le cache des listes publiques et le temps réel SignalR se mélangent avec l'API de dev (depuis le 22/10/2026, `Redis__Prefixe` permettrait aussi de partager le Redis de dev avec un autre préfixe).

Population simulée (proportion des utilisateurs virtuels, pause de 1 à 4 s entre deux pages) : **visiteurs 70 %** (villes, recherche séjour court/longue durée avec filtres et dates, page 2, carte avec bornes, fiche, photo, avis, calendrier, miniatures), **locataires 20 %** (leurs réservations par onglet, non-lus, détail, fil de messages, envoi d'un message une fois sur quatre dans une conversation ouverte, favoris), **hôtes 8 %** (réservations reçues, tableau de bord, leurs logements, candidatures), **admin 2 %** (tableau de bord, listes paginées et recherche `q` : utilisateurs, réservations, logements, locations, financements), plus des connexions **SignalR** (WebSocket) pour la moitié des locataires/hôtes. Connexions faites une seule fois au début (`setup()`) avec de bons mots de passe, jetons réutilisés : la limitation des tentatives n'est jamais déclenchée. Seuils : p95 < 500 ms (800 ms pour l'admin), erreurs < 1 %.

```bash
# 1. Base et bucket de charge (voir « Données de charge ») puis profil echelle branché dessus :
docker compose -f docker-compose.yml -f infra/charge/compose.charge.yml --profile echelle up -d --build api-echelle-1 api-echelle-2 nginx-echelle
docker compose --profile supervision up -d          # facultatif : suivre le test dans Grafana
# 2. Un palier = montée 1 min + 3 min stables (MONTEE=…, DUREE=… pour changer)
infra/charge/lancer-charge.sh 100
K6_GOMEMLIMIT=1000MiB infra/charge/lancer-charge.sh 500     # plafond mémoire de k6 (Docker 4 Go)
infra/charge/lancer-charge.sh 1000
# 3. Nettoyage : emails du test dans MailHog (uniquement @test-charge.local), arrêt des profils
infra/charge/purger-mailhog.sh
docker compose -f docker-compose.yml -f infra/charge/compose.charge.yml --profile echelle stop api-echelle-1 api-echelle-2 nginx-echelle redis-charge
docker compose -f docker-compose.yml -f infra/charge/compose.charge.yml --profile echelle rm -f api-echelle-1 api-echelle-2 nginx-echelle redis-charge
docker compose --profile supervision stop prometheus grafana
```

Résultats dans `infra/charge/resultats/` (non versionné) : résumé k6 par palier (JSON, p50/p95/p99 par profil et par route), CPU/mémoire des conteneurs et connexions PostgreSQL toutes les 10 s (CSV). Pour relancer le profil echelle **sur la base de dev** (usage normal), relancer la commande du paragraphe « Profil echelle » sans `-f infra/charge/compose.charge.yml`.

Variables de la surcharge : `CHARGE_POOL_MAX` (taille maximale du pool Npgsql par instance, 40 par défaut ; `100` = réglage d'origine de Npgsql), `CHARGE_NPGSQL_OPTIONS` (ex. `"Max Auto Prepare=100;Auto Prepare Min Usages=2"`), `CHARGE_NIVEAU_JOURNAL_REQUETES` (`Warning` coupe la ligne de journal par requête), `CHARGE_SEUIL_SQL_LENT_MS`, `CHARGE_MAINTENANCE_ACTIVE` (`false` par défaut : maintenance planifiée coupée pour mesurer les lectures ; `true` = vérifier qu'elle ne gêne pas — son premier passage sur `immoplus_charge` envoie des centaines d'emails `@test-charge.local` à MailHog, à purger ensuite). Variable du script : `K6_GOMEMLIMIT` (ex. `1000MiB`, plafond mémoire souple de k6 ; nécessaire à 500 utilisateurs virtuels avec 4 Go pour Docker, sinon k6 est tué en fin de palier, code 137).

Le résumé k6 affiche les erreurs par famille : `503` (base saturée, avec `Retry-After`), `5xx` (503 compris), `4xx`, et « sans réponse » (délai dépassé, connexion coupée) ; tout statut ≥ 400 compte dans le taux d'échec.

**Premiers résultats, 22/10/2026, avant corrections** (Mac M1, Docker Desktop 8 processeurs virtuels / 4 Go partagés par toute la pile, l'API de dev et k6 — à ne pas confondre avec un serveur) :

| Palier (utilisateurs virtuels) | Réglage | Requêtes/s (vues par l'API) | p50 | p95 | p99 | Erreurs |
|---|---|---|---|---|---|---|
| 100 | pool Npgsql d'origine (100/instance) | ≈ 100 | 17 ms | 336 ms | 976 ms | 0 % |
| 100 | pool 40/instance | ≈ 97 | 15 ms | 427 ms | 1,7 s | 0 % |
| 250 | pool 40 | ≈ 73 | 1,0 s | 8,3 s | 13,5 s | 0,01 % |
| 250 | pool 40 + cache nginx des photos | ≈ 64 (+ photos servies par nginx) | 256 ms | 6,5 s | 11,6 s | 0 % |
| 500 | pool d'origine | ≈ 30 | 7,5 s | 39 s | 58 s | **39,7 %** (« too many clients ») |
| 500 | pool 40 | ≈ 40 | 3,3 s | 36 s | 56 s | 0,7 % |
| 1 000 | — | non mesurable sur ce Mac : k6 tué faute de mémoire (code 137) pendant la montée | | | | |

Goulots relevés : saturation du processeur de la machine dès ~150 utilisateurs virtuels (≈ 21 ms de CPU par requête, dont ≈ 12 ms API et ≈ 5 ms PostgreSQL) ; **pools Npgsql (2 × 100) supérieurs à `max_connections=100`** de PostgreSQL → erreurs « too many clients » sous charge (corrigé par `Maximum Pool Size`) ; ces erreurs sont renvoyées en **400** par l'API au lieu de 503 ; routes les plus coûteuses : `/appartement/lister` (28 % du temps serveur), fiche `/appartement/{id}` (20 %, statistiques de l'hôte recalculées à chaque fiche), photos (17 %, d'où le cache nginx), compteur des non-lus (parcours complet des réservations), tableaux de bord admin et hôte (0,5 à 5 s), liste admin des réservations (traitement en série des séjours à clôturer pendant la lecture).

**Corrections du 22/10/2026 (remesurées le 23/10, tableau avant/après ci-dessous)** : 503 + `Retry-After` au lieu de 400 quand la base sature ; badges des hôtes en cache pour la fiche aussi ; non-lus selon le rôle (+ index) ; maintenance planifiée par lots au lieu des traitements pendant les lectures ; retards du tableau de bord admin agrégés en SQL ; liste des annonces avec projection étroite et couverture seulement ; préfixe Redis réglable ; `X-Correlation-Id` hors du cache de sortie ; DataProtection éphémère ; pool par défaut à 40. Mesures unitaires (API Release seule, une requête à la fois, copie jetable de `immoplus_charge`, sans Redis) :

| Route | Avant (médiane / p95) | Après (médiane / p95) |
|---|---|---|
| admin `/reservation/lister`, 1er appel avec un arriéré de 1 100 séjours | 42 s (≈ 2 200 emails pendant la requête) | 0,3 s (lignes affichées seulement) |
| admin `/reservation/lister`, appels suivants | 22 / 32 ms | 21 / 32 ms |
| `/admin/tableau-de-bord` | 254 / 1 958 ms | 53 / 80 ms |
| `/messages/non-lus` voyageur / hôte | 18 / 17 ms (SQL : 22 ms et 22 ms) | 5 / 9 ms (SQL : 0,13 ms et 2,7 ms) |
| fiche `/appartement/{id}` | 12 / 19 ms | 5 / 9 ms |
| `/appartement/lister` (avec jeton, sans cache) | 24 / 59 ms | 21 / 36 ms |
| `/appartement/lister` carte, 200 annonces | 66 / 102 ms | 22 / 45 ms |

**Résultats après corrections (23/10/2026)**, même scénario, même machine, mêmes paliers (montée 1 min + 3 min stables), pool 40 par instance, cache nginx des photos, maintenance planifiée coupée (`CHARGE_MAINTENANCE_ACTIVE=false`). Requêtes/s et latences mesurées par k6 (côté client, toutes les requêtes du test, recalculées de la même façon pour « avant » à partir des résumés enregistrés) ; CPU = moyenne sur la phase stable, PostgreSQL + les 2 instances (en cœurs), et CPU de toute la pile Docker k6 compris (sur 8). Attention : les mesures « après » ont été faites **sur batterie en mode économie d'énergie** (processeur bridé), celles d'« avant » sur secteur : le gain réel est au moins celui indiqué.

| Palier | | Requêtes/s | p50 | p95 | p99 | Erreurs | CPU PostgreSQL + API | CPU par requête servie | CPU pile entière |
|---|---|---|---|---|---|---|---|---|---|
| 100 | avant | 59 | 15 ms | 427 ms | 1,7 s | 0 % | 2,7 cœurs | 43 ms | 3,6 / 8 |
| 100 | **après** | 65 | **5 ms** | **61 ms** | **265 ms** | 0 % | **1,2 cœur** | **20 ms** | 1,9 / 8 |
| 250 | avant (+ cache photos) | 64 | 256 ms | 6,5 s | 11,6 s | 0 % | 4,7 cœurs | 75 ms | 6,3 / 8 |
| 250 | **après** | **137** | **13 ms** | **1,1 s** | **2,9 s** | 0 % | 3,7 cœurs | **26 ms** | 5,2 / 8 |
| 500 | avant | 35 | 3,3 s | 36 s | 56 s | 0,7 % | 4,2 cœurs | 85 ms | 6,1 / 8 |
| 500 | **après** | **89** | **384 ms** | **7,5 s** | **12,8 s** | **0 %** (aucun 503 ; 1 délai dépassé sur 33 384) | 4,0 cœurs | 50 ms | 6,0 / 8 (dont k6 1,4) |
| 1 000 | avant / après | non mesurable sur ce Mac : k6 seul demanderait ≈ 2,5 Go sur les 4 Go de Docker (1,3 Go à 500) | | | | | | | |
| 100 | après, **maintenance active** (montée 30 s) | 65 | 3 ms | 39 ms | 211 ms | 0 % | — | — | — |

Lecture : à 100 utilisateurs virtuels, p95 divisé par 7 et CPU divisé par 2,2 ; à 250, débit doublé et p95 divisé par 6 ; à 500, plus aucune erreur et p95 divisé par 5. Seuils k6 (p95 < 500 ms, erreurs < 1 %) tenus à 100, dépassés à 250 et au-delà **sur ce Mac** (8 processeurs virtuels partagés par PostgreSQL, les API, k6, l'API de dev, en mode économie d'énergie). Maintenance active : premier passage de rattrapage au démarrage (500 demandes expirées, 391 séjours terminés, 36 locations avec loyers, en 18,6 s, ≈ 1 400 emails par la boîte d'envoi), passage suivant pendant la charge (136 demandes, 0,8 s) sans effet mesurable sur les latences.

Goulots restants (23/10, palier 250) :
- **PostgreSQL consomme ≈ 57 % du CPU** de la pile (≈ 15 ms par requête à 250, ≈ 9 ms à 100) ; à partir de 250, les pools (2 × 40) sont pleins et les requêtes attendent une connexion (jusqu'à 37 en attente à 250, 385 à 500 ; p95 SQL 0,4 s puis 1 s) : c'est la conséquence du processeur saturé, pas un pool trop petit (l'agrandir n'aiderait pas).
- `/appartement/lister` : **≈ 37 % du temps serveur** ; chaque page lance la requête de la page **et** un `count(*)` (jointure annonces/hôtes) : à étudier côté API (total mis en cache par combinaison de filtres, ou « page suivante » sans total), le cache de sortie de 30 s servant peu quand les filtres et dates varient.
- Listes admin avec recherche (`count(*)` + `immoplus_sans_accents(...) ILIKE`, 1,4 à 2,9 s sous charge), `/financement/lister`, `/admin/tableau-de-bord`, `/bailleur/tableau-de-bord` : p95 4 à 5 s à 250 (peu de trafic, mais lourdes).
- Lectures de réservations et **calendrier public** : chaque appel lance encore une recherche d'impayées à annuler sur son périmètre (`ExpirerImpayeesAsync`) ; maintenant que la maintenance planifiée existe, un filtre SQL comme celui de la recherche suffirait (une requête de moins par lecture).
- Limite de mesure : k6 tourne sur la même machine (1,3 Go et 1,4 cœur à 500). Pour mesurer 1 000 utilisateurs, lancer k6 depuis une autre machine.

**Dimensionnement du VPS (mis à jour le 23/10/2026)** : le coût par requête a été divisé par 2 (≈ 20 ms de CPU par requête servie, dont ≈ 9 ms PostgreSQL, à charge normale). Pour le lancement, **4 vCPU / 8 Go** suffisent désormais pour PostgreSQL + 1 ou 2 instances de l'API + Redis + Garage + nginx sur la même machine (avant : 4 vCPU / 16 Go conseillés) : ordre de grandeur 100 à 150 requêtes/s avec p95 < 500 ms, soit l'équivalent de plusieurs centaines d'utilisateurs actifs simultanés (un utilisateur virtuel du test enchaîne une page toutes les 1 à 4 s, bien plus qu'un visiteur réel). 16 Go restent confortables pour un cache disque PostgreSQL plus grand. Réglages : pool 40 par instance, `max_connections` ≥ (instances × 40) + 20 (100 suffit pour 2 instances). Passer à 8 vCPU, ou mettre PostgreSQL sur un serveur à part, quand Grafana montre un CPU > 70 % ou un p95 > 300 ms en continu.

## Vrais SMS en local (téléphone Android)

Par défaut, les SMS sont **simulés** (Admin › SMS envoyés). Pour envoyer de vrais SMS avec ta propre carte SIM :

1. Installer sur un téléphone Android l'application open source **SMS Gateway for Android** (capcom6, « SMS Gate »), lui accorder la permission d'envoyer des SMS et activer le **serveur local** : elle affiche une adresse (ex. `http://192.168.1.20:8080`), un identifiant et un mot de passe. Le téléphone et le Mac doivent être sur le **même Wi-Fi**.
2. Copier `api/ImmoPlus.Api/sms.env.exemple` en `api/ImmoPlus.Api/sms.env` (non versionné) et y mettre l'adresse, l'identifiant, le mot de passe et les numéros autorisés.
3. `docker compose up -d api`.

Sécurité : seuls les numéros de `Sms__NumerosAutorises__N` reçoivent un vrai SMS (les autres sont seulement tracés), sauf si `Sms__EnvoyerATous=true`. Chaque SMS est facturé sur le forfait du téléphone. Téléphone éteint ou injoignable : le SMS est marqué « Échec », l'action continue normalement. Pour revenir à la simulation : supprimer `sms.env` puis `docker compose up -d api`.

## Lancer les tests

Les tests automatisés de l'API (`api/ImmoPlus.Api.Tests`, xUnit) démarrent l'API complète en mémoire (`WebApplicationFactory`) et l'appellent par HTTP, comme le front :

- base PostgreSQL **temporaire** pour chaque classe de tests (`immoplus_test_…`, créée vide, migrée, puis supprimée à la fin) : la base réelle `immoplus` n'est jamais ouverte, l'API Docker peut tourner en même temps. Par défaut, le serveur PostgreSQL est un conteneur jetable `postgres:17-alpine` lancé par Testcontainers (Docker Desktop doit tourner) et détruit en fin de lancement ;
- emails envoyés à un faux serveur SMTP interne aux tests (rien ne part vers MailHog) ;
- services planifiés (rappels, boîte d'envoi) désactivés : les tests les déclenchent eux-mêmes (`EmailsPourAsync` vide la boîte d'envoi avant de lire les emails reçus) ; les délais (24 h, 48 h, fin de séjour, préavis) sont simulés en reculant les dates dans la base de test ;
- stockage S3 et Redis testés contre des conteneurs jetables **Garage** (`dxflrs/garage:v2.4.1`) et **Redis** (`redis:8.8-alpine`), démarrés une fois par lancement (jamais les services du compose) : `StockageS3Tests`, `RedisTests` (compteurs et cache partagés entre deux instances), `BoiteEnvoiTests` (envoi, nouvelles tentatives, abandon, pas de doublon avec deux dépileurs concurrents), `ServicesPlanifiesTests` (un seul passage avec plusieurs instances), `PaginationPerformancesTests` (pagination, onglets, cache de sortie, compression, cache des photos, santé, corrélation, métriques), `ConfirmationEmailTests` (inscription → lien de confirmation → admins prévenus → validation → email ; jetons expiré/remplacé/inconnu, renvoi neutre et limité, migration des comptes existants). Les comptes de test (`Donnees.CompteAsync`) passent par la confirmation d'email, jeton lu dans la boîte d'envoi. 139 tests au 26/10/2026.

Sur le Mac (SDK .NET 10) :

```bash
cd api/ImmoPlus.Api.Tests
dotnet test                                   # tous les tests (≈ 1 min, + démarrage du conteneur PostgreSQL)
dotnet test --filter "FullyQualifiedName~AnnulationTests"   # une seule classe
```

Sans Testcontainers, sur le service `postgres` de Docker Compose (les tests n'y créent et suppriment que des bases `immoplus_test_…`) :

```bash
cd api/ImmoPlus.Api.Tests
IMMOPLUS_TESTS_POSTGRES="Host=localhost;Port=5433;Username=immoplus;Database=postgres;Password=$(grep '^POSTGRES_PASSWORD=' ../../infra/postgres.env | cut -d= -f2-)" dotnet test
```

Dans Docker (sans SDK local), avec un conteneur jetable branché sur le réseau de Compose — les volumes anonymes sur `bin/` et `obj/` évitent de mélanger les fichiers de compilation Linux et macOS :

```bash
docker run --rm -v "$PWD/api:/src" --network immoplus_default --env-file infra/postgres.env \
  -v /src/ImmoPlus.Api/bin -v /src/ImmoPlus.Api/obj -v /src/ImmoPlus.Api.Tests/bin -v /src/ImmoPlus.Api.Tests/obj \
  -w /src/ImmoPlus.Api.Tests mcr.microsoft.com/dotnet/sdk:10.0 \
  sh -c 'IMMOPLUS_TESTS_POSTGRES="Host=postgres;Port=5432;Username=immoplus;Database=postgres;Password=$POSTGRES_PASSWORD" dotnet test'
```

Le dossier de tests est à côté de `api/ImmoPlus.Api` (contexte de build Docker de l'API) : il n'entre pas dans l'image de l'API. Un test marqué `Skip = "Bug : …"` signale un bug connu de l'API, à corriger avant de le réactiver.

## Notes de sécurité (dev → prod)

- La clé JWT de `appsettings.json` est **publique** (dépôt GitHub) : valeur de développement uniquement. La production utilise sa propre clé (`Jwt__Cle`, 128 caractères aléatoires) dans `/etc/immoplus/immoplus.env` (root, 600), jamais commitée.
- Les origines CORS (`Cors:OriginesAutorisees`) sont définies par environnement (`Cors__OriginesAutorisees__0…` en production).
- Les fichiers déposés (photos, documents, états des lieux, pièces d'identité) ne sont jamais servis en statique : accès uniquement par les routes de l'API. En production : `/var/lib/immoplus/storage` (utilisateur `immoplus`, 700).

## Déploiement (VPS Hostinger 168.231.77.68) — version légère

Une seule instance de l'API, PostgreSQL natif du serveur, fichiers sur disque, pas de Docker ni de Redis. Même VPS que l'ancien projet **financement** (`financement-api`, port 5000, site nginx `financement-api`, base `financement`) : ne jamais y toucher.

| | Emplacement |
|---|---|
| Adresses | http://immoplus.cloud (dès que le DNS pointe vers le VPS ; HTTPS ensuite) et, en attendant, http://168.231.77.68:8081 |
| API | service systemd `immoplus-api`, utilisateur `immoplus`, **127.0.0.1:5050** uniquement (derrière nginx) |
| Code publié | `/opt/immoplus/current` → `/opt/immoplus/releases/<date>-<commit>` (5 versions gardées) |
| Configuration et secrets | `/etc/immoplus/immoplus.env` (root, 600) — modèle : `infra/production/immoplus.env.exemple` |
| Base | PostgreSQL 18 local, base et rôle `immoplus` (non superutilisateur), extensions `unaccent` et `pg_trgm` |
| Fichiers | `/var/lib/immoplus/storage` (`Stockage__Disque__Dossier` ; défaut `Storage/` à côté du programme, d'où l'ancien lien `Storage` dans chaque version) |
| Client React | `/var/www/immoplus-react` (build Vite, `VITE_API_URL=/api`) |
| nginx | `/etc/nginx/sites-available/immoplus` + `/etc/nginx/snippets/immoplus-commun.conf` : `/api/` → API sans le préfixe, WebSockets `/api/hubs/`, `/api/metriques` bloqué (404), cache des photos publiques, repli SPA |
| Sauvegardes | `immoplus-sauvegarde.timer` chaque nuit vers 3 h 30 : `/var/backups/immoplus/postgres/immoplus-*.dump` (pg_dump -Fc, visibles dans la page admin) et `/var/backups/immoplus/fichiers/storage-*.tar.gz`, 14 jours |
| Dépôt | `/var/www/immoplus-git` (branche `main`) |
| Ancienne installation (SQLite, Blazor) | archivée dans `/var/backups/immoplus-ancien-20261023/` |

Fichiers sources : `infra/production/` (installation : `installer-serveur.sh`, ré-exécutable sans danger).

**Mettre à jour l'API** (après un push sur `main`) — sauvegarde, `git pull`, publication Release, redémarrage (migrations au démarrage), contrôle `/sante/pret`, retour automatique à la version précédente en cas d'échec :
```bash
ssh root@168.231.77.68 immoplus-deployer
ssh root@168.231.77.68 immoplus-deployer --retour   # revenir à la version précédente (le code seulement)
```
Si `infra/production/deployer.sh` (ou un autre script) change, le réinstaller : `ssh root@168.231.77.68 bash /var/www/immoplus-git/infra/production/installer-serveur.sh`.

**Mettre à jour le client** (depuis le Mac, dépôt sur le même commit que l'API) :
```bash
infra/production/deployer-front.sh
```

**Sauvegarde / restauration** :
```bash
ssh root@168.231.77.68 systemctl start immoplus-sauvegarde       # sauvegarde immédiate
ssh -t root@168.231.77.68 immoplus-restaurer /var/backups/immoplus/postgres/immoplus-AAAAMMJJ-HHMM.dump [/var/backups/immoplus/fichiers/storage-AAAAMMJJ-HHMM.tar.gz]
# vérifier une sauvegarde sans toucher à la production :
ssh -t root@168.231.77.68 BASE=essai_restauration immoplus-restaurer <fichier.dump>   # puis : sudo -u postgres dropdb essai_restauration
# copie hors du serveur (les sauvegardes sont sur le même disque que les données) :
rsync -az root@168.231.77.68:/var/backups/immoplus/ ~/Sauvegardes/immoplus-vps/
```
La restauration fait d'abord une sauvegarde de sécurité de la base actuelle (`immoplus-avant-restauration-*.dump`).

**Modifier un réglage ou un secret** (mot de passe SMTP, URL du site…) : `sudo nano /etc/immoplus/immoplus.env` puis `sudo systemctl restart immoplus-api`.

**Emails (Hostinger)** : dans `/etc/immoplus/immoplus.env`, `Smtp__Host=smtp.hostinger.com`, `Smtp__Port=465`, `Smtp__Securite=SslImplicite` (ou 587 + `StartTls`), `Smtp__Utilisateur=noreply@mail.immoplus.cloud`, `Smtp__MotDePasse=…`, `Smtp__Expediteur=noreply@mail.immoplus.cloud` (même adresse que l'identifiant), puis redémarrer l'API. Copie de chaque email dans « Envoyés » de cette boîte : `Imap__Actif=true`, `Imap__Host=imap.hostinger.com`, `Imap__Port=993`, `Imap__Securite=SslImplicite` (identifiant et mot de passe repris du SMTP, dossier détecté ; voir « Copie dans « Envoyés » (IMAP) »). Vérifier : `journalctl -u immoplus-api | grep -E 'SMTP|IMAP'` (lignes de démarrage), puis la page admin « Messages sortants » ; les messages `Abandonne` d'avant la configuration se relancent par le bouton « Relancer » (ou `POST /api/admin/messages-sortants/relancer?statut=Abandonne`).

**Derrière nginx** : `RedirectionHttps__Actif=false` (nginx termine HTTPS ; évite l'avertissement « Failed to determine the https port »).

**HTTPS** (dès que `immoplus.cloud` et `www` pointent vers 168.231.77.68) :
```bash
certbot --nginx -d immoplus.cloud -d www.immoplus.cloud --redirect -m <email> --agree-tos --no-eff-email
# puis dans /etc/immoplus/immoplus.env : Frontend__Url=https://immoplus.cloud ; systemctl restart immoplus-api
# et fermer l'accès en clair par l'IP : supprimer le bloc « listen 8081 » de /etc/nginx/sites-available/immoplus, nginx -t && systemctl reload nginx
```

**Premier administrateur** (base vide) : tant qu'aucun admin actif n'existe, l'inscription avec le rôle `Admin` est acceptée et validée d'office (`POST /api/utilisateur/inscription`, `"role": "Admin"`) ; ensuite seul un admin crée un admin.

**Journaux et contrôles** :
```bash
journalctl -u immoplus-api -f                     # journaux JSON de l'API
tail -f /var/log/nginx/immoplus.access.log        # accès (avec l'identifiant de corrélation cid=)
curl -s http://127.0.0.1:5050/sante/pret          # sur le VPS
# métriques (jamais publiées par nginx), sur le VPS :
curl -H "Authorization: Bearer $(grep ^Metriques__Jeton= /etc/immoplus/immoplus.env | cut -d= -f2)" http://127.0.0.1:5050/metriques
```

## Reste à faire

- **DNS** : enregistrements A de `immoplus.cloud` et `www` vers 168.231.77.68, puis HTTPS (ci-dessus).
- **Emails** : l'API sait désormais s'authentifier et chiffrer (MailKit, 465 / 587) ; reste à déployer cette version et à renseigner les réglages Hostinger (ci-dessus), puis relancer les messages abandonnés.
- Étape 2 de la montée en charge (S3, Redis, plusieurs instances) : faite en local, non utilisée par la version légère du VPS.
