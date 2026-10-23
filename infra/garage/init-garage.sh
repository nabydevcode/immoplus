#!/bin/sh
# Initialisation idempotente du stockage objet Garage d'ImmoPlus (service compose « garage-init », rejouable sans risque) :
#   1. attribue un rôle au nœud unique (layout) s'il n'en a pas encore ;
#   2. crée le bucket privé « immoplus » (aucun accès anonyme : Garage n'ouvre un bucket qu'en mode « website », jamais activé ici) ;
#   3. importe la clé applicative (lecture + écriture sur ce seul bucket, pas de création de bucket) ;
#   4. importe la clé de sauvegarde (lecture seule sur ce seul bucket).
# Tout passe par l'API d'administration de Garage (jeton GARAGE_ADMIN_TOKEN), aucun secret n'est affiché.
#
# Variables : GARAGE_ADMIN_URL, GARAGE_ADMIN_TOKEN, S3_BUCKET (infra/garage.env)
#             Stockage__S3__CleAcces / Stockage__S3__CleSecrete (infra/stockage.env : clé de l'API)
#             SAUVEGARDE_S3_CLE_ACCES / SAUVEGARDE_S3_CLE_SECRETE (infra/garage.env : clé de sauvegarde)
set -eu

URL="${GARAGE_ADMIN_URL:-http://garage:3903}"
JETON="${GARAGE_ADMIN_TOKEN:?GARAGE_ADMIN_TOKEN manquant (infra/garage.env)}"
BUCKET="${S3_BUCKET:-immoplus}"
CAPACITE_OCTETS="${GARAGE_CAPACITE_OCTETS:-53687091200}" # 50 Go : simple pondération sur un nœud unique

journal() { echo "$(date -u '+%Y-%m-%dT%H:%M:%SZ') [garage-init] $*"; }

# Appel de l'API d'administration. Usage : api METHODE /v2/Route [corps-json]. Échoue (code ≠ 0) si HTTP ≥ 400.
api() {
  if [ -n "${3:-}" ]; then
    curl -sS --fail-with-body -X "$1" -H "Authorization: Bearer $JETON" -H 'Content-Type: application/json' \
      --data "$3" "$URL$2"
  else
    curl -sS --fail-with-body -X "$1" -H "Authorization: Bearer $JETON" "$URL$2"
  fi
}

# Code HTTP seul (pour tester l'existence d'un objet sans échouer).
code_http() {
  curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $JETON" "$URL$1"
}

verifier_cle() { # nom id secret
  case "$2" in
    GK[0-9a-f]*) [ "${#2}" -eq 26 ] || { journal "ERREUR : identifiant de la clé $1 invalide (GK + 24 caractères hexadécimaux)"; exit 1; } ;;
    *) journal "ERREUR : identifiant de la clé $1 invalide (doit commencer par GK)"; exit 1 ;;
  esac
  [ "${#3}" -eq 64 ] || { journal "ERREUR : secret de la clé $1 invalide (64 caractères hexadécimaux)"; exit 1; }
}

importer_cle() { # nom id secret
  if [ "$(code_http "/v2/GetKeyInfo?id=$2")" = "200" ]; then
    journal "clé $1 déjà présente"
  else
    api POST /v2/ImportKey "$(jq -nc --arg n "$1" --arg i "$2" --arg s "$3" '{name:$n, accessKeyId:$i, secretAccessKey:$s}')" >/dev/null
    journal "clé $1 importée"
  fi
  # Jamais de droit de création de bucket pour les clés applicatives.
  api POST "/v2/UpdateKey?id=$2" '{"deny":{"createBucket":true}}' >/dev/null
}

