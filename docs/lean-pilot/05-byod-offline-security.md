# 05 — BYOD and Offline Security Design

## 5.1 Assumptions (explicit)

- Field staff use their **own Android phones**. There is **no MDM and no Android Enterprise managed profile** in the pilot.
- The municipality **cannot** uninstall the app, wipe app data, or inspect anything on a personal phone. The design **must not rely on any of that**.
- The app is distributed as a **signed APK** from the municipality's server, with no dependency on Google Play or Google Play Services. Many phones lack them, and their availability cannot be assumed.
- Networks are unreliable and paid by someone (TBD). The app is offline-first and syncs over **any approved internet connection**. There is no Wi-Fi assumption.
- A phone may be shared with family members, rooted, running a mock-location app, or lost.

**Consequence:** the security boundary is the **server**. The phone is treated as an untrusted client that collects evidence.

## 5.2 What the app may and may not do on a personal phone

| Allowed | Never |
|---|---|
| Camera (in-app capture only) | Access gallery, files, contacts, SMS, call logs, other apps |
| Location **only at the moment of an evidence capture or arrival, while the app is in the foreground** | Background or continuous location tracking |
| Internet | Device-admin APIs, remote wipe, remote uninstall |
| Store its own data in its **private app sandbox**, encrypted | Write evidence to shared storage or the gallery |
| Read the mock-location flag of its own location fixes | Read or modify anything outside the app sandbox |

Android permissions requested: `CAMERA`, `ACCESS_FINE_LOCATION` (while in use), `INTERNET`, `ACCESS_NETWORK_STATE`. Nothing else.

On first run the app shows a **privacy notice** in Arabic: what is collected, when, why, who can see it, and how long it is kept. The user must accept it before registering the device. The employee can see the list of evidence they have captured.

## 5.3 Enrollment

```
1. Admin creates the user (web) and assigns roles.
2. Employee installs the signed APK (download page on the municipality server).
3. App: OIDC login via system browser (Keycloak, PKCE).
4. App: generates a device key pair in Android Keystore (non-exportable; StrongBox if available)  [M6]
5. App → POST /sync/v1/devices/register {model, os, app_version, public_key}
   → device PENDING_APPROVAL (audit event)
6. Supervisor or admin sees the request (web) with user, model, time → Approve / Reject
   → ACTIVE. Any previous ACTIVE device of the user becomes REVOKED(REPLACED): one active device per user.
7. App → POST /sync/v1/grant → first offline grant → first pull.
8. App asks the user to set an app PIN (biometric optional).
```

Before step 6 the app can do nothing except show "waiting for approval".

## 5.4 Credentials and sessions

| Credential | Lifetime | Stored | Revocation |
|---|---|---|---|
| Keycloak access token | **5 min** | memory | expires; our backend also checks user and device status on every request |
| Keycloak refresh token (mobile client) | idle ≤ 24 h, **rotation on every use** ("revoke refresh token" enabled) | encrypted app storage | Keycloak logout-all for the user; user disable |
| **Offline grant** (ours) | `policy.offline_grace_hours` (**default 24, allowed 1–72, configurable, never hard-coded**) | encrypted app storage | `REVOKED` on device revoke, user disable, or a new grant |
| Device key (M6) | life of the installation | Android Keystore (non-exportable) | server marks the public key revoked with the device |
| App PIN / biometric | local | Keystore-protected | not a server credential. It protects against family or shoulder access |

**Every mobile request** carries `Authorization: Bearer <access token>` and `X-Device-Id`. The server rejects the request if any of these holds:
- the user is not `ACTIVE`;
- the device is not `ACTIVE`;
- the device does not belong to the token's subject;
- the app version is below `policy.min_app_version` (response `426`).

From M6, `POST /sync/v1/push` and `POST /sync/v1/grant` also require a **device signature** over `sha256(canonical body) || timestamp || nonce`, and nonces are single-use. A stolen token without the device's Keystore key then cannot write anything.

