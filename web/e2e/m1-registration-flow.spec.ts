import { expect, test } from '@playwright/test';
import { signIn } from './helpers';

// M1 / DEMO 1 steps 2–5: registrar registers a synthetic light → a DIFFERENT user approves it →
// asset code + QR + printable label + history with old → new values; the QR link resolves back to the asset.
test('register → submit → separate approval → asset code, QR, label, history, tag link', async ({ browser }) => {
  const address = `شارع الاختبار ${Date.now()}`;

  // Registrar
  const registrarContext = await browser.newContext();
  const registrar = await registrarContext.newPage();
  await signIn(registrar, 'registrar1');
  await registrar.getByTestId('nav-new-registration').click();
  await registrar.getByTestId('fill-synthetic').click();
  await registrar.locator('input[name="addressText"]').fill(address);
  await registrar.getByTestId('save-draft').click();
  await expect(registrar.getByTestId('registration-state')).toHaveText('مسودة');
  await expect(registrar.getByTestId('approve')).toHaveCount(0); // maker cannot be checker
  await registrar.getByTestId('submit').click();
  await expect(registrar.getByTestId('registration-state')).toHaveText('مقدَّم للاعتماد');
  await expect(registrar.getByTestId('decision-panel')).toHaveCount(0);
  await registrarContext.close();

  // Approver (a different user)
  const approverContext = await browser.newContext();
  const approver = await approverContext.newPage();
  await signIn(approver, 'approver1');
  await approver.getByTestId('home-approval-queue').click();
  await approver.getByRole('link', { name: address }).click();
  await expect(approver.getByTestId('registrar-name')).toHaveText('مسجّل تجريبي');
  await approver.getByTestId('decision-note-input').fill('موافق بعد المراجعة');
  await approver.getByTestId('approve').click();

  // Asset page
  await expect(approver.getByTestId('asset-code')).toHaveText(/^SL-\d{6}$/);
  const assetCode = (await approver.getByTestId('asset-code').textContent())!;
  const assetUrl = approver.url();
  await expect(approver.getByTestId('lifecycle-state')).toHaveText('معتمد');
  await expect(approver.getByTestId('operational-status')).toHaveText('يعمل');
  await expect(approver.locator('[data-testid="qr"] svg')).toBeVisible();
  const humanCode = (await approver.getByTestId('human-code').textContent())!;
  expect(humanCode).toMatch(/^[0-9A-HJKMNP-TV-Z]{8}\.[0-9A-HJKMNP-TV-Z]{2}$/);
  await expect(approver.getByTestId('qr')).toHaveAttribute('data-payload', new RegExp(`/t/${humanCode.replace('.', '\\.')}$`));

  // History: registration by registrar1, approval by approver1, lifecycle PENDING → ACTIVE, reason recorded
  const history = approver.getByTestId('history');
  await expect(history.locator('[data-action="registration.drafted"] [data-testid="actor"]')).toContainText('مسجّل تجريبي');
  await expect(history.locator('[data-action="registration.approved"] [data-testid="actor"]')).toContainText('معتمد تجريبي');
  await expect(history.locator('[data-action="registration.approved"]')).toContainText('موافق بعد المراجعة');
  const lifecycle = history.locator('[data-action="street_light.activated"] [data-field="lifecycle_state"]');
  await expect(lifecycle.locator('.old')).toHaveText('PENDING');
  await expect(lifecycle.locator('.new')).toHaveText('ACTIVE');
  await expect(history.locator('[data-action="asset_version.created"]')).toContainText('snapshot_hash');
  // Long audited values (snapshot JSON, hashes) wrap: no horizontal page scroll
  expect(await approver.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);

  // Printable A6 label (same tab: the session lives in this tab's sessionStorage)
  await approver.getByTestId('print-label').click();
  await expect(approver).toHaveURL(`${assetUrl}/label`);
  await expect(approver.getByTestId('label-asset-code')).toHaveText(assetCode);
  await expect(approver.getByTestId('label-human-code')).toHaveText(humanCode);
  await expect(approver.locator('[data-testid="label-qr"] svg')).toBeVisible();
  await approver.getByTestId('back-to-asset').click();
  await expect(approver.getByTestId('asset-code')).toHaveText(assetCode);

  // Scanning the QR opens /t/<code> → resolves to the same asset
  await approver.goto(`/t/${humanCode}`);
  await expect(approver.getByTestId('asset-code')).toHaveText(assetCode);

  // A forged check code does not resolve
  const forged = humanCode.slice(0, -2) + (humanCode.endsWith('AA') ? 'BB' : 'AA');
  await approver.goto(`/t/${forged}`);
  await expect(approver.getByTestId('tag-not-found')).toBeVisible();
  await approverContext.close();
});
