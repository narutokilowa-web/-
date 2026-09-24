# ADR-0002: One municipality per deployment (pilot)

- **Status:** Accepted
- **Date:** 2026-09-24

## Context
The long-term spec describes multi-tenancy (`tenant_id` + row-level security). The pilot serves one municipality, and production in Syria is likely to be one deployment per municipality or governorate for data-sovereignty reasons.

## Decision
- No `tenant_id` in pilot tables.
- A single `organization` configuration record (name, time zone, default currency, map center) plus a `department` table with one row.

## Consequences
- Simpler schema, queries and tests.
- If one deployment ever hosts several municipalities, a migration adds `tenant_id` + RLS. That work is accepted as a known future cost.

## Alternatives considered
Multi-tenant from day one: rejected as unnecessary complexity for the pilot.
