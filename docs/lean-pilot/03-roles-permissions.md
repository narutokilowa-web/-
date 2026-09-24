# 03 — Minimal Roles and Permissions

## 3.1 Roles

| Role | Code | Pilot responsibility | Main client |
|---|---|---|---|
| Field Technician | `TECHNICIAN` | Executes assigned work orders, captures evidence, consumes from own custody, acknowledges issues, declares returns, reports faults | Mobile |
| Field Inspector | `INSPECTOR` | Inspection rounds (fault reports with power observation), field checks of registrations | Mobile |
| Supervisor | `SUPERVISOR` | Triage faults, create/assign work orders, review/close/return, approve devices of field staff, resolve sync quarantine, dashboard | Web |
| Asset Registrar | `REGISTRAR` | Field capture of new street lights, bind tags | Mobile |
| Asset Approver | `APPROVER` | Approve / return / reject registrations, request field checks | Web |
| Warehouse Officer | `WAREHOUSE` | Opening stock, issue to technicians, receive returns and failed parts | Web |
| Auditor | `AUDITOR` | Read everything, verify audit chain, view originals, decide flags | Web |
| System Administrator | `ADMIN` | Users, role assignments, devices (approve/revoke), policy settings, tag batches | Web |

**Deferred to later:** Department Manager (the Supervisor dashboard covers the pilot), Procurement, contractor roles.

## 3.2 Evaluation model

```
allowed = hasPermission(actor.activeRoles, action)          -- RBAC
       && actor.status == ACTIVE && (device == null || device.status == ACTIVE)
       && recordPolicy(action, actor, record)                -- assignee / maker≠checker / state
```

- Roles and permissions are **seeded in code**. There is no UI to create roles in the pilot.
- Authorization data is read from **our database on every request**, with a cache of at most 30 s that is invalidated on revoke. Token claims are used only for identity (`sub`).
- **Revocation is cheap, granting is controlled:** several roles may revoke (safety bias). Granting requires the specific role, and self-grant is never allowed.

## 3.3 Permission matrix

Legend: ✅ allowed · 👁 read · ⚠️ allowed with record policy (note) · — denied

| Action | TECH | INSP | SUP | REG | APPR | WH | AUD | ADMIN |
|---|---|---|---|---|---|---|---|---|
| View assets, map, asset history | 👁 | 👁 | 👁 | 👁 | 👁 | — | 👁 | — |
| Resolve tag (scan) | ✅ | ✅ | ✅ | ✅ | ✅ | — | ✅ | — |
| Create registration draft / bind tag / submit | — | — | — | ✅ | — | — | — | — |
| Record field check | — | ⚠️1 | — | — | — | — | — | — |
| Approve / return / reject registration, request field check | — | — | — | — | ⚠️2 | — | — | — |
| Create tag batch, print, void tag | — | — | — | — | — | — | — | ✅ |
| Report fault / confirm fault | ✅ | ✅ | ✅ | — | — | — | — | — |
| Dismiss fault | — | — | ✅ | — | — | — | — | — |
| Create work order, assign, reassign, cancel | — | — | ✅ | — | — | — | — | — |
| Execute work order (arrive, evidence, diagnose, start, hold, consume, checklist, submit, resolve-no-work) | ⚠️3 | — | — | — | — | — | — | — |
| Review work order (approve / return) | — | — | ⚠️4 | — | — | — | — | — |
| View evidence previews | ⚠️5 | ⚠️5 | ✅ | ⚠️5 | ✅ | — | ✅ | — |
| View evidence originals (with EXIF/GPS) | — | — | — | — | — | — | ✅ | — |
| Record opening stock (receipt) | — | — | — | — | — | ✅ | — | — |
| Issue stock to technician | — | — | — | — | — | ⚠️6 | — | — |
| Acknowledge / dispute issue, declare return, declare core return | ⚠️7 | — | — | — | — | — | — | — |
| Receive return / core part | — | — | — | — | — | ✅ | — | — |
| Waive core return | — | — | ⚠️8 | — | — | — | — | — |
| View stock balances | 👁 (own custody) | — | 👁 | — | — | 👁 | 👁 | — |
| Dashboard | — | — | 👁 | — | — | — | 👁 | — |
| View audit log, run chain verification | — | — | — | — | — | — | ✅ | — |
| View / decide flags | — | — | 👁 ⚠️9 | — | — | — | ✅ | — |
| Resolve sync quarantine | — | — | ✅ | — | — | — | 👁 | — |
| Manage users, role assignments | — | — | — | — | — | — | 👁 | ⚠️10 |
| Approve device | — | — | ✅ | — | — | — | — | ✅ |
| **Revoke device / disable user** | — | — | ✅ (device) | — | — | — | — | ✅ |
| Change policy settings (e.g. offline grace hours, rule params) | — | — | — | — | — | — | 👁 | ⚠️11 |
| **Delete any operational or audit record** | — | — | — | — | — | — | — | — |

Notes:
1. Inspector ≠ registrar of that registration.
2. Approver ≠ registrar of that registration (maker ≠ checker).
3. Only on work orders where the actor is the current assignee, and only in states that allow the action.
4. Reviewer ≠ any user who executed the work order (assignee or evidence author).
5. Only for records the user created or is assigned to.
6. Warehouse officer ≠ receiving technician (static SoD already prevents holding both roles).
7. Only on issues and returns addressed to or declared by the actor.
8. Waiving a core return requires a reason and is always flagged `INFO` to the auditor. Supervisor ≠ the technician concerned.
9. The supervisor sees flags about assets and work orders in scope but **cannot close flags**, and cannot see flags whose explanation names the supervisor as a related user.
10. No self-grant. Granting `APPROVER`, `SUPERVISOR`, `WAREHOUSE` or `AUDITOR` raises an `INFO` flag to the auditor. Every grant and revoke is audited.
11. Every policy change is audited and raises an `INFO` flag. Values are validated against allowed ranges (for example offline grace between 1 and 72 hours).

## 3.4 Separation of duties

**Static (the same user cannot hold both roles).** Assignment is rejected with `SOD_ROLE_CONFLICT`:

| A | B |
|---|---|
| REGISTRAR | APPROVER |
| WAREHOUSE | TECHNICIAN |
| AUDITOR | any other role |
| ADMIN | any operational role (TECH, INSP, SUP, REG, APPR, WH) |

**Dynamic (per record):** registrar ≠ approver ≠ field-checker · executor ≠ reviewer · issuer ≠ receiver · actor ≠ target on self-grant.

## 3.5 Authentication (summary; details in 05)

| Client | Method |
|---|---|
| Web | Keycloak OIDC Authorization Code + PKCE. **TOTP MFA required** for SUPERVISOR, APPROVER, WAREHOUSE, AUDITOR, ADMIN in pilot configuration (may be relaxed in the `dev` profile) |
| Mobile | Keycloak OIDC (system browser, PKCE) **+ approved device registration + offline grant + app PIN/biometric** |

The permission matrix above is converted in M1 into `docs/lean-pilot/permissions.yaml`, and the authorization test suite is generated from that file.
