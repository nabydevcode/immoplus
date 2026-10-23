#!/bin/sh
# Sauvegarde PostgreSQL d'ImmoPlus (pg_dump format personnalisé -Fc), exécutée par le service
# docker compose « sauvegarde-db ».
#
#   sans argument : boucle infinie — vérifie toutes les heures, sauvegarde si la dernière a plus de 24 h
#   une-fois      : une seule sauvegarde immédiate (docker compose exec sauvegarde-db /infra/sauvegarde-postgres.sh une-fois)
#
# Fichiers : $DOSSIER/immoplus-AAAAMMJJ-HHMM.dump (heure UTC). Écriture dans un .tmp puis renommage :
# un fichier .dump visible est toujours complet. Rétention : $CONSERVER_JOURS jours.
# Variables (fournies par compose) : PGHOST, PGUSER, PGDATABASE, POSTGRES_PASSWORD.
set -eu

DOSSIER="${DOSSIER_SAUVEGARDES:-/sauvegardes}"
CONSERVER_JOURS="${CONSERVER_JOURS:-14}"
INTERVALLE_SECONDES="${INTERVALLE_SECONDES:-86400}"
export PGPASSWORD="${POSTGRES_PASSWORD:?POSTGRES_PASSWORD manquant (infra/postgres.env)}"

umask 077   # données personnelles : fichiers lisibles par le seul propriétaire
mkdir -p "$DOSSIER"

journal() { echo "$(date -u '+%Y-%m-%dT%H:%M:%SZ') [sauvegarde-db] $*"; }

sauvegarder() {
  nom="immoplus-$(date -u '+%Y%m%d-%H%M').dump"
  tmp="$DOSSIER/.$nom.tmp"
  if pg_dump -Fc --no-owner --file="$tmp"; then
    mv "$tmp" "$DOSSIER/$nom"
    journal "OK $nom ($(du -h "$DOSSIER/$nom" | cut -f1))"
  else
    rm -f "$tmp"
    journal "ÉCHEC pg_dump" >&2
    return 1
  fi
  # Rétention : supprime les sauvegardes de plus de CONSERVER_JOURS jours.
  find "$DOSSIER" -maxdepth 1 -name 'immoplus-*.dump' -type f -mmin +$((CONSERVER_JOURS * 1440)) -print -delete \
    | while read -r f; do journal "supprimée (rétention) $(basename "$f")"; done
  # Restes d'une sauvegarde interrompue.
  find "$DOSSIER" -maxdepth 1 -name '.immoplus-*.tmp' -type f -mmin +60 -delete
}

attendre_base() {
  until pg_isready -q; do journal "en attente de PostgreSQL…"; sleep 5; done
}

if [ "${1:-}" = "une-fois" ]; then
  attendre_base
  sauvegarder
  exit $?
fi

attendre_base
journal "démarré : une sauvegarde toutes les $((INTERVALLE_SECONDES / 3600)) h, conservation $CONSERVER_JOURS jours, dossier $DOSSIER"
while true; do
  # Vérification toutes les heures : sauvegarde seulement si la dernière a plus de INTERVALLE_SECONDES
  # (pas de sauvegarde à chaque redémarrage du conteneur).
  recente=$(find "$DOSSIER" -maxdepth 1 -name 'immoplus-*.dump' -type f -mmin -$((INTERVALLE_SECONDES / 60)) | head -n 1)
  if [ -z "$recente" ]; then
    sauvegarder || true
  fi
  sleep 3600
done
