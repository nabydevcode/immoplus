#!/usr/bin/env bash
# Installation initiale d'ImmoPlus (version légère) sur le VPS Ubuntu, en root, depuis le dépôt cloné :
#   sudo bash /var/www/immoplus-git/infra/production/installer-serveur.sh
# Ré-exécutable sans danger : ce qui existe déjà est gardé (en particulier /etc/immoplus/immoplus.env et ses secrets,
# la base et les fichiers). Ne touche à aucun autre projet du serveur (financement-api, sa base, son site nginx).
#
# Ensuite : sudo immoplus-deployer (publication de l'API) et, depuis le Mac, infra/production/deployer-front.sh.
set -euo pipefail

ICI="$(cd "$(dirname "$0")" && pwd)"
[ "$(id -u)" = 0 ] || { echo "À lancer en root." >&2; exit 1; }
etape() { echo "== $*"; }

etape "Utilisateur système et dossiers"
id immoplus >/dev/null 2>&1 || useradd --system --home-dir /var/lib/immoplus --shell /usr/sbin/nologin immoplus
install -d -m 750 -o immoplus -g immoplus /var/lib/immoplus
install -d -m 700 -o immoplus -g immoplus /var/lib/immoplus/storage
install -d -m 755 -o root -g root /opt/immoplus /opt/immoplus/releases /var/www/immoplus-react
install -d -m 750 -o root -g immoplus /var/backups/immoplus /var/backups/immoplus/postgres
install -d -m 700 -o root -g root /var/backups/immoplus/fichiers /etc/immoplus

etape "Configuration de production (/etc/immoplus/immoplus.env)"
nouveau_mdp_base=""
if [ ! -f /etc/immoplus/immoplus.env ]; then
  # Secrets générés ici, jamais affichés : base (hexadécimal), clé JWT (128 caractères), jeton des métriques.
  nouveau_mdp_base="$(openssl rand -hex 32)"
  cle_jwt="$(openssl rand -base64 192 | tr -d '\n/+=' | cut -c1-128)"
  jeton_metriques="$(openssl rand -hex 32)"
  umask 077
  sed -e "s|^ConnectionStrings__Default=.*|ConnectionStrings__Default=Host=127.0.0.1;Port=5432;Database=immoplus;Username=immoplus;Password=$nouveau_mdp_base|" \
      -e "s|^Jwt__Cle=.*|Jwt__Cle=$cle_jwt|" \
      -e "s|^Metriques__Jeton=.*|Metriques__Jeton=$jeton_metriques|" \
      "$ICI/immoplus.env.exemple" > /etc/immoplus/immoplus.env
  umask 022
  unset cle_jwt jeton_metriques
fi
chown root:root /etc/immoplus/immoplus.env
chmod 600 /etc/immoplus/immoplus.env

etape "PostgreSQL : rôle et base « immoplus », extensions"
if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='immoplus'" | grep -q 1; then
  [ -n "$nouveau_mdp_base" ] || { echo "Le rôle immoplus n'existe pas mais immoplus.env existe déjà : créer le rôle à la main avec le mot de passe de ce fichier." >&2; exit 1; }
  # Mot de passe transmis par l'entrée standard (jamais en argument de commande).
  sudo -u postgres psql -v ON_ERROR_STOP=1 -q <<SQL
CREATE ROLE immoplus LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '$nouveau_mdp_base';
SQL
fi
unset nouveau_mdp_base
if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='immoplus'" | grep -q 1; then
  sudo -u postgres psql -v ON_ERROR_STOP=1 -q -c "CREATE DATABASE immoplus OWNER immoplus ENCODING 'UTF8' TEMPLATE template0;"
fi
# Extensions exigées par les migrations (InitialePostgres : unaccent ; IndexEtPerformances : pg_trgm), créées par le
# superutilisateur avant le premier démarrage de l'API.
sudo -u postgres psql -v ON_ERROR_STOP=1 -q -d immoplus -c "CREATE EXTENSION IF NOT EXISTS unaccent; CREATE EXTENSION IF NOT EXISTS pg_trgm;"
sudo -u postgres psql -v ON_ERROR_STOP=1 -q -d immoplus -c "REVOKE ALL ON DATABASE immoplus FROM PUBLIC;"

etape "PostgreSQL : réglages mémoire (rechargés sans coupure ; shared_buffers au prochain redémarrage)"
conf_d="$(dirname "$(sudo -u postgres psql -tAc 'SHOW config_file')")/conf.d"
install -d -m 755 -o postgres -g postgres "$conf_d"
install -m 644 -o postgres -g postgres "$ICI/postgresql/90-immoplus.conf" "$conf_d/90-immoplus.conf"
sudo -u postgres psql -q -tAc "SELECT pg_reload_conf();" >/dev/null

etape "Mémoire d'échange (2 Go) si absente : évite l'arrêt brutal d'un processus en cas de pic"
if [ -z "$(swapon --show --noheadings)" ]; then
  fallocate -l 2G /swapfile
  chmod 600 /swapfile
  mkswap /swapfile >/dev/null
  swapon /swapfile
  grep -q '^/swapfile ' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
  echo 'vm.swappiness=10' > /etc/sysctl.d/90-immoplus-swap.conf
  sysctl -q -p /etc/sysctl.d/90-immoplus-swap.conf
fi

etape "Scripts et services systemd"
install -m 750 "$ICI/sauvegarde.sh" /usr/local/sbin/immoplus-sauvegarde
install -m 750 "$ICI/restaurer.sh" /usr/local/sbin/immoplus-restaurer
install -m 750 "$ICI/deployer.sh" /usr/local/sbin/immoplus-deployer
install -m 644 "$ICI/systemd/immoplus-api.service" /etc/systemd/system/immoplus-api.service
install -m 644 "$ICI/systemd/immoplus-sauvegarde.service" /etc/systemd/system/immoplus-sauvegarde.service
install -m 644 "$ICI/systemd/immoplus-sauvegarde.timer" /etc/systemd/system/immoplus-sauvegarde.timer
systemctl daemon-reload
systemctl enable --now immoplus-sauvegarde.timer
systemctl enable immoplus-api

etape "nginx : site immoplus (domaine sur 80, IP sur 8081)"
# Dossier parent du cache des photos (absent sur une installation nginx Ubuntu neuve).
install -d -m 755 -o root -g root /var/cache/nginx
install -m 644 "$ICI/nginx/immoplus-commun.conf" /etc/nginx/snippets/immoplus-commun.conf
install -m 644 "$ICI/nginx/immoplus-securite.conf" /etc/nginx/snippets/immoplus-securite.conf
if [ ! -f /etc/nginx/sites-available/immoplus ]; then
  # Pas d'écrasement : certbot modifie ce fichier pour y ajouter HTTPS.
  install -m 644 "$ICI/nginx/immoplus.conf" /etc/nginx/sites-available/immoplus
fi
ln -sfn /etc/nginx/sites-available/immoplus /etc/nginx/sites-enabled/immoplus
# Ancien client Blazor (port 8081) remplacé par le client React.
rm -f /etc/nginx/sites-enabled/immoplus-web
nginx -t
systemctl reload nginx

etape "certbot (HTTPS Let's Encrypt) : paquet installé, certificat demandé quand le DNS pointe ici"
command -v certbot >/dev/null || DEBIAN_FRONTEND=noninteractive apt-get install -y -q certbot python3-certbot-nginx >/dev/null

echo "Installation terminée. Suite : sudo immoplus-deployer"
