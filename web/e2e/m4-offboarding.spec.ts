import { expect, test } from '@playwright/test';
import { password, signIn } from './helpers';

// M4 on the real stack: security policies are data the admin changes with a reason; offboarding disables the account in
// our database AND in Keycloak (the API's service account, after commit), so the person can no longer sign in; enabling
// restores sign-in. inspector1 is used because no other scenario depends on it, and it is enabled again at the end.
test('admin changes a security policy, offboards a user (Keycloak sign-in blocked), and enables them again', async ({ browser }) => {
  test.setTimeout(180_000);
  const adminContext = await browser.newContext();
  const admin = await adminContext.newPage();
  await signIn(admin, 'admin1');

  await admin.getByTestId('nav-settings').click();
  await expect(admin.getByTestId('policy-value-offline_grace_hours')).toHaveText('24');
  await expect(admin.getByTestId('policy-value-min_app_version')).toHaveText('0.4.0');
  await admin.getByTestId('policy-input-app_lock_minutes').fill('6');
  await admin.getByTestId('policy-save-app_lock_minutes').click();
  await expect(admin.getByTestId('problem')).toHaveAttribute('data-code', 'REASON_REQUIRED');
  await admin.getByTestId('policy-reason-app_lock_minutes').fill('اختبار E2E');
  await admin.getByTestId('policy-save-app_lock_minutes').click();
  await expect(admin.getByTestId('policy-value-app_lock_minutes')).toHaveText('6');
  await admin.getByTestId('policy-input-app_lock_minutes').fill('5');
  await admin.getByTestId('policy-reason-app_lock_minutes').fill('إعادة القيمة');
  await admin.getByTestId('policy-save-app_lock_minutes').click();
  await expect(admin.getByTestId('policy-value-app_lock_minutes')).toHaveText('5');

  // Offboarding.
  await admin.getByTestId('nav-users').click();
  const row = admin.locator('tr[data-username="inspector1"]');
  await expect(row.getByTestId('user-status')).toHaveText('فعّال');
  await row.getByTestId('user-reason').fill('انتهاء العقد (E2E)');
  await row.getByTestId('user-disable').click();
  await row.getByTestId('user-disable-confirm').click();
  await expect(row.getByTestId('user-status')).toHaveText('معطّل');
  // The Keycloak updates are sent after commit by the API's worker: wait until nothing is pending.
  await expect(async () => {
    await admin.reload();
    await expect(admin.locator('tr[data-username="inspector1"]').getByTestId('user-idp')).toHaveCount(0);
  }).toPass({ timeout: 60_000 });

  // inspector1 can no longer sign in at Keycloak.
  const blockedContext = await browser.newContext();
  const blocked = await blockedContext.newPage();
  await blocked.goto('/');
  await blocked.getByTestId('login').click();
  await blocked.locator('#username').fill('inspector1');
  await blocked.locator('#password').fill(password());
  await blocked.locator('#kc-login').click();
  await expect(blocked.getByTestId('username')).toHaveCount(0);
  await expect(blocked.locator('#input-error, .kc-feedback-text, [id^="input-error"], .pf-v5-c-alert__title').first()).toBeVisible();
  await blockedContext.close();

  // Enable again (reason required); sign-in works once Keycloak is updated.
  const again = admin.locator('tr[data-username="inspector1"]');
  await again.getByTestId('user-reason').fill('عاد إلى العمل (E2E)');
  await again.getByTestId('user-enable').click();
  await expect(again.getByTestId('user-status')).toHaveText('فعّال');
  await expect(async () => {
    await admin.reload();
    await expect(admin.locator('tr[data-username="inspector1"]').getByTestId('user-idp')).toHaveCount(0);
  }).toPass({ timeout: 60_000 });
  const backContext = await browser.newContext();
  await signIn(await backContext.newPage(), 'inspector1');
  await backContext.close();

  // Supervisor queues exist: held commands and work orders needing reassignment.
  const supContext = await browser.newContext();
  const sup = await supContext.newPage();
  await signIn(sup, 'supervisor1');
  await expect(sup.getByTestId('home-needs-reassignment')).toBeVisible();
  await sup.getByTestId('nav-quarantine').click();
  await expect(sup.getByTestId('quarantine-filter-PENDING')).toBeVisible();
  for (const c of [adminContext, supContext]) await c.close();
});
