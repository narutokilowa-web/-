# DEMO 1 — click-by-click script (20 steps)

The DEMO 1 lifecycle of one street light on one laptop and one Android phone (docs/lean-pilot/11 §11.1).
All data is **synthetic** (`deploy/seed/demo-data.json`); nothing here is a fact about a real municipality.
The same steps run automatically in CI: `Mfop.GoldenPath.Tests` (steps 2–19 through the API),
`web/e2e/m3-demo1-flow.spec.ts` (web screens) and the Android emulator job (phone screens).

Time: about 20–25 minutes. Language: Arabic (right-to-left); the **English** button switches the web console.

## 0. Prepare (once, before the audience arrives)

```bash
make demo-reset      # wipes the dev volumes, builds and starts the stack (≈ 5–10 min the first time)
make seed            # dev users, reference data, 8 items with SYP costs, stock, 10 lights, the repeat-failure light
make verify-audit    # RESULT: OK — no tampering detected.
```

Phone (Android 8+, USB debugging on), with the APK from CI (`mfop-field-debug-apk`) or `make apk`:

```bash
adb install -r app-debug.apk
adb reverse tcp:5080 tcp:5080   # API
adb reverse tcp:8080 tcp:8080   # Keycloak
```

Passwords of all dev users: `MFOP_DEV_USER_PASSWORD` in `.env`. Use one browser window per user (or private windows).

`make seed` prints the **repeat-failure light** (`SL-000001`, "شارع العرض التجريبي — العمود المتكرر العطل"):
it already has two closed LED-module repairs inside the 180-day window. Print its label too (step 5) if you want
to scan it for step 17a.

Banner at the top of the web console: "تخزين محلي — ليس تخزينًا غير قابل للكتابة (WORM)". DEMO 1 stores photos on
the laptop's disk; WORM storage and signed checkpoints are mandatory before a real pilot (M8).

## The 20 steps