## 5.5 The offline grant

The server issues the grant only when online, and only to an `ACTIVE` user on an `ACTIVE` device:

```json
{
  "grant_id": "01J…",
  "user_id": "…", "device_id": "…",
  "roles": ["TECHNICIAN"],
  "issued_at": "2026-10-01T06:00:00Z",
  "expires_at": "2026-10-02T06:00:00Z",
  "policy_hours": 24
}
```

The grant is signed with the server's grant key (Ed25519; key from secret configuration; rotation supported by `kid`).

**On the phone:**
- The app verifies the signature with the pinned public key.
- Expiry is enforced using **elapsed time since the grant was received** (`SystemClock.elapsedRealtime`), not the wall clock, so changing the phone's clock does not extend the grant.
- After a reboot, elapsed time resets. The app then uses `max(wall-clock estimate, last known trusted time)` and marks commands `time_confidence = LOW`.
- The app renews the grant whenever it is online (each successful sync). While connectivity lasts, a technician never sees the grace period expire.

**When the grant has expired, the app enters a locked state:**
- **No new work actions** (no arrival, evidence, consumption, submission, registration, fault report).
- The only screen is "Connect to the internet and sign in again", with the number of items waiting to sync.
- Commands already in the outbox are **kept**, encrypted, and are sent after re-authentication (subject to the server rules below).

**On the server (the authoritative check).** Every pushed command carries its `grant_id`:

| Condition | Result |
|---|---|
| user or device not `ACTIVE` at push time | whole request `401/403`; **nothing is applied** |
| unknown `grant_id`, or grant belongs to another device or user | `REJECTED(GRANT_INVALID)` + `WARNING` flag |
| command's estimated creation time (05 §5.7) within `[issued_at, expires_at]` and grant not revoked before that time | normal processing |
| estimated creation time > `expires_at` with `time_confidence = HIGH` | `REJECTED(GRANT_EXPIRED)`, recorded, `WARNING` flag (a normal app cannot do this, so it indicates tampering or a bug) |
| creation time cannot be proven inside the window (`time_confidence = LOW`) | `QUARANTINED(GRANT_WINDOW_UNPROVEN)`; a supervisor decides (apply or discard-with-record) |
| grant revoked, and command time after the revocation time | `REJECTED(GRANT_REVOKED)` |

## 5.6 Offboarding and revocation (server-side only)

When an employee leaves, resigns, or loses the phone:

```
Admin → Disable user   (or Supervisor/Admin → Revoke device, for a lost/replaced phone)
  1. users.status = DISABLED            (or devices.status = REVOKED)
  2. all devices of the user → REVOKED; public keys revoked
  3. all offline grants → REVOKED
  4. Keycloak: user disabled + logout all sessions (refresh tokens invalid)
  5. auth cache invalidated → the next request from that phone is rejected
  6. future sync rejected; no new work action can be accepted
  7. open work orders of the user → flagged "needs reassignment" on the supervisor's queue
  8. custody balance ≠ 0 → INFO flag "custody to reconcile" (return of materials is an HR/warehouse task)
  9. every step above is audited in one transaction (Keycloak calls are made after commit
     with retry; failure raises a CRITICAL operational alert, because our own checks already block access)
```

**What remains on the phone and why that is acceptable:** data synced to the phone before revocation (assigned WOs, the zone's asset index, the user's own unsynced evidence) stays in the encrypted app sandbox. It is protected by the app PIN and the Keystore-bound DB key, the grant expires within the grace period, and no server will accept anything from that device again. **This residual risk is documented and accepted** for the pilot. The long-term remedy is MDM / Android Enterprise, to be evaluated later.

**Best-effort hygiene (never relied on):** if a revoked app does reach the server again, the server responds `DEVICE_REVOKED`. The app then discards its own local encryption key, which makes its **own** sandbox data unreadable, and returns to the start screen. It touches nothing outside its sandbox. Security does not depend on this happening.

