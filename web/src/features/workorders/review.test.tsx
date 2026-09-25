import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import i18n from '../../shared/i18n';
import { formatMoney, formatQuantity, shortHash } from '../../shared/format';
import { groupEvidence, unexpectedAnswers } from './ReviewSection';
import { groupBalances } from '../stock/StockBalancesPage';
import { auditQuery } from '../audit/AuditSearchPage';
import { RecommendationBanner } from '../assets/RecommendationBanner';
import { HistoryTimeline } from '../assets/HistoryTimeline';
import type { Balance, ChecklistTemplate, ChecklistResult, Recommendation, ReviewEvidence } from '../../shared/types';

const photo = (id: string, kind: ReviewEvidence['kind'], announcedAt: string, supersededBy: string | null = null): ReviewEvidence => ({
  id, kind, state: 'VERIFIED', sha256Device: 'a'.repeat(64), sha256Server: 'a'.repeat(64), capturedAt: null, announcedAt, verifiedAt: null,
  hasPreview: true, duplicateOf: null, supersededBy, supersedeReason: null, capturedByName: 'فني 1',
});

describe('M3 web helpers', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('ar');
  });

  it('money always carries its currency, never an assumed one', () => {
    expect(formatMoney({ amount: 350000, currency: 'SYP' }, 'en')).toBe('350,000 SYP');
    expect(formatMoney({ amount: 12.5, currency: 'EUR' }, 'en')).toBe('12.5 EUR');
    expect(formatMoney(null, 'ar')).toBe('—');
    expect(formatQuantity(1)).toBe('1');
    expect(formatQuantity(2.5)).toBe('2.5');
    expect(shortHash('b'.repeat(64), 8)).toBe('bbbbbbbb…');
  });

  it('orders photos before → test → after, superseded last', () => {
    const groups = groupEvidence([
      photo('a1', 'AFTER', '2026-09-24T10:05:00Z'),
      photo('b2', 'BEFORE', '2026-09-24T10:01:00Z', 'b3'),
      photo('b1', 'BEFORE', '2026-09-24T10:02:00Z'),
      photo('t1', 'TEST', '2026-09-24T10:03:00Z'),
    ]);
    expect(groups.map((g) => g.kind)).toEqual(['BEFORE', 'TEST', 'AFTER']);
    expect(groups[0]!.items.map((e) => e.id)).toEqual(['b1', 'b2']);
  });

  it('highlights checklist answers that differ from the template', () => {
    const template = {
      id: 't', code: 'X', version: 1, powerSources: ['GRID'], testMethods: [], evidenceRequirements: { beforeFirstArrival: 2, beforeLaterArrival: 1, after: 2, test: 1 },
      items: [
        { code: 'LAMP_ON', textAr: '', textEn: '', expected: 'YES', blocking: true, testItem: true },
        { code: 'EXPOSED_WIRES', textAr: '', textEn: '', expected: 'NO', blocking: true, testItem: false },
      ],
    } satisfies ChecklistTemplate;
    const checklist = { answers: { LAMP_ON: 'YES', EXPOSED_WIRES: 'YES' } } as unknown as ChecklistResult;
    expect(unexpectedAnswers(template, checklist)).toEqual(['EXPOSED_WIRES']);
    expect(unexpectedAnswers(null, checklist)).toEqual([]);
  });

  it('groups balances: warehouse first, then each custody', () => {
    const b = (locationId: string, locationKind: Balance['locationKind'], name: string, custodianName: string | null): Balance => ({
      locationId, locationKind, locationName: name, custodianUserId: null, custodianName, itemId: `${locationId}-i`, sku: 'LED', itemNameAr: 'ليد', itemNameEn: 'LED', unit: 'EA', quantity: 2,
    });
    const groups = groupBalances([b('c1', 'CUSTODY', 'c', 'فني 1'), b('w', 'WAREHOUSE', 'المستودع', null), b('x', 'CONSUMED', 'x', null)]);
    expect(groups.map((g) => g.kind)).toEqual(['WAREHOUSE', 'CUSTODY']);
    expect(groups[1]!.name).toBe('فني 1');
  });

  it('builds the audit query from the form', () => {
    expect(auditQuery('work_order', ' abc ', '')).toBe('entityType=work_order&entityId=abc&take=200');
    expect(auditQuery('', '', 'work_order.closed')).toBe('action=work_order.closed&take=200');
  });

  it('shows the recurrent-failure recommendation with the counted faults, threshold and window', () => {
    const rec: Recommendation = {
      id: 'r', assetId: 'a', code: 'FULL_INSPECTION', ruleCode: 'REPEATED_FAILURE', state: 'OPEN', count: 3, threshold: 3, windowDays: 180,
      countedFailures: [1, 2, 3].map((n) => ({ faultId: `f${n}`, workOrderId: `w${n}`, workOrderCode: `WO-2026-00000${n}`, diagnosisCode: 'LED_MODULE_FAIL', resolvedAt: '2026-09-24T10:00:00Z' })),
      createdAt: '2026-09-24T10:00:00Z', updatedAt: '2026-09-24T10:00:00Z',
    };
    render(<MemoryRouter><RecommendationBanner recommendations={[rec]} /></MemoryRouter>);
    const banner = screen.getByTestId('recommendation');
    expect(banner).toHaveTextContent('عطل متكرر');
    expect(banner).toHaveTextContent('يُنصح بفحص شامل');
    expect(screen.getByTestId('recommendation-rule')).toHaveTextContent('العتبة 3 خلال 180');
    expect(screen.getAllByRole('link')).toHaveLength(3);
  });

  it('timeline shows the device of phone commands', () => {
    render(<HistoryTimeline entries={[{
      seq: 9, occurredAt: '2026-09-24T10:00:00Z', actorUserId: 'u', actorName: 'فني 1', actorRoles: ['TECHNICIAN'], action: 'work_order.arrived_on_site',
      entityType: 'work_order', entityId: 'w', changes: {}, facts: null, reason: null, hash: 'c'.repeat(64), deviceId: '0197c000-aaaa-7000-8000-000000000001',
    }]} />);
    expect(screen.getByTestId('device')).toHaveTextContent('0197c000');
    expect(screen.getByText('الوصول إلى الموقع')).toBeInTheDocument();
  });
});
