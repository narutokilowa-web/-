/** Mirrors Mfop.Access.Contracts.CurrentUserDto. */
export interface CurrentUser {
  userId: string;
  username: string;
  displayName: string;
  roles: string[];
  rolesSource: 'database';
}

export type MeResult =
  | { kind: 'ok'; user: CurrentUser }
  | { kind: 'forbidden'; code: string }
  | { kind: 'error'; status: number };

export async function fetchMe(accessToken: string, fetchImpl: typeof fetch = fetch): Promise<MeResult> {
  const response = await fetchImpl('/api/v1/me', {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  if (response.ok) {
    return { kind: 'ok', user: (await response.json()) as CurrentUser };
  }
  if (response.status === 403) {
    const problem = (await response.json().catch(() => ({}))) as { code?: string };
    return { kind: 'forbidden', code: problem.code ?? 'FORBIDDEN' };
  }
  return { kind: 'error', status: response.status };
}
