import { createHash, generateKeyPairSync, randomUUID, sign as cryptoSign } from 'node:crypto';
import { expect, type APIRequestContext, type Page } from '@playwright/test';

export const password = () => {
  const p = process.env.MFOP_DEV_USER_PASSWORD;
  if (!p) throw new Error('MFOP_DEV_USER_PASSWORD must be set (same value as in .env).');
  return p;
};

/** Signs in through Keycloak (Authorization Code + PKCE) from the current page. */
export async function signIn(page: Page, username: string, path = '/') {
  await page.goto(path);
  await page.getByTestId('login').click();
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password());
  await page.locator('#kc-login').click();
  await expect(page.getByTestId('username')).toHaveText(username);
}

/**
 * The field app's device trust (M4) for specs that play the phone through the API: an EC P-256 key (the Keystore key on a
 * real phone) registered with the device, signed POST /sync/v1/push and /sync/v1/grant, the offline grant on every command
 * and the work-order version the phone last saw (baseVersion).
 */
export class E2EPhone {
  private readonly keys = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  grantId: string | null = null;

  readonly publicKey = (this.keys.publicKey.export({ type: 'spki', format: 'der' }) as Buffer).toString('base64');

  signatureHeaders(method: string, path: string, body: string): Record<string, string> {
    const timestamp = String(Date.now());
    const nonce = randomUUID().replaceAll('-', '');
    const bodyHash = createHash('sha256').update(body, 'utf8').digest('hex');
    const message = `${method}\n${path}\n${timestamp}\n${nonce}\n${bodyHash}`;
    const signature = cryptoSign('sha256', Buffer.from(message, 'utf8'), this.keys.privateKey).toString('base64'); // DER for EC keys
    return { 'X-Device-Signature': signature, 'X-Signature-Timestamp': timestamp, 'X-Signature-Nonce': nonce };
  }

  async signedPost(request: APIRequestContext, api: string, token: string, deviceId: string, path: string, body: unknown) {
    const text = JSON.stringify(body);
    const response = await request.post(`${api}${path}`, {
      headers: { Authorization: `Bearer ${token}`, 'X-Device-Id': deviceId, 'Content-Type': 'application/json', ...this.signatureHeaders('POST', path, text) },
      data: text,
    });
    return { status: response.status(), json: (await response.json().catch(() => null)) as Record<string, unknown> | null };
  }

  async grant(request: APIRequestContext, api: string, token: string, deviceId: string): Promise<string> {
    const r = await this.signedPost(request, api, token, deviceId, '/sync/v1/grant', {});
    expect(r.status, `grant: ${JSON.stringify(r.json)}`).toBe(200);
    this.grantId = r.json!.grantId as string;
    return this.grantId;
  }

  /** One command through the single write path, with the grant and (for work-order commands) the version the phone holds. */
  async push(request: APIRequestContext, api: string, token: string, deviceId: string, type: string, payload: Record<string, unknown>) {
    if (!this.grantId) await this.grant(request, api, token, deviceId);
    let baseVersion: number | undefined;
    if (typeof payload.workOrderId === 'string') {
      const task = await request.get(`${api}/sync/v1/work-orders/${payload.workOrderId}`, { headers: { Authorization: `Bearer ${token}`, 'X-Device-Id': deviceId } });
      if (task.ok()) baseVersion = ((await task.json()) as { eventSeq: number }).eventSeq;
    }
    const command = { commandId: randomUUID(), type, payload, deviceTime: new Date().toISOString(), grantId: this.grantId, baseVersion };
    const r = await this.signedPost(request, api, token, deviceId, '/sync/v1/push', { commands: [command], pendingCommands: 0 });
    expect(r.status, `push ${type}: ${JSON.stringify(r.json)}`).toBe(200);
    return (r.json!.results as Record<string, unknown>[])[0]!;
  }
}
