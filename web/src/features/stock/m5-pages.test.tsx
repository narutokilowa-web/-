import type { ReactElement } from 'react';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../shared/i18n';
import { SessionContext } from '../../app/MeContext';
import { IssuesPage } from './IssuesPage';
import { ReturnsPage } from './ReturnsPage';
import { CoreReturnsPage } from './CoreReturnsPage';
import { ReconciliationPage } from './ReconciliationPage';
import type { CoreReturn, Reconciliation, StockIssue, StockItem, StockReturn } from '../../shared/types';

const WH = 'w0000000-0000-0000-0000-000000000006';
const SUP = 's0000000-0000-0000-0000-000000000003';
const TECH = 't0000000-0000-0000-0000-000000000004';

function session(userId: string, roles: string[]) {
  return { token: 't', me: { userId, username: 'u', displayName: 'U', roles, rolesSource: 'database' as const } };
}

/** Routes are matched by prefix in the given order (put the more specific first). */
function mockFetch(routes: [string, unknown][]) {
  const calls: { url: string; method: string; body: unknown }[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, method: init?.method ?? 'GET', body: init?.body ? JSON.parse(init.body as string) : null });
    const hit = routes.find(([k]) => url.startsWith(k));
    return new Response(JSON.stringify(hit ? hit[1] : {}), { status: 200, headers: { 'Content-Type': 'application/json' } });
  }));
  return calls;
}

const posts = (calls: { method: string }[]) => calls.filter((c) => c.method === 'POST');

const renderAs = (userId: string, roles: string[], page: ReactElement) =>
  render(<SessionContext.Provider value={session(userId, roles)}><MemoryRouter>{page}</MemoryRouter></SessionContext.Provider>);

const items: StockItem[] = [
  { id: 'drv', sku: 'LED-DRV-50W', nameAr: 'درايفر', nameEn: 'Driver', unit: 'EA', serialTracked: true, coreReturnRequired: true },
  { id: 'fuse', sku: 'FUSE-10A', nameAr: 'فيوز', nameEn: 'Fuse', unit: 'EA', serialTracked: false, coreReturnRequired: false },
];

const line = (serials: string[], sku = 'LED-DRV-50W') => ({ movementId: `m-${sku}`, itemId: 'drv', sku, nameAr: 'x', nameEn: 'x', unit: 'EA', serialTracked: serials.length > 0, quantity: serials.length || 2, serials });

const issue = (id: string, state: StockIssue['state'], extra: Partial<StockIssue> = {}): StockIssue => ({
  id, code: `ISS-${id}`, state, technicianId: TECH, technicianName: 'Tech One', issuedBy: WH, issuedByName: 'Warehouse', issuedAt: '2026-09-25T08:00:00Z', note: null,
  referenceWorkOrderId: null, requestedBy: null, respondedAt: null, disputeReason: null, disputeEvidenceId: null, cancelledAt: null, cancelledByName: null,
  cancelReason: null, overdue: false, lines: [line(['D-1', 'D-2'])], flags: [], ...extra,
});

