#!/usr/bin/env bash
# Restaure une sauvegarde pg_dump (-Fc) dans la base PostgreSQL locale « immoplus » (Docker Compose).
#
# Usage (depuis n'importe où dans le repo) :
#   infra/restaurer-postgres.sh api/ImmoPlus.Api/Sauvegardes/postgres/immoplus-20261019-0300.dump
#   infra/restaurer-postgres.sh --oui <fichier>      # sans question de confirmation
#   BASE=essai_restauration infra/restaurer-postgres.sh <fichier>   # vérifier une sauvegarde dans une base à part
#     (puis : docker compose exec postgres dropdb -U immoplus essai_restauration)
#
# Étapes : 1. contrôle du fichier (pg_restore --list)  2. arrêt de l'API (plus aucune écriture)
#          3. sauvegarde de sécurité de la base actuelle (immoplus-avant-restauration-*.dump)
#          4. suppression/recréation de la base  5. pg_restore  6. redémarrage de l'API.
# La base actuelle est REMPLACÉE ; la sauvegarde de sécurité de l'étape 3 permet de revenir en arrière
# (relancer ce script avec ce fichier).
set -euo pipefail

RACINE="$(cd "$(dirname "$0")/.." && pwd)"
cd "$RACINE"
DOSSIER_SAUVEGARDES="api/ImmoPlus.Api/Sauvegardes/postgres"
# Base cible (immoplus par défaut). Une autre valeur (ex. BASE=essai_restauration) restaure dans une base
# à part pour vérifier une sauvegarde, sans arrêter l'API ni toucher à « immoplus ».
BASE="${BASE:-immoplus}"
[[ "$BASE" =~ ^[a-z_][a-z0-9_]*$ ]] || { echo "Nom de base invalide : $BASE" >&2; exit 1; }

confirmer=1
if [ "${1:-}" = "--oui" ]; then confirmer=0; shift; fi
FICHIER="${1:-}"
if [ -z "$FICHIER" ]; then
  echo "Usage : $0 [--oui] <fichier.dump>" >&2
  echo "Sauvegardes disponibles :" >&2
  ls -1t "$DOSSIER_SAUVEGARDES"/*.dump 2>/dev/null | head -n 20 >&2 || echo "  (aucune)" >&2
  exit 1
fi
# Chemin relatif au dossier d'appel ou à la racine du repo.
if [ ! -f "$FICHIER" ] && [ -f "$OLDPWD/$FICHIER" ]; then FICHIER="$OLDPWD/$FICHIER"; fi
[ -f "$FICHIER" ] || { echo "Fichier introuvable : $FICHIER" >&2; exit 1; }

psql_admin() { docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U immoplus -d postgres "$@"; }

echo "1/6 Contrôle de la sauvegarde…"
docker compose up -d --wait postgres >/dev/null 2>&1 || { echo "PostgreSQL ne démarre pas (docker compose logs postgres)." >&2; exit 1; }
docker compose exec -T postgres pg_restore --list < "$FICHIER" > /dev/null \
  || { echo "Ce fichier n'est pas une sauvegarde pg_dump -Fc valide." >&2; exit 1; }

if [ "$confirmer" = 1 ]; then
  read -r -p "La base locale « $BASE » va être remplacée par $(basename "$FICHIER"). Continuer ? [o/N] " rep
  [ "$rep" = "o" ] || [ "$rep" = "O" ] || { echo "Annulé."; exit 1; }
fi

api_arretee=0
if [ "$BASE" = "immoplus" ] && [ -n "$(docker compose ps -q --status running api)" ]; then
  echo "2/6 Arrêt de l'API…"
  docker compose stop api >/dev/null
  api_arretee=1
else
  echo "2/6 (API non concernée ou déjà arrêtée)"
fi

SECURITE=""
if [ -n "$(psql_admin -tAc "SELECT 1 FROM pg_database WHERE datname='$BASE'")" ]; then
  echo "3/6 Sauvegarde de sécurité de la base actuelle…"
  mkdir -p "$DOSSIER_SAUVEGARDES"
  SECURITE="$DOSSIER_SAUVEGARDES/$BASE-avant-restauration-$(date -u '+%Y%m%d-%H%M%S').dump"
  ( umask 077; docker compose exec -T postgres pg_dump -Fc --no-owner -U immoplus "$BASE" > "$SECURITE" )
  echo "    → $SECURITE"
else
  echo "3/6 (la base $BASE n'existe pas encore : pas de sauvegarde de sécurité)"
fi

echo "4/6 Recréation de la base…"
psql_admin -c "DROP DATABASE IF EXISTS $BASE WITH (FORCE);" -c "CREATE DATABASE $BASE OWNER immoplus;" >/dev/null

echo "5/6 Restauration…"
docker compose exec -T postgres pg_restore --no-owner --exit-on-error -U immoplus -d "$BASE" < "$FICHIER"

if [ "$api_arretee" = 1 ]; then
  echo "6/6 Redémarrage de l'API (les migrations manquantes s'appliquent au démarrage)…"
  docker compose start api >/dev/null
else
  echo "6/6 (pas de redémarrage de l'API)"
fi
echo "Restauration terminée dans la base « $BASE »."
[ -z "$SECURITE" ] || echo "Retour arrière possible : BASE=$BASE $0 $SECURITE"
