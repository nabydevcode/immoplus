
```

## Reste à faire

- **Nom de domaine + HTTPS** : bloqué tant qu'aucun domaine n'est choisi/acheté — dès qu'un domaine pointe vers `168.231.77.68`, ajouter un reverse-proxy nginx (port 80/443) devant l'API et le client, puis `certbot` pour le TLS.
- Migration SQLite → PostgreSQL en production (PostgreSQL est déjà installé sur le VPS mais pas encore branché à ImmoPlus).
- Restyler/finaliser des parcours secondaires si besoin (ex. tableau de bord Admin dédié).
