#!/usr/bin/env bash
# Restauration ImmoPlus sur le VPS (installé dans /usr/local/sbin/immoplus-restaurer). À lancer en root.
#
#   immoplus-restaurer /var/backups/immoplus/postgres/immoplus-20261024-0330.dump
#   immoplus-restaurer /var/backups/immoplus/postgres/immoplus-20261024-0330.dump /var/backups/immoplus/fichiers/storage-20261024-0330.tar.gz
#   BASE=essai_restauration immoplus-restaurer <fichier.dump>   # vérifier une sauvegarde dans une base à part
#        (puis : sudo -u postgres dropdb essai_restauration)
#
# Étapes : contrôle du fichier, arrêt de l'API, sauvegarde de sécurité de la base actuelle
# (immoplus-avant-restauration-*.dump, et des fichiers si une archive est fournie), recréation de la base,
# pg_restore, remise en place des fichiers (si archive fournie), redémarrage de l'API et contrôle /sante/pret.
set -euo pipefail

BASE="${BASE:-immoplus}"
[[ "$BASE" =~ ^[a-z_][a-z0-9_]*$ ]] || { echo "Nom de base invalide : $BASE" >&2; exit 1; }
DUMP="${1:-}"
ARCHIVE="${2:-}"
DOSSIER=/var/backups/immoplus
STOCKAGE=/var/lib/immoplus/storage

if [ -z "$DUMP" ]; then
  echo "Usage : $0 <fichier.dump> [archive-storage.tar.gz]" >&2
  ls -1t "$DOSSIER"/postgres/*.dump "$DOSSIER"/fichiers/*.tar.gz 2>/dev/null | head -n 20 >&2 || true
  exit 1
fi
[ -f "$DUMP" ] || { echo "Fichier introuvable : $DUMP" >&2; exit 1; }
[ -z "$ARCHIVE" ] || [ -f "$ARCHIVE" ] || { echo "Archive introuvable : $ARCHIVE" >&2; exit 1; }
sudo -u postgres pg_restore --list < "$DUMP" > /dev/null || { echo "Sauvegarde pg_dump -Fc invalide." >&2; exit 1; }

read -r -p "La base « $BASE » va être REMPLACÉE par $(basename "$DUMP")$([ -n "$ARCHIVE" ] && echo " et les fichiers par $(basename "$ARCHIVE")"). Continuer ? [o/N] " rep
[ "$rep" = "o" ] || [ "$rep" = "O" ] || { echo "Annulé."; exit 1; }

horodatage="$(date +%Y%m%d-%H%M%S)"
if [ "$BASE" = "immoplus" ]; then
  echo "Arrêt de l'API…"
  systemctl stop immoplus-api
fi

if sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='$BASE'" | grep -q 1; then
  securite="$DOSSIER/postgres/immoplus-avant-restauration-$horodatage.dump"
  echo "Sauvegarde de sécurité : $securite"
  sudo -u postgres pg_dump -Fc "$BASE" > "$securite"
  chown root:immoplus "$securite"; chmod 640 "$securite"
fi

echo "Recréation de la base $BASE…"
sudo -u postgres psql -v ON_ERROR_STOP=1 -q <<SQL
SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$BASE' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS $BASE;
CREATE DATABASE $BASE OWNER immoplus ENCODING 'UTF8' TEMPLATE template0;
SQL
# Extensions créées par le superutilisateur (le rôle immoplus n'en a pas le droit), puis tout le reste en tant
# qu'immoplus (propriétaire de chaque objet), sans les entrées EXTENSION de la sauvegarde.
sudo -u postgres psql -v ON_ERROR_STOP=1 -q -d "$BASE" -c "CREATE EXTENSION IF NOT EXISTS unaccent; CREATE EXTENSION IF NOT EXISTS pg_trgm;"
liste="$(mktemp)"
trap 'rm -f "$liste"' EXIT
sudo -u postgres pg_restore --list < "$DUMP" | grep -v -E ' EXTENSION | COMMENT - EXTENSION ' > "$liste"
chmod 644 "$liste"
sudo -u postgres pg_restore --no-owner --role=immoplus --exit-on-error -L "$liste" -d "$BASE" < "$DUMP"

if [ -n "$ARCHIVE" ] && [ "$BASE" = "immoplus" ]; then
  echo "Remise en place des fichiers (ancien dossier conservé : ${STOCKAGE}.avant-restauration-$horodatage)…"
  mv "$STOCKAGE" "${STOCKAGE}.avant-restauration-$horodatage"
  tar -C "$(dirname "$STOCKAGE")" -xzf "$ARCHIVE"
  chown -R immoplus:immoplus "$STOCKAGE"
  chmod 700 "$STOCKAGE"
fi

if [ "$BASE" = "immoplus" ]; then
  echo "Redémarrage de l'API…"
  systemctl start immoplus-api
  for _ in $(seq 1 30); do
    if curl -fs -o /dev/null --max-time 5 http://127.0.0.1:5050/sante/pret; then echo "API prête."; exit 0; fi
    sleep 3
  done
  echo "L'API ne répond pas prête : journalctl -u immoplus-api -n 100" >&2
  exit 1
fi
echo "Restauration dans $BASE terminée."
