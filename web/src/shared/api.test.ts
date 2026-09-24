import { describe, expect, it, vi } from 'vitest';
import { fetchMe } from './api';

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

describe('fetchMe', () => {
  it('sends the bearer token and returns the user', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(
      json(200, { userId: 'u1', username: 'registrar1', displayName: 'x', roles: ['REGISTRAR'], rolesSource: 'database' }),
    );
    const result = await fetchMe('token-123', fetchImpl);
    expect(fetchImpl).toHaveBeenCalledWith('/api/v1/me', { headers: { Authorization: 'Bearer token-123' } });
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
