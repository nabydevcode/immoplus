# ImmoPlus

Plateforme immobilière : location longue durée (candidature/dossier/garant/financement) et séjours courts type Airbnb.

## Structure

- `api/ImmoPlus.Api/` — API ASP.NET Core Minimal API (.NET 10), EF Core + SQLite, JWT + BCrypt, Swagger.
- `web/ImmoPlus.Web/` — Client Blazor WebAssembly + Tailwind CSS v3.
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
