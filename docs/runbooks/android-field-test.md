# Android field test on a real phone (DEMO 1 steps 6–17)

The automated check runs on every CI build (job **Android emulator**): the real app on Android 14, against the Docker Compose stack, with screenshots (artifact `android-e2e-screenshots`). Three things cannot be automated, and this checklist covers them on a real phone: **the Keycloak login page in the phone's browser**, **the live camera scan of a printed label**, and **taking work photos with the in-app camera**.

## Prepare

1. Laptop: `make up && make seed` (or `make demo-reset && make seed`), then sign in to the web console at http://localhost:5173.
2. Get the APK: CI artifact `mfop-field-debug-apk`, or `make apk` where Google Maven is reachable.
3. Phone (Android 8+), USB debugging on, connected by USB:
   ```bash
   adb install -r app-debug.apk
   adb reverse tcp:5080 tcp:5080   # API
   adb reverse tcp:8080 tcp:8080   # Keycloak
   ```
   The phone now reaches the laptop as `localhost`. Nothing else is installed or changed on the phone.
4. Print a label: web → Assets → a street light → **Print label** (A6).

## Checklist

| # | Action | Expected |
|---|---|---|
| 1 | Open the app | Arabic, right-to-left. Privacy notice (camera for QR only, no location tracking, no remote wipe) |
| 2 | Accept → **Sign in** | The phone's browser opens the Keycloak page. Sign in as `tech1` (password `MFOP_DEV_USER_PASSWORD` from `.env`) and return to the app |
| 3 | — | "Waiting for device approval", with the device id |
| 4 | Web as `supervisor1` (or `admin1`) → **Devices** → pending | The phone appears with its model and Android version. **Approve** |
| 5 | App → **Check now** | "Choose an app PIN". Set a 4–8 digit PIN |
| 6 | Web as `supervisor1`: asset page → **Report a fault** (power available) → Faults → the fault → **Create work order** → assign to `tech1` | Light becomes FAULTY; work order ASSIGNED |
| 7 | App → **Me** → **Send and sync now** → **My tasks** | The work order is listed; opening it shows the asset code, "معطّل", power source, responsible party |
| 8 | App → **Scan** → point the camera at the printed label (allow camera access once) | The asset card for that light, with "You have a task on this light" |
| 9 | Scan with a damaged/unreadable label: type the printed code (e.g. `7K3M92QX.4F`) | Same card. A wrong check code → "No active asset for this code" |
| 10 | Put the app in the background for more than 5 minutes, reopen | PIN prompt |
| 10a | Open the task → **وصلت** → scan the label | Arrival recorded (a wrong label is refused with `TAG_MISMATCH`) |
| 10b | **صور قبل العمل** → **التقاط صورة** twice (allow camera access once) | In-app camera, full screen; no photo appears in the phone's gallery; a screenshot of the camera screen is blocked (FLAG_SECURE) |
| 10c | Diagnosis → start → material (`LED-MOD-50W`, a serial from your custody) → checklist → test photo → 2 after photos → **تقديم** | "Submitted; waiting for the supervisor's review"; the status strip shows no photos waiting |
| 10d | Web as `supervisor1`: the work order → review | The five photos (stamped with the work-order code and UTC time), material with its SYP cost, checklist → **اعتماد وإغلاق** → seal shown |
| 10e | On a second task: unplug the USB cable (the phone loses the laptop; airplane mode does not, `adb reverse` runs over USB), take a before photo, plug back in, run the two `adb reverse` commands again, **إرسال ومزامنة الآن** | While unplugged the photo waits on the phone (status strip, offline); it uploads once connected |
| 11 | Web → Devices → Active → the phone → reason → **Revoke** | — |
| 12 | App → **Send and sync now** (or any action) | "This device was revoked". The app has deleted its own data; open the phone's gallery/files: nothing of the app is there, nothing else changed |

## M4 checks on the real phone (app 0.4.0)

Uninstall the M3 app first (its registration has no device key), install the M4 APK, and register again (steps 1–5).

| # | Action | Expected |
|---|---|---|
| 13 | Web as `admin1` → **Devices** → the new phone | No "no device signing key" warning: the Keystore key was registered |
| 14 | App → **أنا** | "Offline work allowed until …" about 24 h from now (policy `offline_grace_hours`) |
| 15 | Web as `admin1` → **Settings → Security policies**: set `offline_grace_hours` to 1 (reason). App → **إرسال ومزامنة الآن** | The Me tab shows about 1 hour |
| 16 | Unplug USB (offline). Record an arrival and a before photo on a task. Change the phone's clock **back** by a day. Wait until the hour passes (or set the policy to 1 h and wait) | New work is refused with "Connect to continue working" after the hour, even with the clock set back; items recorded before stay counted on that screen |
| 17 | Plug back in, `adb reverse` again, **حاول الآن** | The outbox is sent, a new grant arrives, the app returns to the tasks; the web shows the arrival and photo. Restore `offline_grace_hours` to 24 |
| 18 | While a task is open offline, reassign it on the web to `tech2`; on the phone record a diagnosis and a photo; reconnect | The diagnosis is refused ("reassigned to another technician"); the photo is kept and flagged on the work order |
| 19 | Web as `admin1` → **Users** → `tech1` → reason → **Disable** → confirm. App → any action | "Your account was disabled"; the app deleted its own data and key. Nothing else on the phone changed. Then **Enable again** `tech1` (a new device approval is needed) |

Record the phone model, Android version, lighting and label condition for each run, and the time the field steps took (DEMO 1 DoD: interaction time recorded). Report any Arabic wording a technician finds unclear.
