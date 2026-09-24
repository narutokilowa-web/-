#!/usr/bin/env bash
# Creates .env with freshly generated random secrets (dev only). Never overwrites an existing .env.
set -euo pipefail
cd "$(dirname "$0")/../.."
if [[ -f .env ]]; then
  echo ".env already exists — leaving it unchanged."
  exit 0
fi
gen() { LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c "${1:-24}"; }
sed -e "s/^POSTGRES_SUPERUSER_PASSWORD=.*/POSTGRES_SUPERUSER_PASSWORD=$(gen)/" \
    -e "s/^MFOP_OWNER_PASSWORD=.*/MFOP_OWNER_PASSWORD=$(gen)/" \
    -e "s/^MFOP_APP_PASSWORD=.*/MFOP_APP_PASSWORD=$(gen)/" \
    -e "s/^MFOP_READONLY_PASSWORD=.*/MFOP_READONLY_PASSWORD=$(gen)/" \
    -e "s/^KEYCLOAK_DB_PASSWORD=.*/KEYCLOAK_DB_PASSWORD=$(gen)/" \
    -e "s/^KEYCLOAK_ADMIN_PASSWORD=.*/KEYCLOAK_ADMIN_PASSWORD=$(gen)/" \
    -e "s/^MFOP_DEV_USER_PASSWORD=.*/MFOP_DEV_USER_PASSWORD=Dev-$(gen 16)/" \
    .env.example > .env
chmod 600 .env
echo "Created .env with random dev secrets."
echo "Dev users (registrar1, approver1, supervisor1, tech1, warehouse1, auditor1, admin1, inspector1)"
echo "sign in with MFOP_DEV_USER_PASSWORD from .env."
