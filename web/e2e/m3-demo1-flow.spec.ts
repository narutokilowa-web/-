import { createHash } from 'node:crypto';
import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { E2EPhone, signIn } from './helpers';

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

type Json = Record<string, unknown>;

async function api(request: APIRequestContext, token: string, method: 'GET' | 'POST', path: string, body?: unknown, deviceId?: string) {
  const headers: Record<string, string> = { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() };
  if (deviceId) headers['X-Device-Id'] = deviceId;
  const response = method === 'GET' ? await request.get(`${API}${path}`, { headers }) : await request.post(`${API}${path}`, { headers, data: body ?? {} });
  return { status: response.status(), json: (await response.json().catch(() => null)) as Json | null };
}

/** A unique synthetic JPEG made by the browser's canvas (like the app, the bytes are hashed before upload). */
async function syntheticJpeg(page: Page, label: string): Promise<Buffer> {
  const base64 = await page.evaluate((text) => {
    const canvas = document.createElement('canvas');
    canvas.width = 320;
    canvas.height = 240;
    const ctx = canvas.getContext('2d')!;
    ctx.fillStyle = `hsl(${Math.floor(Math.random() * 360)} 60% 45%)`;
    ctx.fillRect(0, 0, 320, 240);
    ctx.fillStyle = '#fff';
    ctx.font = '20px sans-serif';
    ctx.fillText(`${text} ${Date.now()} ${Math.random()}`, 10, 120);
    return canvas.toDataURL('image/jpeg', 0.8).split(',')[1];
  }, label);
  return Buffer.from(base64, 'base64');
}

function sha256Hex(bytes: Buffer): string {
  return createHash('sha256').update(bytes).digest('hex');
}

