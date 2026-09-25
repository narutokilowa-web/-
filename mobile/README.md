# mfop_field — Android field app (Flutter)

Arabic-first (RTL), offline-first field client for personal phones (BYOD). M2: sign-in, device registration and approval, app PIN, encrypted local database, command outbox and sync, **Scan** (camera QR + typed code) → asset card, **My tasks**. On-site execution (arrival, photos, diagnosis, materials) comes in M3.

```bash
make test-mobile   # flutter analyze + unit/widget tests (local Flutter 3.41.6 or the pinned Docker image)
make apk           # debug APK → mobile/build/app/outputs/flutter-apk/app-debug.apk
make android-e2e   # integration test on a connected phone/emulator against `make up` (see docs/runbooks/android-field-test.md)
```

| Piece | Where |
|---|---|
| Screens by state (privacy → sign-in → waiting for approval → PIN → tabs; locked, rejected, revoked) | `lib/app/app_controller.dart`, `lib/features/` |
| Keycloak login (system browser, PKCE, client `mfop-mobile`, redirect `mfop://oauth-callback`) | `lib/core/auth.dart` |
| API client (bearer token + `X-Device-Id` on every request) | `lib/core/api_client.dart` |
| Encrypted local DB (SQLite3 Multiple Ciphers, SQLCipher v4 scheme; random key in Keystore-backed storage) | `lib/core/local_db.dart` |
| Outbox (every write is a command with a UUIDv7 id) and sync (push in order, then pull) | `lib/core/outbox.dart`, `lib/core/sync_engine.dart` |
| App PIN (PBKDF2, lock after `MFOP_APP_LOCK_MINUTES`, default 5) | `lib/core/pin.dart` |
| Android integration test (CI job "Android emulator") | `integration_test/field_flow_test.dart` |

Build-time settings (`--dart-define`): `MFOP_API_URL` (default `http://localhost:5080`), `MFOP_ISSUER` (`http://localhost:8080/realms/mfop`), `MFOP_CLIENT_ID`, `MFOP_REDIRECT_URI`, `MFOP_ENV`, `MFOP_ALLOW_HTTP` (dev only), `MFOP_APP_LOCK_MINUTES`.

- The phone reaches the laptop through `adb reverse tcp:5080 tcp:5080` and `adb reverse tcp:8080 tcp:8080`. Plain HTTP is allowed **only in debug builds** and only for `localhost`, `127.0.0.1` and `10.0.2.2` (`android/app/src/debug/res/xml/network_security_config.xml`).
- Permissions: `INTERNET`, `CAMERA`. No location yet (it arrives with evidence capture in M3, foreground only). Cloud backup and device transfer of app data are disabled.
- Revocation is server-side. When the server answers `DEVICE_REVOKED`, the app deletes **its own** database, database key, tokens, device id and PIN, and nothing else.
- Android application id: `com.reuxstudio.mfop.field` (owner decision, 2026-09-24). It cannot change after devices install the app without a reinstall.
- `minSdk` 26 (Android 8.0). UI text lives in `lib/l10n/app_ar.arb` (template) and `app_en.arb`. Never hard-code UI strings.
