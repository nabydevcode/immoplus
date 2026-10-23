#!/bin/sh
# Sauvegarde des fichiers d'ImmoPlus (bucket S3 « immoplus » de Garage) vers un dossier local, avec rclone.
# Exécutée par le service docker compose « sauvegarde-fichiers » (image rclone/rclone).
#
#   sans argument : boucle — vérifie toutes les heures, sauvegarde si la dernière a plus de 24 h
#   une-fois      : une sauvegarde immédiate
#                   (docker compose exec sauvegarde-fichiers /infra/sauvegarde-fichiers.sh une-fois)
#   controle      : compare le miroir au bucket (tailles + empreintes), code de sortie 0 si identique
#
# Organisation dans $DOSSIER (api/ImmoPlus.Api/Sauvegardes/fichiers/ côté Mac, non versionné) :
#   courant/                  miroir exact du bucket à la dernière sauvegarde (mêmes clés : photos/…, documents/…)
#   versions/AAAAMMJJ-HHMM/   anciennes versions des fichiers modifiés ou supprimés depuis la sauvegarde précédente
#                             (rclone --backup-dir) ; supprimées après $CONSERVER_JOURS jours
#   derniere-sauvegarde       horodatage de la dernière sauvegarde réussie
# Un fichier supprimé du bucket reste donc récupérable pendant $CONSERVER_JOURS jours dans versions/.
#
# Accès en LECTURE SEULE au bucket (clé « immoplus-sauvegarde », infra/garage.env) : ce service ne peut rien
# modifier dans le stockage. La restauration se fait avec la clé de l'API (voir README, « Restaurer les fichiers »).
set -eu

DOSSIER="${DOSSIER_SAUVEGARDES:-/sauvegardes}"
CONSERVER_JOURS="${CONSERVER_JOURS:-14}"
INTERVALLE_SECONDES="${INTERVALLE_SECONDES:-86400}"
BUCKET="${S3_BUCKET:-immoplus}"

# Remote rclone « stockage » défini uniquement par variables d'environnement (aucun fichier de configuration, aucun secret affiché).
export RCLONE_CONFIG_STOCKAGE_TYPE=s3
export RCLONE_CONFIG_STOCKAGE_PROVIDER=Other
export RCLONE_CONFIG_STOCKAGE_ENDPOINT="${S3_URL:-http://garage:9000}"
export RCLONE_CONFIG_STOCKAGE_REGION="${S3_REGION:-us-east-1}"
export RCLONE_CONFIG_STOCKAGE_FORCE_PATH_STYLE=true
export RCLONE_CONFIG_STOCKAGE_ACCESS_KEY_ID="${SAUVEGARDE_S3_CLE_ACCES:?SAUVEGARDE_S3_CLE_ACCES manquant (infra/garage.env)}"
export RCLONE_CONFIG_STOCKAGE_SECRET_ACCESS_KEY="${SAUVEGARDE_S3_CLE_SECRETE:?SAUVEGARDE_S3_CLE_SECRETE manquant (infra/garage.env)}"
export RCLONE_CONFIG_STOCKAGE_NO_CHECK_BUCKET=true
# Pas de fichier rclone.conf : un fichier vide évite l'avertissement « Config file not found ».
export RCLONE_CONFIG=/tmp/rclone-vide.conf
: > "$RCLONE_CONFIG"

umask 077   # données personnelles (pièces d'identité, documents) : lisibles par le seul propriétaire
mkdir -p "$DOSSIER/courant" "$DOSSIER/versions"

journal() { echo "$(date -u '+%Y-%m-%dT%H:%M:%SZ') [sauvegarde-fichiers] $*"; }

sauvegarder() {
  horodatage="$(date -u '+%Y%m%d-%H%M')"
  # sync : le miroir devient identique au bucket ; tout fichier remplacé ou supprimé part dans versions/<horodatage>.
  if rclone sync "stockage:$BUCKET" "$DOSSIER/courant" \
       --backup-dir "$DOSSIER/versions/$horodatage" \
       --checksum --fast-list --transfers 4 --stats-log-level NOTICE --stats 0 --log-level NOTICE; then
    date -u '+%Y-%m-%dT%H:%M:%SZ' > "$DOSSIER/derniere-sauvegarde"
    journal "OK $horodatage : $(find "$DOSSIER/courant" -type f | wc -l | tr -d ' ') fichiers, $(du -sh "$DOSSIER/courant" | cut -f1)"
  else
    journal "ÉCHEC rclone sync" >&2
    return 1
  fi
  # Rétention des anciennes versions.
  find "$DOSSIER/versions" -mindepth 1 -maxdepth 1 -type d -mmin +$((CONSERVER_JOURS * 1440)) -print \
    | while read -r d; do rm -rf "$d"; journal "versions supprimées (rétention) $(basename "$d")"; done
}

controler() {
  rclone check "stockage:$BUCKET" "$DOSSIER/courant" --checksum --log-level NOTICE \
    && journal "contrôle OK : le miroir est identique au bucket"
}

attendre_stockage() {
  until rclone lsf "stockage:$BUCKET" --max-depth 1 >/dev/null 2>&1; do journal "en attente du stockage S3…"; sleep 5; done
}

case "${1:-}" in
  une-fois) attendre_stockage; sauvegarder; exit $? ;;
  controle) attendre_stockage; controler; exit $? ;;
esac

attendre_stockage
journal "démarré : une sauvegarde toutes les $((INTERVALLE_SECONDES / 3600)) h, versions conservées $CONSERVER_JOURS jours, dossier $DOSSIER"
while true; do
  if [ -z "$(find "$DOSSIER" -maxdepth 1 -name derniere-sauvegarde -mmin -$((INTERVALLE_SECONDES / 60)))" ]; then
    sauvegarder || true
  fi
  sleep 3600
done
