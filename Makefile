# Developer entry points. See AGENTS.md and docs/runbooks/dev-setup.md.
SHELL := /bin/bash
COMPOSE := docker compose -f deploy/compose/docker-compose.yml $(if $(wildcard deploy/compose/docker-compose.override.yml),-f deploy/compose/docker-compose.override.yml) --env-file .env
FLUTTER_IMAGE ?= instrumentisto/flutter:3.41.6-androidsdk36-r0
# Extra `docker run` args for the Flutter container (e.g. proxy settings on restricted networks).
FLUTTER_DOCKER_ARGS ?=
# Use a local Flutter SDK if present, otherwise the pinned Docker image (includes the Android SDK).
FLUTTER := $(shell command -v flutter >/dev/null 2>&1 && echo flutter || echo docker run --rm $(FLUTTER_DOCKER_ARGS) -v $(CURDIR)/mobile:/app -v mfop-flutter-pub-cache:/root/.pub-cache -v mfop-gradle-cache:/root/.gradle -w /app $(FLUTTER_IMAGE) flutter)

.PHONY: help env up down ps logs migrate seed verify-audit tamper-demo tamper-undo demo-reset test test-backend test-web test-mobile e2e android-e2e apk clean

help:
	@grep -E '^[a-z-]+:.*?## ' $(MAKEFILE_LIST) | awk -F':.*?## ' '{printf "  %-14s %s\n", $$1, $$2}'

env: ## create .env with random dev secrets (if missing)
	@./tools/dev/init-env.sh

up: env ## build and start the stack; waits until healthy
	$(COMPOSE) up -d --build --wait
	@echo "Web: http://localhost:$${WEB_PORT:-5173}  API: http://localhost:$${API_PORT:-5080}  Keycloak: http://localhost:8080"

down: ## stop the stack (keeps data)
	$(COMPOSE) down

ps: ## show services
	$(COMPOSE) ps

logs: ## follow logs
	$(COMPOSE) logs -f --tail=100

migrate: env ## apply DB migrations (owner role, one-shot)
	$(COMPOSE) run --rm migrate migrate

seed: env ## load DEMO 1 data: dev users, reference data, synthetic lights/stock and the light with 2 earlier repairs (idempotent)
	$(COMPOSE) run --rm migrate migrate --reference-data /seed/reference-data.json --seed-dev /seed/dev-users.json
	$(COMPOSE) run --rm migrate seed-demo /seed/demo-data.json

verify-audit: env ## verify the audit chain, approved snapshots and active assets (read-only DB role)
	set -a && source .env && set +a && dotnet run --project tools/audit-verify -- --db "Host=localhost;Port=$${DB_PORT:-5432};Database=mfop;Username=mfop_readonly;Password=$${MFOP_READONLY_PASSWORD}"

# DEMO 1 step 20 (DEV ONLY): a privileged database user changes a consumed quantity of the most recently closed work
# order. The append-only trigger must be disabled first, which only the table owner can do; the verifier then names
# the broken seal and stock balances. `make tamper-undo` puts the recorded value back.
TAMPER_SQL = ALTER TABLE inventory.stock_movements DISABLE TRIGGER stock_movements_no_update_delete; \
	UPDATE inventory.stock_movements SET quantity = $(1) WHERE type = 'CONSUME' AND work_order_id = \
	(SELECT id FROM workorders.work_orders WHERE state = 'CLOSED' ORDER BY closed_at DESC LIMIT 1); \
	ALTER TABLE inventory.stock_movements ENABLE TRIGGER stock_movements_no_update_delete; \
	SELECT w.code, m.quantity FROM inventory.stock_movements m JOIN workorders.work_orders w ON w.id = m.work_order_id \
	WHERE w.id = (SELECT id FROM workorders.work_orders WHERE state = 'CLOSED' ORDER BY closed_at DESC LIMIT 1);

tamper-demo: env ## DEV ONLY, DEMO 1 step 20: as mfop_owner, set the latest closed work order's consumed quantity to 3
	set -a && source .env && set +a && $(COMPOSE) exec -T -e PGPASSWORD="$${MFOP_OWNER_PASSWORD}" db psql -h localhost -U mfop_owner -d mfop -c "$(call TAMPER_SQL,3)"

tamper-undo: env ## DEV ONLY: put the recorded quantity (1) back after the tamper demonstration
	set -a && source .env && set +a && $(COMPOSE) exec -T -e PGPASSWORD="$${MFOP_OWNER_PASSWORD}" db psql -h localhost -U mfop_owner -d mfop -c "$(call TAMPER_SQL,1)"

demo-reset: env ## wipe dev volumes and start again from scratch
	$(COMPOSE) down -v
	$(MAKE) up

test: test-backend test-web test-mobile ## all automated tests (no running stack needed)

test-backend: ## .NET build + tests (needs Docker for Testcontainers)
	cd backend && dotnet build -warnaserror && dotnet test --no-build

test-web: ## web lint + typecheck + unit tests
	cd web && npm ci --no-audit --no-fund && npm run lint && npm run typecheck && npm test

test-mobile: ## flutter analyze + unit/widget tests
	$(FLUTTER) pub get && $(FLUTTER) analyze && $(FLUTTER) test

e2e: ## browser E2E against the running stack (make up first)
	cd web && set -a && source ../.env && set +a && npx playwright test

android-e2e: ## field-app flow on a connected Android phone/emulator (make up first; needs a local Flutter SDK and adb)
	adb reverse tcp:5080 tcp:5080 && adb reverse tcp:8080 tcp:8080
	set -a && source .env && set +a && cd mobile && flutter drive --driver=test_driver/integration_test.dart \
	  --target=integration_test/field_flow_test.dart --dart-define=MFOP_TEST_PASSWORD="$$MFOP_DEV_USER_PASSWORD"
	@echo "Screenshots: mobile/build/android-e2e/"

apk: ## build the Android debug APK
	$(FLUTTER) pub get && $(FLUTTER) build apk --debug
	@ls -lh mobile/build/app/outputs/flutter-apk/app-debug.apk

clean: ## remove build outputs
	rm -rf web/dist mobile/build
	find backend tools -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