describe('M5 warehouse screens', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en');
  });
  afterEach(async () => {
    await new Promise((r) => setTimeout(r, 0)); // let reloads started by the last decision settle
    cleanup();
    vi.unstubAllGlobals();
  });

  it('issues stock to a technician with one serial per unit, and cancels a disputed issue with a reason', async () => {
    const calls = mockFetch([
      ['/api/v1/stock/items', items],
      ['/api/v1/stock/technicians', [{ userId: TECH, displayName: 'Tech One', username: 'tech1', hasActiveDevice: true }]],
      ['/api/v1/stock/issues?', [issue('1', 'DISPUTED', { disputeReason: 'Only one arrived', flags: [{ id: 'f', ruleCode: 'STOCK_ISSUE_DISPUTED', severity: 'WARNING', state: 'OPEN', createdAt: '' }] })]],
      ['/api/v1/stock/issues', { issueId: 'new', code: 'ISS-000009', movementIds: [] }],
    ]);
    renderAs(WH, ['WAREHOUSE'], <IssuesPage />);
    await screen.findByText('ISS-1');
    expect(screen.getByTestId('issue-dispute-reason')).toHaveTextContent('Only one arrived');
    expect(screen.getByTestId('document-flag')).toHaveTextContent('disputed');

    // A typing mistake is caught before anything is sent.
    fireEvent.click(screen.getByTestId('issue-submit'));
    expect(screen.getByTestId('form-error')).toHaveTextContent('receiving technician');
    await waitFor(() => expect(screen.getByTestId('issue-technician').querySelectorAll('option')).toHaveLength(2));
    fireEvent.change(screen.getByTestId('issue-technician'), { target: { value: TECH } });
    await waitFor(() => expect(screen.getByTestId('line-item').querySelectorAll('option')).toHaveLength(3));
    fireEvent.change(screen.getByTestId('line-item'), { target: { value: 'drv' } });
    fireEvent.change(screen.getByTestId('line-quantity'), { target: { value: '3' } });
    fireEvent.change(screen.getByTestId('line-serials'), { target: { value: 'D-7, D-8' } });
    fireEvent.click(screen.getByTestId('issue-submit'));
    expect(screen.getByTestId('form-error')).toHaveTextContent('one serial per unit');
    expect(posts(calls)).toHaveLength(0);

    fireEvent.change(screen.getByTestId('line-quantity'), { target: { value: '' } });
    fireEvent.click(screen.getByTestId('issue-submit'));
    await screen.findByTestId('issue-created');
    expect(posts(calls)[0]).toMatchObject({ url: '/api/v1/stock/issues', body: { technicianId: TECH, lines: [{ itemId: 'drv', quantity: 2, serials: ['D-7', 'D-8'] }] } });

    fireEvent.change(screen.getByTestId('issue-cancel-reason'), { target: { value: 'Re-issue the right quantity' } });
    fireEvent.click(screen.getByTestId('issue-cancel'));
    await waitFor(() => expect(posts(calls)).toHaveLength(2));
    expect(posts(calls)[1]).toMatchObject({ url: '/api/v1/stock/issues/1/cancel', body: { reason: 'Re-issue the right quantity' } });
  });

  const ret = (id: string, extra: Partial<StockReturn> = {}): StockReturn => ({
    id, code: `RET-${id}`, kind: 'UNUSED', state: 'DECLARED', technicianId: TECH, technicianName: 'Tech One', declaredBy: TECH, declaredByName: 'Tech One',
    declaredAt: '2026-09-25T08:00:00Z', note: null, decidedAt: null, decidedByName: null, rejectReason: null, lines: [line(['D-1', 'D-2'])], flags: [], ...extra,
  });

  it('receives a return only when every serial is ticked, and rejects with a reason', async () => {
    const calls = mockFetch([
      ['/api/v1/stock/returns', [ret('r1')]],
      ['/api/v1/stock/reconciliation', { custodians: [], items: [], findings: [], open: {}, balanced: true }],
      ['/api/v1/stock/items', items],
    ]);
    renderAs(WH, ['WAREHOUSE'], <ReturnsPage />);
    const row = await screen.findByText((_, el) => el?.getAttribute('data-return-id') === 'r1');
    const receive = within(row).getByTestId('return-receive');
    expect(receive).toBeDisabled();
    const [first, second] = within(row).getAllByTestId('return-serial');
    fireEvent.click(first!);
    expect(receive).toBeDisabled();
    fireEvent.click(second!);
    expect(receive).toBeEnabled();
    fireEvent.click(receive);
    await waitFor(() => expect(posts(calls)).toHaveLength(1));
    expect(posts(calls)[0]).toMatchObject({ url: '/api/v1/stock/returns/r1/receive', body: { confirmedSerials: ['D-1', 'D-2'] } });

    fireEvent.change(within(row).getByTestId('return-reject-reason'), { target: { value: 'Broken parts' } });
    fireEvent.click(within(row).getByTestId('return-reject'));
    await waitFor(() => expect(posts(calls)).toHaveLength(2));
    expect(posts(calls)[1]).toMatchObject({ url: '/api/v1/stock/returns/r1/reject', body: { reason: 'Broken parts' } });
    expect(screen.getByTestId('offboarding-return')).toHaveTextContent('No disabled user holds material');
  });

  const core = (id: string, state: CoreReturn['state'], technicianId = TECH, extra: Partial<CoreReturn> = {}): CoreReturn => ({
    id, code: `CORE-${id}`, state, consumeMovementId: 'm', workOrderId: 'wo', workOrderCode: 'WO-2026-000001', assetId: 'a', assetCode: 'SL-000001', itemId: 'drv',
    sku: 'LED-DRV-50W', nameAr: 'x', nameEn: 'Driver', serialTracked: true, quantity: 1, technicianId, technicianName: 'Tech One', installedSerial: 'D-9',
    expectedSerial: 'D-1', createdAt: '2026-09-25T08:00:00Z', declaredAt: '2026-09-25T09:00:00Z', declaredSerial: 'D-1', declaredReason: null, evidenceId: null,
    receivedAt: null, receivedByName: null, receivedSerial: null, serialMismatch: false, notPresentedReason: null, waivedAt: null, waivedByName: null,
    waiveReason: null, overdue: false, flags: [], ...extra,
  });

  it('the warehouse receives a failed part with the serial read on it; only another supervisor sees the waiver', async () => {
    const calls = mockFetch([['/api/v1/stock/core-returns', [core('c1', 'DECLARED_WITH_PART', TECH, { overdue: true })]]]);
    const { unmount } = renderAs(WH, ['WAREHOUSE'], <CoreReturnsPage />);
    const row = await screen.findByText((_, el) => el?.getAttribute('data-core-id') === 'c1');
    expect(row).toHaveTextContent('Known on the light: D-1');
    fireEvent.change(within(row).getByTestId('core-serial'), { target: { value: ' D-1 ' } });
    fireEvent.click(within(row).getByTestId('core-receive'));
    await waitFor(() => expect(posts(calls)).toHaveLength(1));
    expect(posts(calls)[0]).toMatchObject({ url: '/api/v1/stock/core-returns/c1/receive', body: { outcome: 'RECEIVED', serial: 'D-1' } });
    unmount();

    const supCalls = mockFetch([['/api/v1/stock/core-returns', [core('c2', 'DECLARED_UNRECOVERABLE', TECH, { declaredReason: 'Burnt' }), core('c3', 'DECLARED_UNRECOVERABLE', SUP)]]]);
    renderAs(SUP, ['SUPERVISOR'], <CoreReturnsPage />);
    await screen.findByText('CORE-c2');
    expect(screen.getAllByTestId('core-waive')).toHaveLength(1); // not on the one where the supervisor is the technician concerned
    fireEvent.change(screen.getByTestId('core-waive-reason'), { target: { value: 'Stolen on site' } });
    fireEvent.click(screen.getByTestId('core-waive'));
    await waitFor(() => expect(posts(supCalls)).toHaveLength(1));
    expect(posts(supCalls)[0]).toMatchObject({ url: '/api/v1/stock/core-returns/c2/waive', body: { reason: 'Stolen on site' } });
    expect(screen.queryByTestId('core-receive')).toBeNull();
  });

  it('reconciliation shows what is where, the findings and custody left with a disabled user', async () => {
    const report: Reconciliation = {
      generatedAt: '2026-09-25T10:00:00Z', balanced: false, movementsChecked: 42,
      items: [{ itemId: 'drv', sku: 'LED-DRV-50W', nameAr: 'x', nameEn: 'Driver', unit: 'EA', receivedNet: 10, warehouse: 7, inTransit: 1, custody: 1, consumed: 1, coreBin: 1, coresExpected: 1, inTransitOpenDocuments: 1 }],
      custodians: [{ userId: TECH, name: 'Former Tech', active: false, items: [{ itemId: 'drv', sku: 'LED-DRV-50W', nameAr: 'x', nameEn: 'Driver', unit: 'EA', serialTracked: true, quantity: 1, serials: ['D-5'] }], openIssues: 0, openReturns: 0, openCoreReturns: 1 }],
      open: { issuesAwaitingAcknowledgment: 1, issuesOverdue: 0, issuesDisputed: 0, returnsDeclared: 0, coreReturnsOpen: 0, coreReturnsDeclaredWithPart: 1, coreReturnsOverdue: 1, coreReturnsUnrecoverable: 0 },
      findings: [{ code: 'CUSTODY_OF_DISABLED_USER', severity: 'WARNING', message: 'Former Tech is disabled and still holds LED-DRV-50W:1', itemId: null, locationId: null, userId: TECH }],
    };
    mockFetch([['/api/v1/stock/reconciliation', report]]);
    renderAs(WH, ['WAREHOUSE'], <ReconciliationPage />);
    expect(await screen.findByTestId('reconciliation-status')).toHaveTextContent('Findings to review');
    expect(screen.getByTestId('reconciliation-finding')).toHaveTextContent('Custody left with a disabled user');
    expect(screen.getByTestId('reconciliation-open')).toHaveTextContent('1 failed part(s) to receive (1 overdue)');
    const custodian = screen.getByText((_, el) => el?.getAttribute('data-user-id') === TECH);
    expect(custodian).toHaveTextContent('Disabled');
    expect(custodian).toHaveTextContent('LED-DRV-50W 1');
  });
});
