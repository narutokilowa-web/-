import { render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import i18n from '../../shared/i18n';
import { HistoryTimeline } from './HistoryTimeline';
import type { HistoryEntry } from '../../shared/types';

const entry = (seq: number, action: string, changes: HistoryEntry['changes'], reason: string | null = null): HistoryEntry => ({
  seq, occurredAt: '2026-09-24T10:00:00.000000Z', actorUserId: 'u', actorName: 'معتمد تجريبي', actorRoles: ['APPROVER'],
  action, entityType: 'street_light', entityId: 'x', changes, facts: null, reason, hash: 'a'.repeat(64),
});

describe('HistoryTimeline', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('ar');
  });

  it('shows newest first with actor, reason and old → new values', () => {
    render(
      <HistoryTimeline
        entries={[
          entry(1, 'registration.drafted', {}),
          entry(5, 'street_light.activated', { lifecycle_state: { old: 'PENDING', new: 'ACTIVE' } }, 'موافق'),
        ]}
      />,
    );
    const items = within(screen.getByTestId('history')).getAllByRole('listitem');
    expect(items[0]).toHaveAttribute('data-action', 'street_light.activated');
    expect(items[0]).toHaveTextContent('تفعيل العمود');
    expect(items[0]).toHaveTextContent('معتمد تجريبي');
    expect(items[0]).toHaveTextContent('موافق');
    const row = items[0]!.querySelector('[data-field="lifecycle_state"]')!;
    expect(row.querySelector('.old')).toHaveTextContent('PENDING');
    expect(row.querySelector('.new')).toHaveTextContent('ACTIVE');
    expect(items[1]).toHaveAttribute('data-action', 'registration.drafted');
  });
});