// DEMO 1 steps 10–19 for the seeded light with two earlier repairs (make seed): the phone side is played through the
// same API the app uses (POST /sync/v1/push, PUT evidence content); the supervisor, dashboard, audit, stock and
// settings screens are exercised in the browser, in Arabic.
test('field work → review → approve and seal → recurrent-failure recommendation → dashboard → audit history', async ({ browser, request }) => {
  test.setTimeout(180_000);

  // tech1's phone (registered by the app's own call, approved by supervisor1).
  const techContext = await browser.newContext();
  const techPage = await techContext.newPage();
  await signIn(techPage, 'tech1');
  const techToken = await accessToken(techPage);
  const phone = new E2EPhone();
  const registered = await api(request, techToken, 'POST', '/sync/v1/devices/register', { platform: 'ANDROID', model: 'E2E Phone M3', osVersion: '14', appVersion: '0.5.0', publicKey: phone.publicKey });
  expect(registered.status).toBe(201);
  const deviceId = registered.json!.deviceId as string;

  const supContext = await browser.newContext();
  const sup = await supContext.newPage();
  await signIn(sup, 'supervisor1');
  const supToken = await accessToken(sup);
  expect((await api(request, supToken, 'POST', `/api/v1/devices/${deviceId}/approve`, { reason: null })).status).toBe(200);

  // The seeded repeat-failure light gets a new fault and work order assigned to tech1.
  const lights = await api(request, supToken, 'GET', `/api/v1/assets?q=${encodeURIComponent('العمود المتكرر العطل')}`);
  const light = (lights.json as unknown as Json[])[0];
  expect(light, 'make seed created the repeat-failure light').toBeTruthy();
  const assetId = light.id as string;
  const fault = await api(request, supToken, 'POST', '/api/v1/faults', { assetId, category: 'LIGHT_OFF', powerAtInspection: 'CONFIRMED_AVAILABLE', powerInfoSource: 'OBSERVED_ON_SITE', description: 'لا يضيء (E2E)' });
  const faultId = (fault.json!.faultId ?? fault.json!.id) as string;
  const wo = await api(request, supToken, 'POST', '/api/v1/work-orders', { faultId, priority: 'P2', note: 'E2E' });
  const workOrderId = wo.json!.id as string;
  expect((await api(request, supToken, 'POST', `/api/v1/work-orders/${workOrderId}/assign`, { assigneeId: (await api(request, techToken, 'GET', '/api/v1/me')).json!.userId, reason: null })).status).toBe(200);
  const tag = ((await api(request, supToken, 'GET', `/api/v1/assets/${assetId}`)).json!.tag as Json).humanCode as string;

  // Phone: steps 10–16 through the single write path.
  const pull = (await api(request, techToken, 'GET', '/sync/v1/pull', undefined, deviceId)).json!;
  const task = (pull.workOrders as Json[]).find((t) => t.id === workOrderId)!;
  const templateId = (task.progress as Json).checklistTemplateId as string;
  const template = ((pull.reference as Json).checklistTemplates as Json[]).find((t) => t.id === templateId)!;
  const led = (pull.custody as Json[]).find((c) => c.sku === 'LED-MOD-50W')!;
  const serial = (led.serials as string[])[0];
  const push = async (type: string, payload: Json) => {
    const result = await phone.push(request, API, techToken, deviceId, type, payload);
    expect(result.result, `${type}: ${JSON.stringify(result)}`).toBe('APPLIED');
  };
  const photo = async (kind: 'BEFORE' | 'AFTER' | 'TEST') => {
    const bytes = await syntheticJpeg(techPage, kind);
    const evidenceId = crypto.randomUUID();
    await push('AddEvidence', { workOrderId, evidenceId, kind, sha256: sha256Hex(bytes), sizeBytes: bytes.length, mime: 'image/jpeg' });
    const up = await request.put(`${API}/sync/v1/evidence/${evidenceId}/content`, {
      headers: { Authorization: `Bearer ${techToken}`, 'X-Device-Id': deviceId, 'Content-Type': 'image/jpeg' }, data: bytes,
    });
    expect(up.status()).toBe(200);
  };
  await push('ArriveOnSite', { workOrderId, scannedToken: tag });
  await photo('BEFORE');
  await photo('BEFORE');
  await push('RecordDiagnosis', { workOrderId, code: 'LED_MODULE_FAIL', note: 'وحدة الليد لا تعمل' });
  await push('StartWork', { workOrderId });
  const movementId = crypto.randomUUID();
  await push('ConsumeMaterial', { workOrderId, movementId, itemId: led.itemId, quantity: 1, serials: [serial] });
  // M5 (W9): the failed module is declared "with part" (the seed installed M-0002 on this light), with its photo.
  const core = await syntheticJpeg(techPage, 'CORE');
  const coreId = crypto.randomUUID();
  await push('DeclareCoreReturn', {
    workOrderId, movementId, outcome: 'WITH_PART', serial: 'M-0002',
    photo: { evidenceId: coreId, sha256: sha256Hex(core), sizeBytes: core.length, mime: 'image/jpeg' },
  });
  expect((await request.put(`${API}/sync/v1/evidence/${coreId}/content`, {
    headers: { Authorization: `Bearer ${techToken}`, 'X-Device-Id': deviceId, 'Content-Type': 'image/jpeg' }, data: core,
  })).status()).toBe(200);
  const answers = Object.fromEntries((template.items as Json[]).map((i) => [i.code, i.expected]));
  await push('RecordChecklist', { workOrderId, checklistId: crypto.randomUUID(), templateId, testMethod: 'NIGHT', notTestedReason: null, answers });
  await photo('TEST');
  await photo('AFTER');
  await photo('AFTER');
  await push('SubmitWorkOrder', { workOrderId, rootCauseNote: 'احتراق وحدة الليد', noMaterialReason: null });

  // 17. Supervisor reviews in the browser: photos, material with its SYP cost, checklist; Approve once every photo is VERIFIED.
  await sup.goto(`/work-orders/${workOrderId}`);
  await expect(sup.getByTestId('wo-state')).toHaveText('بانتظار المراجعة');
  await expect(sup.getByTestId('review-diagnosis')).toContainText('وحدة');
  await expect(sup.getByTestId('material-cost')).toContainText('SYP');
  await expect(sup.getByTestId('checklist')).toBeVisible();
  await expect(sup.getByTestId('wo-approve')).toBeEnabled({ timeout: 30_000 });
  await expect(sup.getByTestId('evidence-thumb')).toHaveCount(5); // 2 before, 1 test, 2 after
  await sup.getByTestId('review-note').fill('تم التحقق من الصور');
  await sup.getByTestId('wo-approve').click();
  await expect(sup.getByTestId('wo-state')).toHaveText('مغلق');
  await expect(sup.getByTestId('seal-hash')).toHaveText(/^[0-9a-f]{64}$/);
  const seal = (await sup.getByTestId('seal-hash').textContent())!;

  // 17a. The asset page shows the recurrent-failure recommendation with the counted faults, threshold and window.
  await sup.goto(`/assets/${assetId}`);
  await expect(sup.getByTestId('operational-status')).toHaveText('يعمل');
  const rec = sup.getByTestId('recommendation');
  await expect(rec).toContainText('عطل متكرر');
  await expect(rec).toContainText('يُنصح بفحص شامل');
  await expect(sup.getByTestId('recommendation-rule')).toContainText('العتبة 3 خلال 180');

  // 18. The dashboard shows the completed work order.
  await sup.getByTestId('nav-dashboard').click();
  await expect(sup.locator(`[data-testid="recently-completed"] tr[data-work-order-id="${workOrderId}"]`)).toBeVisible();

  // 19. The auditor finds every important change, with the seal in the closing event.
  const audContext = await browser.newContext();
  const aud = await audContext.newPage();
  await signIn(aud, 'auditor1');
  await aud.getByTestId('nav-audit').click();
  await aud.getByTestId('audit-entity-type').selectOption('work_order');
  await aud.getByTestId('audit-entity-id').fill(workOrderId);
  await aud.getByTestId('audit-submit').click();
  for (const action of ['work_order.created', 'work_order.arrived_on_site', 'work_order.work_started', 'work_order.material_consumed', 'work_order.submitted', 'work_order.closed']) {
    await expect(aud.locator(`[data-action="${action}"]`).first()).toBeVisible();
  }
  await expect(aud.locator('[data-action="work_order.closed"]')).toContainText(seal);
  await expect(aud.locator('[data-action="work_order.arrived_on_site"] [data-testid="device"]')).toContainText(deviceId.slice(0, 8));

  // Stock: the warehouse sees tech1's custody; the admin sees the rule's threshold and window (data, not code).
  const whContext = await browser.newContext();
  const wh = await whContext.newPage();
  await signIn(wh, 'warehouse1');
  await wh.getByTestId('nav-stock').click();
  await expect(wh.locator('[data-testid="stock-location"][data-kind="WAREHOUSE"]')).toBeVisible();
  await expect(wh.locator('[data-testid="stock-location"][data-kind="CUSTODY"]').first()).toBeVisible();

  const adminContext = await browser.newContext();
  const admin = await adminContext.newPage();
  await signIn(admin, 'admin1');
  await admin.getByTestId('nav-settings').click();
  await expect(admin.getByTestId('rule-threshold')).toHaveValue('3');
  await expect(admin.getByTestId('rule-window')).toHaveValue('180');

  for (const c of [techContext, supContext, audContext, whContext, adminContext]) await c.close();
});
