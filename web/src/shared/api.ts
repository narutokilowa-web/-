/** Mirrors Mfop.Access.Contracts.CurrentUserDto. */
export interface CurrentUser {
  userId: string;
  username: string;
  displayName: string;
  roles: string[];
  rolesSource: 'database';
}

export interface Problem {
  code: string;
  detail?: string;
  details?: Record<string, string>;
}

export type ApiResult<T> = { ok: true; status: number; data: T } | { ok: false; status: number; problem: Problem };

export type MeResult =
  | { kind: 'ok'; user: CurrentUser }
  | { kind: 'forbidden'; code: string }
  | { kind: 'error'; status: number };

/**
 * Calls the API with the bearer token. Every POST carries a fresh Idempotency-Key (UUID) which the server
 * records as the audit event's command_id.
 */
export async function apiFetch<T>(
  accessToken: string,
  path: string,
  init: { method?: 'GET' | 'POST'; body?: unknown } = {},
  fetchImpl: typeof fetch = fetch,
): Promise<ApiResult<T>> {
  const method = init.method ?? 'GET';
  const headers: Record<string, string> = { Authorization: `Bearer ${accessToken}` };
  if (method === 'POST') {
    headers['Content-Type'] = 'application/json';
    headers['Idempotency-Key'] = crypto.randomUUID();
  }
  const response = await fetchImpl(path, {
    method,
    headers,
    body: method === 'POST' ? JSON.stringify(init.body ?? {}) : undefined,
  });
  const text = await response.text();
  const json: unknown = text ? JSON.parse(text) : null;
  if (response.ok) {
    return { ok: true, status: response.status, data: json as T };
  }
  const problem = (json ?? {}) as Partial<Problem>;
  return { ok: false, status: response.status, problem: { code: problem.code ?? `HTTP_${response.status}`, detail: problem.detail, details: problem.details } };
}

export async function fetchMe(accessToken: string, fetchImpl: typeof fetch = fetch): Promise<MeResult> {
  const result = await apiFetch<CurrentUser>(accessToken, '/api/v1/me', {}, fetchImpl);
  if (result.ok) return { kind: 'ok', user: result.data };
  if (result.status === 403) return { kind: 'forbidden', code: result.problem.code };
  return { kind: 'error', status: result.status };
}
