# ADR-0006: No Redis and no message broker; PostgreSQL for jobs and outbox

- **Status:** Accepted
- **Date:** 2026-09-24

## Decision
- Background work uses a transactional outbox table and job tables in PostgreSQL (`FOR UPDATE SKIP LOCKED`).
- In DEMO 1 it runs as a hosted service inside the API process. From M8 it runs in a separate `worker` container built from the same image.

## Consequences
- Fewer services to run, secure and back up.
- Revisit only when distributed rate limiting or a hot cache is actually needed.
