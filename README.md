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

- La clé JWT (`Jwt:Cle` dans `appsettings.json`) est une valeur de développement. En production, la remplacer via une variable d'environnement (`Jwt__Cle`) et ne jamais commiter la clé de prod.
- Les origines CORS (`Cors:OriginesAutorisees`) sont à mettre à jour avec le domaine de production lors du déploiement.
- Les documents uploadés sont stockés localement dans `api/ImmoPlus.Api/Storage/documents/` (hors `wwwroot`, jamais servi en statique — accès uniquement via route authentifiée). Ce dossier est exclu de Git.

## État d'avancement

Voir le plan de développement complet (phasage par lots) — actuellement : **Lot 1 (socle)** terminé : modèle de données complet, auth, utilisateur, appartement (avec `TypeOffre`), client Blazor avec design ImmoPlus.
