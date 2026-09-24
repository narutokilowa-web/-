# 06 — Audit Design and Evidence Design

## 6.1 Audit design (item 6)

### Guarantees

1. **No change without an audit event.** The event is inserted **in the same DB transaction** as the change, via the `UnitOfWork`. A handler cannot commit without it: `UnitOfWork.Commit()` fails if entities changed but no audit event was staged. This is enforced by an integration test on every command handler.
2. **Append-only.** Enforced by the database, not only by convention.
3. **Tamper-evident.** Hash chain plus daily signed checkpoints in write-once storage.
4. **Verifiable without trusting the application**, using the standalone `tools/audit-verify` CLI.

### Event record

```
audit_events
  seq            bigint      -- strictly increasing, gap-free; assigned under a transaction-scoped advisory lock
  event_id       uuid
  occurred_at    timestamptz -- server time
  actor_user_id, actor_roles[], device_id
  action         text        -- e.g. "workorder.approved", "stock.issue.acknowledged", "device.revoked"
  entity_type, entity_id, entity_version
  changes        jsonb       -- [{field, old, new}] for operational fields; references (ids), not personal payloads
  reason         text null
  command_id, correlation_id
  payload_hash   = SHA-256( JCS(event without seq/prev_hash/hash) )     -- RFC 8785 canonical JSON
  prev_hash      = hash of seq-1 (genesis: 32 zero bytes)
  hash           = SHA-256( prev_hash || payload_hash || seq as 8-byte big-endian )
```

**Why an advisory lock?** Gap-free, ordered `seq` and a correct `prev_hash` require serializing audit inserts. At pilot volume (a few events per second at most) this costs nothing and removes a whole class of ordering bugs.

### Database enforcement (`deploy/postgres/init`)

```sql
-- roles: mfop_owner (migrations only), mfop_app (runtime), mfop_readonly (auditor tooling)
REVOKE ALL ON audit.audit_events, audit.audit_checkpoints FROM mfop_app;
GRANT SELECT, INSERT ON audit.audit_events, audit.audit_checkpoints TO mfop_app;
GRANT SELECT ON ALL TABLES IN SCHEMA audit TO mfop_readonly;

CREATE FUNCTION audit.reject_mutation() RETURNS trigger AS $$
BEGIN RAISE EXCEPTION 'audit tables are append-only'; END $$ LANGUAGE plpgsql;
CREATE TRIGGER no_update_delete BEFORE UPDATE OR DELETE ON audit.audit_events
  FOR EACH ROW EXECUTE FUNCTION audit.reject_mutation();
CREATE TRIGGER no_truncate BEFORE TRUNCATE ON audit.audit_events
  FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_mutation();
-- same for other append-only tables: asset_versions, wo_events, wo_submissions, wo_reviews,
-- stock_movements, fault_confirmations, item_costs, offline_grants (revocation via separate table)
```

The runtime connection string uses `mfop_app`. Migrations run as `mfop_owner` in a separate one-shot step, never at API startup in pilot configuration.

### Checkpoints (external anchoring, lean version)

- The worker runs daily and on demand. It writes `{to_seq, last_hash, event_count, created_at}`, signed with the **anchor key** (Ed25519), to:
  1. `audit_checkpoints` (DB);
  2. the `audit-anchors` bucket (Object Lock);
  3. a JSON file that the auditor downloads (web: *Audit → Checkpoints → Download*).
- **Pilot key custody:** the anchor private key is in secret configuration separate from the DB credentials, and the **auditor keeps the public key**. In production the private key moves to a custody the operations team does not control (decision deferred; noted in ADR).

### Verification

`tools/audit-verify` (a standalone .NET console app that does not reference the API project):

```
audit-verify --db <readonly conn> [--from-checkpoint <file.json>] [--anchor-pubkey <file>]
  → recomputes payload_hash and hash for every event in order
  → checks seq continuity, prev_hash links, checkpoint signatures, and checkpoint last_hash matches
  → recomputes seal_hash for every CLOSED work order from operational tables and compares with the audited seal
  → recomputes stock_balances from stock_movements
  → exit 0 = OK; non-zero + report = integrity incident
```

`POST /audit/verify` (auditor) runs the same library server-side for convenience. **The CLI is the authoritative tool.**

### Work-order seal

On `ApproveWorkOrder`:

```
seal = SHA-256( JCS({
  work_order: {id, code, asset_id, fault_id, outcome, diagnosis_code, test_method, …},
  events: [wo_events ordered by seq → (type, actor, estimated_time, data)],
  evidence: [(id, kind, sha256_server) ordered by id],
  materials: [stock_movements where work_order_id → (item, qty, serials)],
  checklist: {template_id, answers},
  submissions: [(submission_no, payload_hash)],
  reviews: [(reviewer, decision, reasons)]
}) )
```

The seal is stored on the WO and written into the audit event `workorder.closed`. Any later change to any of these tables for that WO is caught by the verifier's recomputation.

### What is audited (minimum list)

Every command in 04 (all transitions and non-transition commands); user/role/device/policy changes; stock movements; flag decisions; quarantine resolutions; evidence state changes (`VERIFIED`, `HASH_MISMATCH`, `DUPLICATE`); **reads of evidence originals** (auditor access to personal-location data) and exports.

## 6.2 Evidence design (item 7)

### Required evidence per step (street light)

