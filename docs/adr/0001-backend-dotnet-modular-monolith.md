# ADR-0001: Backend is a .NET modular monolith

- **Status:** Accepted
- **Date:** 2026-09-24

## Context
A long-lived public-sector system. Integrity depends on one transaction covering a state change **and** its audit event. The pilot team is 1–2 people with AI-assisted development, and the system must later be handed over to a Syrian/local team. C# is widely taught and used in Syria.

## Decision
- ASP.NET Core on **.NET 10 (LTS)**, as one deployable **modular monolith**.
- Each module is its own project with `Domain / Application / Infrastructure / Api / Contracts` folders, owns its own PostgreSQL schema, and talks to other modules only through `Contracts`.
- Module boundaries are enforced by architecture tests.
- PostgreSQL 17 + PostGIS.

## Consequences
- Single-transaction integrity (entity change + audit event) is straightforward.
- One process to deploy, monitor and back up.
- Boundaries must be policed by tests, otherwise the monolith erodes.
- Extraction candidates later: media processing, analytics.

## Alternatives considered
- **NestJS:** shares TypeScript with the web app, but has weaker transactional/PostGIS tooling and a larger supply-chain surface.
- **FastAPI:** strong for analytics, weaker for a large typed domain.
- **Microservices:** they would turn one transaction into a distributed saga. Rejected for the pilot.
