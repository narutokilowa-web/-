# ADR-0005: `IObjectStore` abstraction; local filesystem for development and DEMO 1

- **Status:** Accepted
- **Date:** 2026-09-24

## Decision
- All binary storage goes through `IObjectStore` (S3-compatible semantics: put / get / exists / delete-if-allowed; retention metadata).
- **DEMO 1 and development** use `LocalFileSystemObjectStore` on a Docker volume (`STORAGE_MODE=local`). The UI shows a "LOCAL STORAGE — NOT WORM" banner in this mode.
- The **production-compatible S3 product with Object Lock** is chosen in M8, after checking current licensing and image distribution. Candidates: MinIO, SeaweedFS, Ceph RGW, or a provider inside Syria.
- **Field-pilot configuration refuses to start with local storage.**

## Consequences
The storage swap is configuration plus one adapter. Write-once guarantees are **not** claimed until M8.
