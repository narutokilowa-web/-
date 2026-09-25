# Offboarding an employee or a lost phone (M4, material return M5)

Everything happens **on the server**. Personal phones are never wiped or read (docs/lean-pilot/05 §5.6): if a revoked app
reaches the server again, it deletes only its own data and its own device key.

## A. Planned departure (resignation, end of contract)

1. **Before disabling:** web → **Devices** → the employee's phone: check *Last sync* and *waiting to send*. If items are
   waiting, ask the employee to open the app and tap **أنا → إرسال ومزامنة الآن**, so no legitimate work is lost.
2. `admin1` (ADMIN) → **Users** → the employee → reason (required) → **Disable (offboard)** → **Confirm**.
   In one audited transaction the server:
   - sets the user to DISABLED;
   - revokes every active device (`USER_DISABLED`) and rejects a pending registration;
   - revokes every offline grant;
   - queues the Keycloak updates: disable the account and end all its sessions (sent after commit, retried);
   - flags each open work order `NEEDS_REASSIGNMENT` and, if the employee holds materials, raises `CUSTODY_TO_RECONCILE`.
3. The *Sign-in (Keycloak)* column shows "Up to date" within seconds. If it shows an alert, check Keycloak and the
   `Keycloak__AdminClientSecret` / `MFOP_KEYCLOAK_BACKEND_SECRET` setting — the platform already refuses the person.
4. `supervisor1` → **Home → Work orders needing reassignment** → reassign each one (reason required) or cancel it.
   The new technician captures new arrival photos.
5. **Held commands** (Home → *Held commands to decide*): commands of the departed employee can only be **discarded**
   (with a reason) now.
6. Custody: the warehouse officer records what is handed back (section D). Material the employee declared as returned
   before leaving, or issues they never acknowledged, are handled as usual (receive the return; cancel the issue).

## D. Material handed back after offboarding (M5)

A disabled user cannot act on the phone, so the warehouse officer records the handover from their custody. Nothing is
written off: what is not brought back stays in their custody and is flagged for review.

1. `warehouse1` → **Stock → Returns** → *Material handed back by a disabled technician* → choose the employee. Their custody
   (quantities and serials) is listed.
2. Enter each item handed back (serials for serial-tracked items; only what is in that custody is accepted) and a note (who
   brought it, when) → **Record handover**. The material moves CUSTODY → WAREHOUSE (return `RET-…`, kind *Offboarding*), and
   an INFO flag `OFFBOARDING_RETURN_RECORDED` lists what came back and what is left.
3. When nothing more will come back, record a **final handover** (tick *Final handover*; lines may be empty). If material or
   failed parts are still outstanding, a WARNING flag `CUSTODY_NOT_RETURNED` is raised for the auditor.
4. **Stock → Reconciliation** keeps showing *Custody left with a disabled user* until the custody is empty.
5. Unacknowledged issues to the employee: **Stock → Issues** → cancel with a reason (the material returns to the warehouse).
   Failed parts the employee declared "with part": receive them as usual in **Stock → Failed parts**.

## B. Lost or stolen phone (the employee stays)

1. `supervisor1` or `admin1` → **Devices** → Active → the phone → reason → **Revoke**. Its offline grants are revoked and
   its field-app sessions are ended in Keycloak. Any request from it is refused (`DEVICE_REVOKED`).
2. The employee installs the app on the new phone, signs in, and a supervisor approves the new device.
3. Work the lost phone had not sent is lost with it; reassign affected work orders if needed.

## C. Re-hiring

`admin1` → **Users** → the employee → reason → **Enable again**. The account and Keycloak sign-in are restored; **devices stay
revoked**, so a new device registration must be approved.

## What the server refuses after offboarding

| From | Result |
|---|---|
| The disabled user's phone (any request) | `403 USER_DISABLED` / `DEVICE_REVOKED`; the app deletes its own data and key |
| The user's refresh token in Keycloak | disabled account, sessions ended |
| A command made before offboarding and quarantined | can only be discarded (`ACTOR_NOT_ACTIVE` on apply) |
| A copied access token without the phone's key | `SIGNATURE_REQUIRED` / `SIGNATURE_INVALID` on every write |
