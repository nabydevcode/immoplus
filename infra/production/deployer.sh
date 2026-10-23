#!/usr/bin/env bash
# Mise à jour de l'API ImmoPlus sur le VPS (installé dans /usr/local/sbin/immoplus-deployer). À lancer en root :
#   sudo immoplus-deployer            # dernière version de la branche main (GitHub)
#   sudo immoplus-deployer --retour   # revenir à la version précédente (sans toucher à la base)
#
# Étapes : 1. sauvegarde (base + fichiers)  2. git pull (main)  3. dotnet publish -c Release dans une NOUVELLE version
# /opt/immoplus/releases/<date>-<commit>  4. bascule du lien /opt/immoplus/current et redémarrage (les migrations
# EF Core s'appliquent au démarrage)  5. contrôle /sante/pret  6. si échec : retour automatique à la version précédente.
# Les 5 dernières versions sont gardées.
#
# Limite : si la nouvelle version a appliqué une migration incompatible avec l'ancienne, le retour de code ne suffit
# pas : restaurer aussi la sauvegarde faite à l'étape 1 (immoplus-restaurer <fichier affiché>).
#
# Le front (client React) se déploie depuis le Mac : infra/production/deployer-front.sh.
set -euo pipefail

DEPOT=/var/www/immoplus-git
VERSIONS=/opt/immoplus/releases
COURANT=/opt/immoplus/current
STOCKAGE=/var/lib/immoplus/storage
URL_SANTE=http://127.0.0.1:5050/sante/pret
GARDER=5

journal() { echo "[$(date +%H:%M:%S)] $*"; }

attendre_pret() {
  for _ in $(seq 1 40); do
    if curl -fs -o /dev/null --max-time 5 "$URL_SANTE"; then return 0; fi
    sleep 3
  done
  return 1
}

basculer() {
  ln -sfn "$1" "$COURANT.nouveau"
  mv -T "$COURANT.nouveau" "$COURANT"
  systemctl restart immoplus-api
}

retour() {
  local actuelle precedente
  actuelle="$(readlink -f "$COURANT")"
  precedente="$(ls -1d "$VERSIONS"/*/ | sed 's:/$::' | sort | grep -v -x "$actuelle" | tail -n 1)"
  [ -n "$precedente" ] || { echo "Aucune version précédente." >&2; exit 1; }
  journal "Retour à $(basename "$precedente")…"
  basculer "$precedente"
  attendre_pret && journal "Version $(basename "$precedente") en service." || { echo "L'API ne répond pas : journalctl -u immoplus-api -n 100" >&2; exit 1; }
}

main() {
  [ "$(id -u)" = 0 ] || { echo "À lancer en root (sudo)." >&2; exit 1; }
  exec 9>/run/immoplus-deployer.lock
  flock -n 9 || { echo "Un déploiement est déjà en cours." >&2; exit 1; }

  if [ "${1:-}" = "--retour" ]; then retour; exit 0; fi

  journal "1/6 Sauvegarde…"
  /usr/local/sbin/immoplus-sauvegarde

  journal "2/6 Récupération du code (main)…"
  git -C "$DEPOT" fetch --quiet origin main
  git -C "$DEPOT" merge --ff-only --quiet origin/main
  local commit id cible precedente
  commit="$(git -C "$DEPOT" rev-parse --short HEAD)"
  id="$(date +%Y%m%d-%H%M%S)-$commit"
  cible="$VERSIONS/$id"
  journal "    commit $(git -C "$DEPOT" log -1 --format='%h %s')"

  journal "3/6 Publication Release dans $cible…"
  mkdir -p "$VERSIONS"
  # Sans serveurs de compilation résidents (VBCSCompiler/MSBuild : ~250 Mo gardés en mémoire après la publication sur
  # un serveur de 4 Go, et qui hériteraient du verrou) ; descripteur du verrou fermé pour dotnet.
  DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
    dotnet publish "$DEPOT/api/ImmoPlus.Api/ImmoPlus.Api.csproj" -c Release -o "$cible" /p:UseAppHost=false \
      --disable-build-servers --nologo -v quiet 9>&-
  # Fichiers déposés par les utilisateurs : hors du dossier de version (lien Storage -> /var/lib/immoplus/storage).
  if [ ! -L "$cible/Storage" ]; then rm -rf "$cible/Storage"; fi
  ln -sfn "$STOCKAGE" "$cible/Storage"
  # Aucun secret ni réglage de dev dans la version publiée (configuration : /etc/immoplus/immoplus.env).
  rm -f "$cible/appsettings.Development.json" "$cible"/*.db "$cible"/sms.env
  chown -R root:immoplus "$cible"
  chmod -R u=rwX,g=rX,o= "$cible"

  precedente="$(readlink -f "$COURANT" 2>/dev/null || true)"
  journal "4/6 Bascule et redémarrage (migrations appliquées au démarrage)…"
  basculer "$cible"

  journal "5/6 Contrôle $URL_SANTE…"
  if attendre_pret; then
    journal "6/6 Version $id en service."
  else
    journal "ÉCHEC : l'API n'est pas prête. Journal :"
    journalctl -u immoplus-api -n 40 --no-pager || true
    if [ -n "$precedente" ] && [ -d "$precedente" ]; then
      journal "Retour automatique à $(basename "$precedente")…"
      basculer "$precedente"
      attendre_pret && journal "Ancienne version en service. Si une migration a été appliquée, voir la limite en tête de script." \
        || journal "L'ancienne version ne répond pas non plus : restaurer la sauvegarde de l'étape 1 (immoplus-restaurer)."
    fi
    exit 1
  fi

  # Ménage : garder les $GARDER versions les plus récentes (jamais la version en service).
  local actuelle
  actuelle="$(readlink -f "$COURANT")"
  ls -1d "$VERSIONS"/*/ | sed 's:/$::' | sort | head -n -"$GARDER" | while read -r ancienne; do
    [ "$ancienne" = "$actuelle" ] || rm -rf "$ancienne"
  done
}

# Tout le script est lu avant d'être exécuté (main appelée à la fin) : un git pull qui modifie ce fichier
# pendant l'exécution ne le perturbe pas.
main "$@"
exit
