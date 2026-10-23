#!/bin/sh
# Lance UN palier du test de charge k6 contre le profil « echelle » branché sur la base JETABLE immoplus_charge.
#   infra/charge/lancer-charge.sh 100            # 100 utilisateurs virtuels (montée 1 min, palier 3 min)
#   MONTEE=30s DUREE=1m infra/charge/lancer-charge.sh 20     # essai court
# Prérequis (voir README, section « Tests de charge ») : base immoplus_charge remplie, bucket immoplus-charge, et
#   docker compose -f docker-compose.yml -f infra/charge/compose.charge.yml --profile echelle up -d --build api-echelle-1 api-echelle-2 nginx-echelle
# Résultats (non versionnés) dans infra/charge/resultats/ : résumé k6 (JSON), ressources des conteneurs (CSV),
# connexions PostgreSQL (CSV). Aucune donnée réelle n'est lue ni écrite : le script REFUSE de tourner si une instance
# du profil echelle n'est pas branchée sur immoplus_charge.
set -eu
cd "$(dirname "$0")/../.."

VUS="${1:?usage : infra/charge/lancer-charge.sh <utilisateurs virtuels>}"
MONTEE="${MONTEE:-1m}"
DUREE="${DUREE:-3m}"
K6_IMAGE="${K6_IMAGE:-grafana/k6:2.3.0}"
# Plafond « souple » de mémoire de k6 (ramasse-miettes Go plus actif au-delà), ex. K6_GOMEMLIMIT=1000MiB : à 500 utilisateurs
# virtuels k6 dépasse 1,3 Go et peut être tué (code 137) quand Docker n'a que 4 Go pour toute la pile.
K6_GOMEMLIMIT="${K6_GOMEMLIMIT:-}"
NB_LOCATAIRES="${NB_LOCATAIRES:-300}"
NB_HOTES="${NB_HOTES:-80}"
RESULTATS="infra/charge/resultats"
mkdir -p "$RESULTATS"

# --- Garde-fous : les 2 instances tournent, sur la base et le bucket de charge, avec le Redis de charge ---
for s in api-echelle-1 api-echelle-2; do
  env_proc=$(docker compose exec -T "$s" sh -c 'tr "\0" "\n" < /proc/1/environ' 2>/dev/null || true)
  echo "$env_proc" | grep -q 'Database=immoplus_charge' || { echo "Refus : $s n'est pas démarrée sur immoplus_charge (compose.charge.yml)" >&2; exit 1; }
  echo "$env_proc" | grep -q '^Stockage__S3__Bucket=immoplus-charge$' || { echo "Refus : $s n'utilise pas le bucket immoplus-charge" >&2; exit 1; }
  echo "$env_proc" | grep -q 'redis-charge:6379' || { echo "Refus : $s n'utilise pas redis-charge" >&2; exit 1; }
done
unset env_proc
reseau=$(docker inspect -f '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}' "$(docker compose ps -q nginx-echelle)")

# --- Comptes de test (lecture seule sur immoplus_charge) : locataires ayant une conversation encore ouverte, hôtes ayant
# des réservations, admin ; centre de chaque ville pour la carte. Uniquement des adresses @test-charge.local. ---
psql_charge() { docker compose exec -T postgres psql -U immoplus -d immoplus_charge -v ON_ERROR_STOP=1 -At -c "$1"; }
psql_charge "
with ouvertes as (
  select r.\"LocataireId\", r.\"Id\" from \"Reservations\" r
  where r.\"Statut\" in (0, 1, 4) or (r.\"Statut\" = 3 and r.\"DateDepart\" > now() - interval '20 days')
), loc as (
  select u.\"Email\" as email, array_agg(o.\"Id\" order by o.\"Id\") as reservations
  from ouvertes o join \"Utilisateurs\" u on u.\"Id\" = o.\"LocataireId\"
  where u.\"Email\" like '%@test-charge.local' and u.\"Statut\" = 1
  group by u.\"Email\" order by md5(u.\"Email\") limit $NB_LOCATAIRES
), hotes as (
  select distinct u.\"Email\" as email, md5(u.\"Email\") as ordre
  from \"Reservations\" r join \"Appartements\" a on a.\"Id\" = r.\"AppartementId\" join \"Utilisateurs\" u on u.\"Id\" = a.\"BailleurId\"
  where u.\"Email\" like '%@test-charge.local' and u.\"Statut\" = 1
  order by ordre limit $NB_HOTES
), centres as (
  select \"Ville\" as ville, avg(\"Latitude\") as lat, avg(\"Longitude\") as lng from \"Appartements\"
  where \"Latitude\" is not null group by \"Ville\"
)
select json_build_object(
  'locataires', (select json_agg(json_build_object('email', email, 'reservations', reservations)) from loc),
  'hotes', (select json_agg(email) from hotes),
  'admin', 'admin@test-charge.local',
  'centres', (select json_object_agg(ville, json_build_array(lat, lng)) from centres))" > "$RESULTATS/comptes.json"

echo "Palier $VUS VU (montée $MONTEE, palier $DUREE) sur le réseau $reseau — comptes : $(grep -o '@test-charge.local' "$RESULTATS/comptes.json" | wc -l | tr -d ' ')"

# --- Relevés pendant le test : CPU/mémoire des conteneurs et connexions PostgreSQL, toutes les 10 s ---
horodatage=$(date +%Y%m%d-%H%M%S)
stats="$RESULTATS/ressources-${VUS}vu-$horodatage.csv"
pg="$RESULTATS/postgres-${VUS}vu-$horodatage.csv"
echo "heure;conteneur;cpu;memoire" > "$stats"
echo "heure;base;etat;connexions" > "$pg"
(
  while :; do
    h=$(date +%H:%M:%S)
    docker stats --no-stream --format "{{.Name}};{{.CPUPerc}};{{.MemUsage}}" | sed "s/^/$h;/" >> "$stats" 2>/dev/null || true
    docker compose exec -T postgres psql -U immoplus -d postgres -At -F ';' -c \
      "select datname, coalesce(state, '?'), count(*) from pg_stat_activity where datname like 'immoplus%' group by 1, 2" 2>/dev/null \
      | sed "s/^/$h;/" >> "$pg" || true
    sleep 10
  done
) &
releves=$!
trap 'kill $releves 2>/dev/null || true' EXIT INT TERM

# --- k6 (image officielle) sur le réseau Docker du profil : nginx-echelle → instances ---
code=0
docker run --rm --name "immoplus-k6-$VUS" --network "$reseau" \
  --ulimit nofile=65536:65536 ${K6_GOMEMLIMIT:+-e GOMEMLIMIT=$K6_GOMEMLIMIT} \
  -v "$PWD/infra/charge/k6:/scripts:ro" -v "$PWD/$RESULTATS:/resultats" \
  -e VUS="$VUS" -e MONTEE="$MONTEE" -e DUREE="$DUREE" -e BASE_URL=http://nginx-echelle/api \
  -e SORTIE="/resultats/resume-${VUS}vu-$horodatage.json" -e K6_NO_USAGE_REPORT=true \
  "$K6_IMAGE" run --quiet /scripts/charge.js || code=$?

echo "Résumé : $RESULTATS/resume-${VUS}vu-$horodatage.json ; ressources : $stats ; PostgreSQL : $pg (code k6 : $code, 99 = seuil dépassé)"
exit "$code"