| Step | Kind | Count | Extra requirement |
|---|---|---|---|
| Registration | `ASSET_OVERVIEW` | ≥ 1 | whole pole and luminaire in frame |
| Registration | `TAG` | ≥ 1 | tag readable after mounting |
| Registration (solar) | `NAMEPLATE` | ≥ 1 | panel/battery label if reachable |
| Field check | `ASSET_OVERVIEW` | 1 | inspector does **not** see the registrar's photos before capturing |
| Fault report | `OTHER` | 0–1 | optional |
| Arrival (first) | `BEFORE` | 2 | ≥ 1 shows the tag (app overlay guides) |
| Arrival (subsequent) | `BEFORE` | 1 | |
| Resolve no work | `OTHER` | 1 | proves the reason |
| Submit (repaired) | `AFTER` | ≥ 2 | pole door / cover closed in one |
| Submit (repaired) | `TEST` | 1 | light **on** (night, photocell covered, or bypass), unless `NOT_TESTED` + reason |
| Core return declaration | `CORE_PART` | 1 | serial visible if the item is serial-tracked |

Requirements are data (`evidence_requirements` in the checklist template), not code, so later asset types reuse the mechanism.

### Capture → verification pipeline

```
PHONE
  in-app camera only (no gallery picker exists in the app)
  → JPEG re-encode on device (max 2048 px, quality ~80)          ← final bytes
  → sha256_device over final bytes
  → encrypt + store in app sandbox
  → outbox: AddEvidence{evidence_id, owner, kind, sha256_device, size, capture_time, location, accuracy, is_mock}
  → upload queue: PUT /sync/v1/evidence/{id}/content (after AddEvidence is ACK'd)

SERVER
  AddEvidence applied   → evidence.state = ANNOUNCED; audit event includes sha256_device
                          (the hash is committed before the bytes arrive)
  PUT content           → stream to evidence-incoming while computing sha256_server; size ≤ 15 MB;
                          magic-bytes check (JPEG only in pilot)
                          → state = UPLOADED
  worker                → sha256_server == sha256_device ?
                              no  → HASH_MISMATCH + CRITICAL flag (bytes changed after capture, or corruption)
                              yes → exact duplicate of any existing VERIFIED evidence (same sha256)?
                                      yes → DUPLICATE + CRITICAL flag (photo reuse); not usable as evidence
                                      no  → copy to `evidence` (Object Lock) → delete from incoming
                                            → derive preview + thumbnail (re-encoded, EXIF stripped) → VERIFIED
  every state change    → audit event
```

**Viewing:** web and mobile see **derived** images only, via short-lived authorized URLs served by the API. The original (with EXIF/GPS) is available to `AUDITOR` only, and each access is audited.

**Supersede, never delete:** if a technician retakes a photo **before** it is committed to the outbox, the discarded capture never leaves the phone. Once a photo is committed, it can only be superseded with a reason. The superseded photo stays stored and visible to reviewer and auditor.

### Presence score (lean version, per arrival)

| Signal | Points |
|---|---|
| Tag scanned by camera and it is this asset's tag | +40 |
| Location within `max(25 m, 2 × accuracy)` of the asset | +30 |
| Accuracy ≤ 20 m | +10 |
| Time confidence `HIGH` | +10 |
| Device `ACTIVE` and bound to user | +10 |
| `is_mock = true` | −50 |
| Manual code entry instead of scan | −20 |

The score is shown to the reviewer **with its breakdown**. It is never used as a verdict, only as review guidance and as a risk-tier input (`< 40` ⇒ `HIGH`). The weights are pilot defaults to be calibrated.

### Integrity flags produced in the pilot

| Code | Severity | Trigger |
|---|---|---|
| `EVIDENCE_HASH_MISMATCH` | CRITICAL | server hash ≠ device hash |
| `EVIDENCE_DUPLICATE` | CRITICAL | same SHA-256 as existing evidence |
| `MOCK_LOCATION` | WARNING | `is_mock` on arrival/evidence |
| `PRESENCE_WEAK` | WARNING | arrival score < 40 |
| `ASSET_NOT_FOUND` | CRITICAL | diagnosis |
| `REGISTRATION_PROXIMITY` | WARNING | another light within `proximity_duplicate_m` |
| `CONSUMPTION_EXCEEDS_CUSTODY` | WARNING | see 07 |
| `CORE_SERIAL_MISMATCH` | CRITICAL | see 04 §4.11 |
| `CORE_RETURN_OVERDUE`, `ISSUE_UNACKNOWLEDGED` | WARNING | timers |
| `GRANT_*`, `CLOCK_SKEW` | WARNING / INFO | see 05 |
| `REPEATED_FAILURE` | INFO (+ recommendation) | see below |

### Repeated-failure rule (the one analytics rule)

```
on WorkOrder CLOSED with outcome REPAIRED and diagnosis.counts_as_failure = true:
  n = count of WOs on the same asset, CLOSED, REPAIRED, counts_as_failure,
      closed within the last policy.repeated_failure_days (default 180), including this one
  if n >= policy.repeated_failure_count (default 3):
      create Recommendation(FULL_INSPECTION) if none OPEN for the asset
      create Flag(REPEATED_FAILURE, INFO, subject=asset,
                  explanation={n, window_days, threshold, work_orders:[codes, dates, diagnoses]})
```

`GRID_SUPPLY_OFF`, `THEFT`, `VANDALISM`, `NO_FAULT_FOUND`, and the `ASSET_*` codes **do not count** as failures.