**Planned offboarding procedure** (runbook): the web shows, per device, `last_sync_at` and `last_reported_pending_commands`. Before disabling a departing employee in a non-urgent case, the supervisor asks them to sync, so legitimate work is not lost. In urgent cases, revoke immediately and reassign the open work orders. The new assignee re-captures arrival evidence.

**Phone replacement:** approving a new device auto-revokes the old one. The approval screen warns if the old device last reported pending commands.

## 5.7 Trusted time (same design as long-term spec §10.6)

Every command and evidence item records `device_time`, `elapsed_ms`, and `boot_id`. Every successful sync records a clock sample `(server_time, elapsed_ms, boot_id)`.

`estimated_time = sample.server_time + (event.elapsed_ms − sample.elapsed_ms)` when a sample exists for the same `boot_id` (`time_confidence = HIGH`). Otherwise the wall clock is used, bounded by the last trusted time and the server receipt time (`LOW`).

`|device_time − estimated_time| > 5 min` produces a `CLOCK_SKEW` `INFO` flag, raised to `WARNING` if it affects SLA or the grant window.

## 5.8 Local data protection

- SQLite encrypted with SQLCipher. The DB key is wrapped by an Android Keystore key.
- Evidence files are encrypted (AES-GCM, Keystore-wrapped key) in app-private storage. They are deleted locally after the server confirms `VERIFIED`, plus a short retention (policy, default 3 days).
- App lock (PIN/biometric) after `policy.app_lock_minutes` in background (default 5).
- `FLAG_SECURE` on evidence and asset screens (no screenshots or screen recording of evidence).
- Release builds trust **system CAs only**. Debug builds may trust the dev CA. Certificate pinning is deferred to avoid lock-out during certificate rotation.
- Integrity signals are **flags only**: the mock-location flag per fix (`Location.isMock()` on API 31+, `isFromMockProvider()` below). A root-detection heuristic is recorded as device metadata. There is no Google Play Integrity dependency.

## 5.9 Policy settings (configurable, audited, range-validated)

| Key | Default | Range | Used by |
|---|---|---|---|
| `offline_grace_hours` | 24 | 1–72 | grant issuance |
| `grant_window_tolerance_minutes` | 10 | 0–60 | server grant check |
| `app_lock_minutes` | 5 | 1–30 | app |
| `min_app_version` | current | — | API |
| `evidence_local_retention_days` | 3 | 1–14 | app |
| `issue_ack_timeout_hours` | 24 | 1–168 | inventory flag |
| `core_return_due_days` | 7 | 1–30 | inventory flag |
| `proximity_duplicate_m` | 5 | 1–50 | registration flag |
| `repeated_failure_count` / `repeated_failure_days` | 3 / 180 | 2–10 / 30–730 | rule |
| `rework_window_days` | 30 | 7–180 | WO creation |

Changing any policy is an audited command (`ADMIN`) and raises an `INFO` flag for the auditor.

## 5.10 BYOD threats and what covers them

| Threat | Covered by | Residual |
|---|---|---|
| Ex-employee keeps using the app | Server revocation + grant expiry | Reads of already-cached data until the grant expires (≤ grace hours) |
| Lost or stolen phone | App PIN, encrypted storage, revocation, grant expiry | Same as above |
| Family member uses the app | App PIN/biometric | PIN shared deliberately by the employee |
| Mock GPS | `is_mock` flag, tag in photo, presence score, supervisor review, (later) random field audits | Rooted phones can hide mock status. GPS is never treated as proof on its own |
| Modified or old app | Min version, server-side validation of everything, device-signed batches (M6) | Reading and replaying its own data is possible; writing forged data needs the Keystore key |
| Token theft | 5-min access tokens, refresh rotation, device check, device signature on writes (M6) | Read-only exposure for the token lifetime |
| Clock manipulation | Trusted time + server grant window | Low-confidence cases go to human review |
