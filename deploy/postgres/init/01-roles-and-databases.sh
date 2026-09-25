#!/bin/bash
# Runs once, on first start of an empty data volume (postgres docker-entrypoint-initdb.d).
# Creates: owner role (migrations), app role (runtime, no DELETE), readonly role (auditor tooling), Keycloak DB.
set -euo pipefail
: "${MFOP_OWNER_PASSWORD:?}" "${MFOP_APP_PASSWORD:?}" "${MFOP_READONLY_PASSWORD:?}" "${KEYCLOAK_DB_PASSWORD:?}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v owner_pw="$MFOP_OWNER_PASSWORD" -v app_pw="$MFOP_APP_PASSWORD" \
  -v ro_pw="$MFOP_READONLY_PASSWORD" -v kc_pw="$KEYCLOAK_DB_PASSWORD" <<'SQL'
CREATE ROLE mfop_owner    LOGIN PASSWORD :'owner_pw';
CREATE ROLE mfop_app      LOGIN PASSWORD :'app_pw';
CREATE ROLE mfop_readonly LOGIN PASSWORD :'ro_pw';
CREATE ROLE keycloak      LOGIN PASSWORD :'kc_pw';

CREATE DATABASE mfop     OWNER mfop_owner ENCODING 'UTF8' TEMPLATE template0;
CREATE DATABASE keycloak OWNER keycloak   ENCODING 'UTF8' TEMPLATE template0;

REVOKE ALL ON DATABASE mfop FROM PUBLIC;
GRANT CONNECT ON DATABASE mfop TO mfop_app, mfop_readonly;
REVOKE ALL ON DATABASE keycloak FROM PUBLIC;
SQL

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname mfop <<'SQL'
CREATE EXTENSION IF NOT EXISTS postgis;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
SQL
echo "mfop roles and databases created"
