# ADR-0004: Evidence uploads are streamed through our API

- **Status:** Accepted
- **Date:** 2026-09-24

## Decision
- The phone first announces a photo (with its on-device SHA-256) through a command.
- It then uploads the bytes to `PUT /sync/v1/evidence/{id}/content`.
- The API computes SHA-256 while streaming to the object store and compares it with the announced hash.

## Consequences
- No presigned URLs, no CORS or host-rewriting problems on a LAN/`adb reverse` setup.
- The server hash is computed on exactly the bytes received.
- Photo bandwidth passes through the API. This is acceptable at pilot volume; presigned uploads can be added later behind the same client contract.
