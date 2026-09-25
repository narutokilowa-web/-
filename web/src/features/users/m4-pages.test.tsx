import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../shared/i18n';
import { SessionContext } from '../../app/MeContext';
import { UsersPage } from './UsersPage';
import { QuarantinePage } from '../quarantine/QuarantinePage';
import type { QuarantineEntry, UserAccount } from '../../shared/types';

const ADMIN_ID = 'a0000000-0000-0000-0000-000000000007';
const SUP_ID = 's0000000-0000-0000-0000-000000000003';
const TECH_ID = 't0000000-0000-0000-0000-000000000004';

function session(userId: string, roles: string[]) {
  return { token: 't', me: { userId, username: 'u', displayName: 'U', roles, rolesSource: 'database' as const } };
}

function mockFetch(routes: Record<string, unknown>) {
  const calls: { url: string; method: string; body: unknown }[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, method: init?.method ?? 'GET', body: init?.body ? JSON.parse(init.body as string) : null });
    const key = Object.keys(routes).find((k) => url.startsWith(k));
    return new Response(JSON.stringify(key ? routes[key] : {}), { status: 200, headers: { 'Content-Type': 'application/json' } });
  }));
  return calls;
}

const user = (id: string, status: UserAccount['status'], extra: Partial<UserAccount> = {}): UserAccount => ({
  id, username: id.slice(0, 5), displayName: `User ${id.slice(0, 1)}`, status, roles: ['TECHNICIAN'], disabledAt: null, disabledByName: null, disableReason: null,
  devices: [], idpPending: 0, idpAlert: false, idpLastError: null, ...extra,
});

describe('M4 web screens', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en');
  });
  afterEach(() => vi.unstubAllGlobals());

  it('offboarding needs an explicit confirmation, sends the reason, and the admin cannot disable themselves', async () => {
    const calls = mockFetch({
      '/api/v1/users': [user(TECH_ID, 'ACTIVE'), user(ADMIN_ID, 'ACTIVE'), user('d0000000', 'DISABLED', { idpPending: 2, idpAlert: true, disabledAt: '2026-09-25T10:00:00Z', disabledByName: 'Admin', disableReason: 'left' })],
    });
    render(<SessionContext.Provider value={session(ADMIN_ID, ['ADMIN'])}><MemoryRouter><UsersPage /></MemoryRouter></SessionContext.Provider>);
    const tech = await screen.findByText((_, el) => el?.getAttribute('data-user-id') === TECH_ID);
    expect(within(screen.getByText((_, el) => el?.getAttribute('data-user-id') === ADMIN_ID)).queryByTestId('user-disable')).toBeNull();
    expect(screen.getByTestId('user-idp')).toHaveTextContent('already blocked');

    fireEvent.change(within(tech).getByTestId('user-reason'), { target: { value: 'End of contract' } });
    fireEvent.click(within(tech).getByTestId('user-disable'));
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(0);
    fireEvent.click(within(tech).getByTestId('user-disable-confirm'));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true));
    const post = calls.find((c) => c.method === 'POST')!;
    expect(post.url).toBe(`/api/v1/users/${TECH_ID}/disable`);
    expect(post.body).toEqual({ reason: 'End of contract' });
    await waitFor(() => expect(calls.filter((c) => c.url === '/api/v1/users')).toHaveLength(2)); // reloaded after the decision
  });

  const entry = (id: string, userId: string): QuarantineEntry => ({
    id, commandId: `c-${id}`, type: 'RecordDiagnosis', userId, userName: 'Tech', deviceId: 'dddddddd-0000', reasonCode: 'GRANT_WINDOW_UNPROVEN', reason: 'x',
    deviceTime: '2026-09-25T08:00:00Z', estimatedTime: '2026-09-25T08:00:00Z', timeConfidence: 'LOW', receivedAt: '2026-09-26T10:00:00Z', grantId: null,
    grantExpiresAt: null, payload: { code: 'DRIVER_FAIL' }, state: 'PENDING', resolvedByName: null, resolvedAt: null, resolutionReason: null, resultCode: null,
  });

  it('a supervisor decides held commands of others with a reason; their own and an auditor see no decision buttons', async () => {
    const calls = mockFetch({ '/api/v1/sync/quarantine': [entry('q1', TECH_ID), entry('q2', SUP_ID)] });
    const { unmount } = render(<SessionContext.Provider value={session(SUP_ID, ['SUPERVISOR'])}><MemoryRouter><QuarantinePage /></MemoryRouter></SessionContext.Provider>);
    await screen.findAllByTestId('quarantine-state');
    expect(screen.getAllByTestId('quarantine-reason')[0]).toHaveTextContent('cannot be proven');
    expect(screen.getAllByTestId('quarantine-apply')).toHaveLength(1);
    fireEvent.change(screen.getByTestId('quarantine-decision-reason'), { target: { value: 'Confirmed by phone' } });
    fireEvent.click(screen.getByTestId('quarantine-discard'));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true));
    expect(calls.find((c) => c.method === 'POST')).toMatchObject({ url: '/api/v1/sync/quarantine/q1/resolve', body: { decision: 'DISCARD', reason: 'Confirmed by phone' } });
    await waitFor(() => expect(calls.filter((c) => c.method === 'GET')).toHaveLength(2)); // reloaded after the decision
    unmount();

    mockFetch({ '/api/v1/sync/quarantine': [entry('q1', TECH_ID)] });
    render(<SessionContext.Provider value={session('aud', ['AUDITOR'])}><MemoryRouter><QuarantinePage /></MemoryRouter></SessionContext.Provider>);
    await screen.findAllByTestId('quarantine-state');
    expect(screen.queryByTestId('quarantine-apply')).toBeNull();
  });
});