| # | Who / where | Click by click | What the audience sees |
|---|---|---|---|
| 1 | Terminal | `make up` (already done in step 0) | Stack healthy: web http://localhost:5173, API :5080, Keycloak :8080 |
| 2 | Web, `registrar1` | Open http://localhost:5173 → **تسجيل الدخول** → Keycloak → `registrar1` | Home page; footer shows roles "from the platform database" |
| 3 | Web, `registrar1` | **تسجيل عمود إنارة جديد** → fill the form (coordinates, power source, address …) → save the draft → **تقديم للاعتماد** | Registration SUBMITTED; the registrar cannot approve it (maker ≠ checker) |
| 4 | Web, `approver1` | **طابور الاعتماد** → the registration → **اعتماد** with a note | Asset code (e.g. `SL-000011`), lifecycle ACTIVE |
| 5 | Web, `approver1` | Asset page → QR and code → **طباعة الملصق** | A6 label with QR and the human code |
| 6 | Phone, `tech1` | Open the app → accept the privacy notice → **تسجيل الدخول** (Keycloak in the phone browser) → "waiting for approval". Web `supervisor1` → **الأجهزة** → approve. App → **تحقق الآن** → set a PIN → **مسح** → point the camera at the label | The phone was approved on the server; the scan resolves the label |
| 7 | Phone | (after the scan) | Asset card: code, "يعمل", power source, responsible party, open faults |
| 8 | Web, `supervisor1` | Asset page → **الإبلاغ عن عطل** (LIGHT_OFF, power available, description) → **إرسال البلاغ** | Light turns **معطّل** (FAULTY); timeline entry |
| 9 | Web, `supervisor1` | **الأعطال** → the fault → **إنشاء أمر عمل** (P2) → choose `tech1` → **إسناد** | Work order ASSIGNED. On the phone: **مهامي** → pull to refresh → the task |
| 10 | Phone | Open the task → **وصلت** → scan the label (or type the code **and** a reason) → **صور قبل العمل** → **التقاط صورة** twice | In-app camera only; each photo is re-encoded, stamped, hashed, and queued; "photos waiting" then 0 |
| 11 | Phone | **التشخيص** → **اختيار** → "عطل وحدة الليد" (only codes for this power source are shown) → **حفظ** | Diagnosis recorded |
| 12 | Phone | **بدء العمل** → **ابدأ** | State IN_PROGRESS; light shows **قيد الصيانة** on the web |
| 13 | Phone | **المواد المستخدمة** → **إضافة مادة** → "وحدة ليد 50 واط" → serial `M-0003` → **حفظ** | Consumption from tech1's custody (cost is in SYP on the review screen) |
| 14 | Phone | **قائمة الفحص الوظيفي** → **فتح** → method "اختبار ليلي" → answer every item → **حفظ** → **صورة الاختبار** → **التقاط صورة** | Checklist of the template pinned to this work order |
| 15 | Phone | **صور بعد العمل** → **التقاط صورة** twice | 2/2 |
| 16 | Phone | **التقديم للمراجعة** → **تقديم** → root cause → **تقديم** | "Submitted; waiting for the supervisor's review" |
| 17 | Web, `supervisor1` | Home → **أوامر عمل بانتظار المراجعة** → the work order → section "العمل الميداني والمراجعة": before/test/after photos, diagnosis, material with its SYP cost, checklist, arrivals → **اعتماد وإغلاق** (disabled until every photo is VERIFIED) | State **مغلق**; **closure seal** (SHA-256) shown; fault RESOLVED; light **يعمل** (or UNKNOWN if not tested). `tech1` cannot approve their own work (SoD) |
| 17a | Phone + web | Repeat steps 8–17 on the **repeat-failure light** `SL-000001` (serial `M-0004`). Then open its asset page (web) or scan it (phone) | Orange banner **"عطل متكرر — توصية: يُنصح بفحص شامل للعمود"**, the 3 counted work orders, "العتبة 3 خلال 180 يومًا" (from `rule_settings`; `admin1` → **الإعدادات** shows and changes them, audited) |
| 18 | Web, `supervisor1` | **لوحة المتابعة** | Lights by status, open faults, open work orders by state, pending review, **recently completed** work orders with their seal |
| 19 | Web, `auditor1` | **سجل التدقيق** → type `work_order` → paste the work-order id (from its URL) → **بحث** | Every command: actor, **device** (phone commands), time, reason, old → new values, event hash; `work_order.closed` carries `seal_hash`. The work-order and asset pages show the same timeline |
| 20 | Terminal | `make verify-audit` → OK. Then `make tamper-demo` → `make verify-audit` | The verifier exits non-zero and names **`SEAL_MISMATCH` for the work order** (by its code) and the **stock balances** that no longer add up |

After step 20: `make tamper-undo` → `make verify-audit` → OK again (or `make demo-reset` for a fresh start).

### What step 20 does (and does not) show

`make tamper-demo` connects **as the database owner** (`mfop_owner`), disables the append-only trigger on
`inventory.stock_movements`, changes the consumed quantity of the most recently closed work order from 1 to 3, and
re-enables the trigger. That is exactly what a privileged database administrator *can* do. The system does not
prevent it; it **detects** it: the seal stored at closure (and in the hash-chained audit event `work_order.closed`)
no longer matches the operational rows, and the balances no longer equal the sum of the movements.
Tamper-evident and verifiable — not "impossible to change".

Other tampering the verifier catches (tests `AuditCoreTests`, `FieldExecutionTests`): an edited or deleted audit
event (broken chain at a `seq`), an edited approved asset snapshot, an edited asset row.

## If something goes wrong

| Symptom | Fix |
|---|---|
| Approve stays disabled | Photos are still being verified (a few seconds). The hint under the buttons says why; the page refreshes itself |
| Phone: "photos waiting" does not go down | No network to the laptop: check `adb reverse`, then **أنا → إرسال ومزامنة الآن** |
| Phone: a red "refused by the server" line | The server's reason is shown (e.g. `TAG_MISMATCH`: wrong label scanned). Fix and repeat the step |
| Step 17a shows no banner | The seeded light's earlier repairs are missing: `make seed` must have run on this database |
| Verifier not OK before step 20 | Do not demo on it: `make demo-reset && make seed` |