autoriser() { # id-bucket id-cle lecture ecriture
  api POST /v2/AllowBucketKey "$(jq -nc --arg b "$1" --arg k "$2" --argjson r "$3" --argjson w "$4" \
    '{bucketId:$b, accessKeyId:$k, permissions:{read:$r, write:$w, owner:false}}')" >/dev/null
  # Retire explicitement les droits non voulus (ex. écriture accordée à tort lors d'un essai précédent).
  api POST /v2/DenyBucketKey "$(jq -nc --arg b "$1" --arg k "$2" --argjson r "$3" --argjson w "$4" \
    '{bucketId:$b, accessKeyId:$k, permissions:{read:($r|not), write:($w|not), owner:true}}')" >/dev/null
}

CLE_APP_ID="${Stockage__S3__CleAcces:?Stockage__S3__CleAcces manquant (infra/stockage.env)}"
CLE_APP_SECRET="${Stockage__S3__CleSecrete:?Stockage__S3__CleSecrete manquant (infra/stockage.env)}"
CLE_SAUV_ID="${SAUVEGARDE_S3_CLE_ACCES:?SAUVEGARDE_S3_CLE_ACCES manquant (infra/garage.env)}"
CLE_SAUV_SECRET="${SAUVEGARDE_S3_CLE_SECRETE:?SAUVEGARDE_S3_CLE_SECRETE manquant (infra/garage.env)}"
verifier_cle immoplus-api "$CLE_APP_ID" "$CLE_APP_SECRET"
verifier_cle immoplus-sauvegarde "$CLE_SAUV_ID" "$CLE_SAUV_SECRET"

until [ "$(code_http /v2/GetClusterStatus)" = "200" ]; do journal "en attente de Garage…"; sleep 2; done

# 1. Layout : rôle unique (zone dc1) si le nœud n'en a pas.
statut=$(api GET /v2/GetClusterStatus)
noeud=$(echo "$statut" | jq -r '.nodes[0].id')
if [ "$(echo "$statut" | jq -r '.nodes[0].role')" = "null" ]; then
  version=$(api GET /v2/GetClusterLayout | jq -r '.version')
  api POST /v2/UpdateClusterLayout "$(jq -nc --arg id "$noeud" --argjson c "$CAPACITE_OCTETS" \
    '{roles:[{id:$id, zone:"dc1", capacity:$c, tags:["immoplus"]}]}')" >/dev/null
  api POST /v2/ApplyClusterLayout "$(jq -nc --argjson v "$((version + 1))" '{version:$v}')" >/dev/null
  journal "layout appliqué (version $((version + 1)))"
else
  journal "layout déjà en place"
fi

# Attend que le stockage accepte lectures et écritures.
until [ "$(curl -s -o /dev/null -w '%{http_code}' "$URL/health")" = "200" ]; do journal "en attente du quorum…"; sleep 2; done

# 2. Bucket privé.
if [ "$(code_http "/v2/GetBucketInfo?globalAlias=$BUCKET")" != "200" ]; then
  api POST /v2/CreateBucket "$(jq -nc --arg a "$BUCKET" '{globalAlias:$a}')" >/dev/null
  journal "bucket $BUCKET créé"
else
  journal "bucket $BUCKET déjà présent"
fi
infos=$(api GET "/v2/GetBucketInfo?globalAlias=$BUCKET")
bucket_id=$(echo "$infos" | jq -r '.id')
if [ "$(echo "$infos" | jq -r '.websiteAccess')" = "true" ]; then
  journal "ERREUR : le bucket $BUCKET est publié en mode website (accès anonyme) : désactivez-le" >&2
  exit 1
fi

# 3 et 4. Clés applicative (lecture/écriture) et de sauvegarde (lecture seule), limitées à ce bucket.
importer_cle immoplus-api "$CLE_APP_ID" "$CLE_APP_SECRET"
autoriser "$bucket_id" "$CLE_APP_ID" true true
importer_cle immoplus-sauvegarde "$CLE_SAUV_ID" "$CLE_SAUV_SECRET"
autoriser "$bucket_id" "$CLE_SAUV_ID" true false

journal "prêt : bucket $BUCKET privé, clé API (lecture/écriture), clé sauvegarde (lecture seule)"
