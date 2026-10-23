#!/usr/bin/env bash
# Sauvegarde ImmoPlus sur le VPS (installé dans /usr/local/sbin/immoplus-sauvegarde, lancé chaque nuit par
# immoplus-sauvegarde.timer, et par deployer.sh avant chaque mise à jour). À lancer en root.
#
#   /var/backups/immoplus/postgres/immoplus-AAAAMMJJ-HHMM.dump   pg_dump -Fc de la base « immoplus »
#        (root:immoplus 640 : la page admin de l'API les liste et les propose au téléchargement)
#   /var/backups/immoplus/fichiers/storage-AAAAMMJJ-HHMM.tar.gz   photos, documents, pièces d'identité (root 600)
#
# Rétention : 14 jours. Les sauvegardes restent sur le même disque que les données : en copier régulièrement
# hors du serveur (voir README, section « Déploiement »).
set -euo pipefail

RACINE=/var/backups/immoplus
STOCKAGE=/var/lib/immoplus/storage
BASE=immoplus
RETENTION_JOURS=14
horodatage="$(date +%Y%m%d-%H%M)"

install -d -m 750 -o root -g immoplus "$RACINE" "$RACINE/postgres"
install -d -m 700 -o root -g root "$RACINE/fichiers"

# 1. Base : écrite en .tmp (ignoré par l'API) puis renommée une fois complète et vérifiée.
dump="$RACINE/postgres/$BASE-$horodatage.dump"
sudo -u postgres pg_dump -Fc "$BASE" > "$dump.tmp"
sudo -u postgres pg_restore --list < "$dump.tmp" > /dev/null
chown root:immoplus "$dump.tmp"
chmod 640 "$dump.tmp"
mv -f "$dump.tmp" "$dump"

# 2. Fichiers déposés par les utilisateurs.
archive="$RACINE/fichiers/storage-$horodatage.tar.gz"
tar -C "$(dirname "$STOCKAGE")" -czf "$archive.tmp" "$(basename "$STOCKAGE")"
chmod 600 "$archive.tmp"
mv -f "$archive.tmp" "$archive"

# 3. Rétention (uniquement les fichiers produits par ce script).
find "$RACINE/postgres" -maxdepth 1 -type f -name "$BASE-*.dump" -mtime +$((RETENTION_JOURS - 1)) -delete
find "$RACINE/fichiers" -maxdepth 1 -type f -name 'storage-*.tar.gz' -mtime +$((RETENTION_JOURS - 1)) -delete
find "$RACINE" -maxdepth 2 -type f -name '*.tmp' -mmin +120 -delete

echo "Sauvegarde ImmoPlus terminée : $(basename "$dump") ($(du -h "$dump" | cut -f1)), $(basename "$archive") ($(du -h "$archive" | cut -f1))"
