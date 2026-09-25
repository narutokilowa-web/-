import { randomUUID } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { API, E2EPhone, accessToken, apiCall, signIn, type Json } from './helpers';

// M5 on the real stack (after make seed and the M3 flow): the warehouse issues material in the browser, tech1's phone
// acknowledges it and declares an unused return through the single write path, the warehouse receives the return and the
// failed module the seed left waiting (serial checked), and the reconciliation shows no ledger or in-transit mismatch.
test('issue → phone acknowledgment → unused return → failed part received → reconciliation and history', async ({ browser, request }) => {
  test.setTimeout(180_000);

  // 1. Warehouse: issue 2 fuses to tech1 (they are in transit until acknowledged).
  const whContext = await browser.newContext();
  const wh = await whContext.newPage();
  await signIn(wh, 'warehouse1');
  await wh.getByTestId('home-issues').click();
  const tech1 = wh.getByTestId('issue-technician').locator('option', { hasText: 'tech1' });
  await expect(tech1).toHaveCount(1);
  await wh.getByTestId('issue-technician').selectOption({ value: (await tech1.getAttribute('value'))! });
  const fuse = wh.getByTestId('line-item').locator('option', { hasText: 'FUSE-10A' });
  await wh.getByTestId('line-item').selectOption({ value: (await fuse.getAttribute('value'))! });
  await wh.getByTestId('line-quantity').fill('2');
  await wh.getByTestId('issue-note').fill('E2E M5');
  await wh.getByTestId('issue-submit').click();
  await expect(wh.getByTestId('issue-created')).toContainText('ISS-');
  const code = (await wh.getByTestId('issue-created').textContent())!.match(/ISS-\d+/)![0];
  const row = wh.locator(`tr[data-code="${code}"]`);
  await expect(row.getByTestId('issue-state')).toHaveText('بانتظار تأكيد الاستلام');

  // 2. tech1's phone (registered and approved) acknowledges it.
  const techContext = await browser.newContext();
  const techPage = await techContext.newPage();
  await signIn(techPage, 'tech1');
  const techToken = await accessToken(techPage);
  const phone = new E2EPhone();
  const registered = await apiCall(request, techToken, 'POST', '/sync/v1/devices/register', { platform: 'ANDROID', model: 'E2E Phone M5', osVersion: '14', appVersion: '0.5.0', publicKey: phone.publicKey });
  expect(registered.status).toBe(201);
  const deviceId = registered.json!.deviceId as string;
  const supContext = await browser.newContext();
  const sup = await supContext.newPage();
  await signIn(sup, 'supervisor1');
  expect((await apiCall(request, await accessToken(sup), 'POST', `/api/v1/devices/${deviceId}/approve`, { reason: null })).status).toBe(200);

  const pull = (await apiCall(request, techToken, 'GET', '/sync/v1/pull', undefined, deviceId)).json!;
  const issue = ((pull.stock as Json).issues as Json[]).find((i) => i.code === code)!;
  expect(issue, 'the new issue is on the phone').toBeTruthy();
  const fuseId = ((issue.lines as Json[])[0]).itemId as string;
  const push = async (type: string, payload: Json) => {
    const result = await phone.push(request, API, techToken, deviceId, type, payload);
    expect(result.result, `${type}: ${JSON.stringify(result)}`).toBe('APPLIED');
  };
  await push('AcknowledgeStockIssue', { issueId: issue.id, confirmedSerials: [] });

  // 3. One fuse goes back unused: declared on the phone, received in the browser.
  const returnId = randomUUID();
  await push('DeclareStockReturn', { returnId, lines: [{ itemId: fuseId, quantity: 1 }], note: 'فائض (E2E)' });
  await wh.goto('/stock/returns?state=DECLARED');
  const ret = wh.locator(`tr[data-return-id="${returnId}"]`);
  await expect(ret.getByTestId('return-state')).toHaveText('مُصرَّح به، لم يُستلم بعد');
  await ret.getByTestId('return-receive').click();
  await wh.getByTestId('returns-filter-RECEIVED').click();
  await expect(wh.locator(`tr[data-return-id="${returnId}"]`).getByTestId('return-state')).toHaveText('مُستلم');

  // 4. The failed module the seed declared (M-0001) is received; its serial matches the one known on the light.
  await wh.goto('/stock/core-returns?state=DECLARED_WITH_PART');
  const core = wh.locator('tr[data-core-id]', { hasText: 'M-0001' });
  await expect(core).toHaveCount(1);
  const coreCode = (await core.getAttribute('data-code'))!;
  await core.getByTestId('core-serial').fill('M-0001');
  await core.getByTestId('core-receive').click();
  await wh.getByTestId('cores-filter-RECEIVED').click();
  const received = wh.locator(`tr[data-code="${coreCode}"]`);
  await expect(received.getByTestId('core-state')).toHaveText('مُستلمة');
  await expect(received.getByTestId('core-mismatch')).toHaveCount(0);

  // 5. Reconciliation: ledger, in-transit documents and core bin agree; history shows the failed part's receipt.
  await wh.getByTestId('stock-nav-reconciliation').click();
  await expect(wh.getByTestId('reconciliation-status')).toBeVisible();
  for (const code of ['BALANCE_MISMATCH', 'IN_TRANSIT_MISMATCH', 'CORE_BIN_MISMATCH']) {
    await expect(wh.locator(`[data-testid="reconciliation-finding"][data-code="${code}"]`)).toHaveCount(0);
  }
  await expect(wh.locator('[data-testid="reconciliation-items"] tr[data-sku="FUSE-10A"]')).toBeVisible();
  await wh.getByTestId('stock-nav-history').click();
  await wh.getByTestId('history-serial').fill('M-0001');
  await wh.getByTestId('history-serial').press('Enter');
  await expect(wh.locator('[data-testid="history-movements"] tr[data-type="CORE_RECEIVE"]')).toHaveCount(1);

  // The supervisor reaches the failed-parts queue (waivers of unrecoverable parts).
  await sup.goto('/');
  await sup.getByTestId('home-core-returns').click();
  await expect(sup.getByTestId('cores-filter-DECLARED_UNRECOVERABLE')).toBeVisible();
  for (const c of [whContext, techContext, supContext]) await c.close();
});
