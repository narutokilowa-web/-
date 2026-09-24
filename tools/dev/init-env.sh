#!/usr/bin/env bash
# Creates .env with freshly generated random secrets (dev only).
# If .env already exists, it is never overwritten: only variables that are new in .env.example are appended.
set -euo pipefail
cd "$(dirname "$0")/../.."

gen() { LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c "${1:-24}"; }

# Value for a key: secrets are generated, everything else copies the .env.example default.
value_for() {
  case "$1" in
    POSTGRES_SUPERUSER_PASSWORD|MFOP_OWNER_PASSWORD|MFOP_APP_PASSWORD|MFOP_READONLY_PASSWORD|KEYCLOAK_DB_PASSWORD|KEYCLOAK_ADMIN_PASSWORD) gen ;;
    MFOP_TAG_CHECK_KEY) gen 32 ;;
    MFOP_DEV_USER_PASSWORD) echo "Dev-$(gen 16)" ;;
    *) grep -E "^$1=" .env.example | head -1 | cut -d= -f2- ;;
  esac
}

keys() { grep -E '^[A-Z0-9_]+=' "$1" | cut -d= -f1; }

if [[ ! -f .env ]]; then
  : > .env
  chmod 600 .env
  created=1
else
  created=0
fi

added=()
for key in $(keys .env.example); do
  if ! grep -qE "^${key}=" .env; then
    echo "${key}=$(value_for "$key")" >> .env
    added+=("$key")
  fi
done

if [[ $created -eq 1 ]]; then
  echo "Created .env with random dev secrets."
  echo "Dev users (registrar1, approver1, supervisor1, tech1, warehouse1, auditor1, admin1, inspector1)"
  echo "sign in with MFOP_DEV_USER_PASSWORD from .env."
elif [[ ${#added[@]} -gt 0 ]]; then
  echo "Added new variables to existing .env: ${added[*]}"
else
  echo ".env is up to date."
fi
