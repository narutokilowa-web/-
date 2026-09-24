# mfop_field — Android field app (Flutter)

Arabic-first (RTL), offline-first field client. **M0: skeleton only.** Sign-in and QR scanning arrive in M2 (`docs/lean-pilot/11-milestones-and-dod.md`).

```bash
make test-mobile   # flutter analyze + tests (local Flutter 3.41.6 or the pinned Docker image)
make apk           # debug APK → mobile/build/app/outputs/flutter-apk/app-debug.apk
```

- Application id `local.mfop.mfop_field` is a **placeholder** until the municipality chooses its own identifier.
- `minSdk` 26 (Android 8.0).
- UI text lives in `lib/l10n/app_ar.arb` (template) and `app_en.arb`. Never hard-code UI strings.
