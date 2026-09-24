import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { signIn } from './helpers';

const API = process.env.E2E_API_URL ?? 'http://localhost:5080';

async function accessToken(page: Page): Promise<string> {
  const token = await page.evaluate(() => {
    for (let i = 0; i < sessionStorage.length; i++) {
      const key = sessionStorage.key(i)!;
      if (key.startsWith('oidc.user:')) return (JSON.parse(sessionStorage.getItem(key)!) as { access_token: string }).access_token;
    }
    return null;
  });
  expect(token, 'signed-in session has an access token').toBeTruthy();
  return token!;
}

async function api(request: APIRequestContext, token: string, method: 'GET' | 'POST', path: string, body?: unknown, deviceId?: string) {
  const headers: Record<string, string> = { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() };
  if (deviceId) headers['X-Device-Id'] = deviceId;
  const response = method === 'GET' ? await request.get(`${API}${path}`, { headers }) : await request.post(`${API}${path}`, { headers, data: body ?? {} });
  return { status: response.status(), json: (await response.json().catch(() => null)) as Record<string, unknown> | null };
}

// M2 / DEMO 1 steps 8–9 on the web, plus device approval and revocation (the phone side runs on the Android emulator job).
test('device approval → fault → work order → assignment → task visible to the technician → revocation', async ({ browser, request }) => {
  // tech2 registers a phone (the app does exactly this call).
  const techContext = await browser.newContext();
  const techPage = await techContext.newPage();
  await signIn(techPage, 'tech2');
  const techToken = await accessToken(techPage);
  const registered = await api(request, techToken, 'POST', '/sync/v1/devices/register', { platform: 'ANDROID', model: 'E2E Phone', osVersion: '14', appVersion: '0.2.0' });
  expect(registered.status).toBe(201);
  const deviceId = registered.json!.deviceId as string;
  expect((await api(request, techToken, 'GET', '/sync/v1/pull', undefined, deviceId)).json!.code).toBe('DEVICE_NOT_APPROVED');

  // A synthetic ACTIVE light (registration UI is covered by the M1 spec).
  const regContext = await browser.newContext();
  const regPage = await regContext.newPage();
  await signIn(regPage, 'registrar1');
  const regToken = await accessToken(regPage);
  const address = `شارع الأعطال ${Date.now()}`;
  const draft = await api(request, regToken, 'POST', '/api/v1/registrations', {
    reason: 'BASELINE_SURVEY',
    attributes: { latitude: 33.51, longitude: 36.29, powerSource: 'GRID', responsibleParty: 'MUNICIPALITY', installedOnPrecision: 'UNKNOWN', observedStatus: 'OPERATIONAL', addressText: address },
  });
  const registrationId = draft.json!.id as string;
  expect((await api(request, regToken, 'POST', `/api/v1/registrations/${registrationId}/submit`)).status).toBe(200);
  const apprContext = await browser.newContext();
  const apprPage = await apprContext.newPage();
  await signIn(apprPage, 'approver1');
  const apprToken = await accessToken(apprPage);
  expect((await api(request, apprToken, 'POST', `/api/v1/registrations/${registrationId}/approve`, { note: 'e2e' })).status).toBe(200);
  const assetId = (await api(request, apprToken, 'GET', `/api/v1/registrations/${registrationId}`)).json!.assetId as string;
  await regContext.close();
  await apprContext.close();

  // Supervisor: approve the phone on the Devices page.
  const supContext = await browser.newContext();
  const sup = await supContext.newPage();
  await signIn(sup, 'supervisor1');
  await sup.getByTestId('home-pending-devices').click();
  const row = sup.locator(`tr[data-device-id="${deviceId}"]`);
  await expect(row.getByTestId('device-status')).toHaveText('بانتظار الموافقة');
  await row.getByTestId('device-approve').click();
  await expect(row).toHaveCount(0); // left the pending list
  await sup.getByTestId('device-filter-ACTIVE').click();
  await expect(sup.locator(`tr[data-device-id="${deviceId}"]`).getByTestId('device-status')).toHaveText('فعّال');

  // Report a fault on the asset page → the light turns FAULTY.
  await sup.goto(`/assets/${assetId}`);
  await expect(sup.getByTestId('operational-status')).toHaveText('يعمل');
  await sup.getByTestId('fault-category').selectOption('LIGHT_OFF');
  await sup.getByTestId('fault-power-CONFIRMED_AVAILABLE').check();
  await sup.getByTestId('fault-description').fill('لا تضيء منذ يومين');
  await sup.getByTestId('fault-submit').click();
  await expect(sup.getByTestId('fault-result')).toContainText('سُجّل عطل جديد');
  await expect(sup.getByTestId('operational-status')).toHaveText('معطّل');

  // Triage: open the fault, create a work order, assign it to tech2.
  await sup.locator('[data-testid="faults"] [data-testid="fault-link"]').first().click();
  await expect(sup.getByTestId('fault-state')).toHaveText('مفتوح');
  await sup.getByTestId('wo-priority').selectOption('P2');
  await sup.getByTestId('wo-note').fill('تفقد وإصلاح');
  await sup.getByTestId('wo-create').click();
  await expect(sup.getByTestId('wo-state')).toHaveText('جديد');
  await expect(sup.getByTestId('wo-code')).toHaveText(/^WO-\d{4}-\d{6}$/);
  const woCode = (await sup.getByTestId('wo-code').textContent())!;
  const tech2 = sup.getByTestId('wo-assignee-select').locator('option', { hasText: 'tech2' });
  await expect(tech2).toBeEnabled();
  await sup.getByTestId('wo-assignee-select').selectOption({ value: (await tech2.getAttribute('value'))! });
  await sup.getByTestId('wo-assign').click();
  await expect(sup.getByTestId('wo-state')).toHaveText('مُسند');
  await expect(sup.getByTestId('wo-assignee')).toHaveText('فني تجريبي ٢');
  await expect(sup.locator('[data-action="work_order.assigned"] [data-testid="actor"]')).toContainText('مشرف تجريبي');
  await expect(sup.locator('[data-action="fault.in_work_order"]')).toBeVisible();

  // The technician's pull (what "My tasks" shows) now contains the task.
  const pull = await api(request, techToken, 'GET', '/sync/v1/pull', undefined, deviceId);
  expect(pull.status).toBe(200);
  expect((pull.json!.workOrders as { code: string }[]).map((w) => w.code)).toContain(woCode);

  // Revoke the phone: its very next request is rejected (server-side only; nothing on the phone is touched).
  await sup.goto('/devices?status=ACTIVE');
  const activeRow = sup.locator(`tr[data-device-id="${deviceId}"]`);
  await activeRow.getByTestId('device-reason').fill('نهاية التجربة');
  await activeRow.getByTestId('device-revoke').click();
  await expect(activeRow).toHaveCount(0);
  expect((await api(request, techToken, 'GET', '/sync/v1/pull', undefined, deviceId)).json!.code).toBe('DEVICE_REVOKED');

  await techContext.close();
  await supContext.close();
});
