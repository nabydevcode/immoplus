#!/bin/sh
# Crée les fichiers de secrets locaux manquants à partir des modèles infra/*.env.exemple, avec des valeurs aléatoires.
# N'écrase JAMAIS un fichier existant, n'affiche aucun secret. Fichiers créés en droits 600.
#   ./infra/initialiser-secrets.sh
# (infra/postgres.env n'est pas concerné : il existe déjà et son mot de passe est lié au volume postgres-data.)
set -eu
cd "$(dirname "$0")"
umask 077

hex() { openssl rand -hex "$1"; }

creer() { # fichier contenu
  if [ -e "$1" ]; then
    echo "infra/$1 existe déjà : conservé"
  else
    printf '%s\n' "$2" > "$1"
    chmod 600 "$1"
    echo "infra/$1 créé (droits 600)"
  fi
}

modele() { grep '^#' "$1.exemple"; }

creer garage.env "$(modele garage.env)
GARAGE_RPC_SECRET=$(hex 32)
GARAGE_ADMIN_TOKEN=$(hex 32)
GARAGE_METRICS_TOKEN=$(hex 32)
S3_BUCKET=immoplus
SAUVEGARDE_S3_CLE_ACCES=GK$(hex 12)
SAUVEGARDE_S3_CLE_SECRETE=$(hex 32)"

creer stockage.env "$(modele stockage.env)
Stockage__Mode=Disque
Stockage__S3__Url=http://garage:9000
Stockage__S3__Bucket=immoplus
Stockage__S3__Region=us-east-1
Stockage__S3__CheminStyle=true
Stockage__S3__CleAcces=GK$(hex 12)
Stockage__S3__CleSecrete=$(hex 32)"

creer redis.env "$(modele redis.env)
REDIS_PASSWORD=$(hex 32)
Redis__Connexion="

creer echelle.env "$(modele echelle.env)
Jwt__Cle=$(openssl rand -base64 64 | tr -d '\n')"

creer metriques.env "$(modele metriques.env)
Metriques__Jeton=$(hex 32)"

creer grafana.env "$(modele grafana.env)
GF_SECURITY_ADMIN_USER=admin
GF_SECURITY_ADMIN_PASSWORD=$(openssl rand -base64 24 | tr -d '/+=\n')"
