import { describe, expect, it, vi } from 'vitest';
import { apiFetch, fetchMe } from './api';

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

describe('fetchMe', () => {
  it('sends the bearer token and returns the user', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(
      json(200, { userId: 'u1', username: 'registrar1', displayName: 'x', roles: ['REGISTRAR'], rolesSource: 'database' }),
    );
    const result = await fetchMe('token-123', fetchImpl);
    expect(fetchImpl).toHaveBeenCalledWith('/api/v1/me', expect.objectContaining({ method: 'GET', headers: { Authorization: 'Bearer token-123' } }));
    expect(result).toEqual({ kind: 'ok', user: expect.objectContaining({ roles: ['REGISTRAR'] }) });
  });

  it('maps 403 problem codes', async () => {
    const result = await fetchMe('t', vi.fn().mockResolvedValue(json(403, { code: 'USER_DISABLED' })));
    expect(result).toEqual({ kind: 'forbidden', code: 'USER_DISABLED' });
  });

  it('maps other failures to an error with status', async () => {
    const result = await fetchMe('t', vi.fn().mockResolvedValue(json(500, {})));
    expect(result).toEqual({ kind: 'error', status: 500 });
  });
});

describe('apiFetch', () => {
  it('sends a fresh UUID Idempotency-Key with every POST (becomes the audit command_id)', async () => {
    const fetchImpl = vi.fn().mockImplementation(() => Promise.resolve(json(200, { id: 'x' })));
    await apiFetch('t', '/api/v1/registrations', { method: 'POST', body: { a: 1 } }, fetchImpl);
    await apiFetch('t', '/api/v1/registrations', { method: 'POST', body: { a: 1 } }, fetchImpl);
    const keys = fetchImpl.mock.calls.map((c) => (c[1] as RequestInit & { headers: Record<string, string> }).headers['Idempotency-Key']);
    expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/);
    expect(keys[0]).not.toBe(keys[1]);
  });

  it('returns problem codes on failure', async () => {
    const result = await apiFetch('t', '/x', { method: 'POST' }, vi.fn().mockResolvedValue(json(409, { code: 'INVALID_TRANSITION' })));
    expect(result).toEqual({ ok: false, status: 409, problem: { code: 'INVALID_TRANSITION', detail: undefined, details: undefined } });
  });
});
