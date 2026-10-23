#!/bin/sh
# Supprime de MailHog (http://localhost:8025) les SEULS emails dont tous les destinataires sont en @test-charge.local
# (envoyés pendant les tests de charge). Les autres emails ne sont jamais touchés.
#   infra/charge/purger-mailhog.sh            # supprime
#   SIMULATION=1 infra/charge/purger-mailhog.sh   # compte seulement
set -eu
MAILHOG="${MAILHOG_URL:-http://localhost:8025}"
export MAILHOG SIMULATION="${SIMULATION:-0}"
python3 -I - <<'EOF'
import json, os, urllib.request

base = os.environ["MAILHOG"]
simulation = os.environ["SIMULATION"] == "1"
domaine = "@test-charge.local"

def destinataires(m):
    return [f"{d['Mailbox']}@{d['Domain']}".lower() for d in (m.get("To") or [])]

a_supprimer, gardes, debut = [], 0, 0
while True:
    page = json.load(urllib.request.urlopen(f"{base}/api/v2/messages?start={debut}&limit=250"))
    items = page.get("items") or []
    for m in items:
        dest = destinataires(m)
        if dest and all(d.endswith(domaine) for d in dest):
            a_supprimer.append(m["ID"])
        else:
            gardes += 1
    debut += len(items)
    if not items or debut >= page["total"]:
        break

if not simulation:
    for i in a_supprimer:
        urllib.request.urlopen(urllib.request.Request(f"{base}/api/v1/messages/{i}", method="DELETE"))
print(f"{len(a_supprimer)} email(s) {domaine} {'à supprimer (simulation)' if simulation else 'supprimé(s)'} ; {gardes} autre(s) email(s) conservé(s)")
EOF
