import { defineConfig } from '@playwright/test';

// E2E runs against the full Docker Compose stack (make up). Base URL = web container.
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  retries: 0,
  // One worker: tests sign in the same Keycloak users (e.g. registrar1). Keycloak's brute-force detector can
  // report concurrent sign-ins of one user as user_temporarily_disabled (see IMPLEMENTATION_STATUS, M1 warnings).
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
    locale: 'ar',
    trace: 'retain-on-failure',
    launchOptions: process.env.PW_CHROMIUM_EXECUTABLE ? { executablePath: process.env.PW_CHROMIUM_EXECUTABLE } : {},
  },
});
