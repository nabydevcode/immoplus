#!/usr/bin/env bash
# Déploiement du client React sur le VPS, depuis le Mac (racine du dépôt, à jour sur le même commit que l'API) :
#   infra/production/deployer-front.sh
# Build Vite avec VITE_API_URL=/api (nginx fait suivre /api/ vers l'API), puis envoi par rsync dans
# /var/www/immoplus-react/ : d'abord les nouveaux fichiers « assets », ensuite index.html, puis suppression des
# anciens fichiers (les visiteurs en cours de navigation ne tombent pas sur des fichiers manquants).
# Retour arrière : relancer ce script depuis le commit précédent (git checkout <commit>).
set -euo pipefail

SERVEUR="${SERVEUR:-root@168.231.77.68}"
DESTINATION=/var/www/immoplus-react
RACINE="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$RACINE/web/immoplus-web-react"

echo "Commit : $(git log -1 --format='%h %s')"
[ -z "$(git status --porcelain -- .)" ] || echo "ATTENTION : modifications non commitées dans web/immoplus-web-react (elles seront déployées)."

npm ci --no-audit --no-fund
VITE_API_URL=/api npm run build

ssh "$SERVEUR" "mkdir -p $DESTINATION"
rsync -az dist/assets/ "$SERVEUR:$DESTINATION/assets/"
rsync -az --delete-after dist/ "$SERVEUR:$DESTINATION/"
ssh "$SERVEUR" "chown -R root:root $DESTINATION && chmod -R u=rwX,go=rX $DESTINATION"
echo "Client déployé. Vérifier : curl -I https://immoplus.cloud/ (ou http://168.231.77.68:8081/)"
