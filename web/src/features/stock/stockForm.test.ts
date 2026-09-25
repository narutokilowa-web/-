import { describe, expect, it } from 'vitest';
import { buildLines, parseSerials, serialDifference } from './stockForm';
import type { StockItem } from '../../shared/types';

const items: StockItem[] = [
  { id: 'drv', sku: 'LED-DRV-50W', nameAr: 'درايفر', nameEn: 'Driver', unit: 'EA', serialTracked: true, coreReturnRequired: true },
  { id: 'fuse', sku: 'FUSE-10A', nameAr: 'فيوز', nameEn: 'Fuse', unit: 'EA', serialTracked: false, coreReturnRequired: false },
  { id: 'cable', sku: 'CABLE-2.5', nameAr: 'كابل', nameEn: 'Cable', unit: 'M', serialTracked: false, coreReturnRequired: false },
];

describe('stock lines', () => {
  it('parses serials separated by commas, Arabic commas, spaces and new lines', () => {
    expect(parseSerials('D-0001, D-0002\nD-0003، D-0004  ')).toEqual(['D-0001', 'D-0002', 'D-0003', 'D-0004']);
  });

  it('builds lines: serial count gives the quantity, metres may be fractional, units must be whole', () => {
    const built = buildLines([
      { itemId: 'drv', quantity: '', serials: 'D-1 D-2' },
      { itemId: 'cable', quantity: '12,5', serials: '' },
      { itemId: '', quantity: '', serials: '' },
    ], items);
    expect(built).toEqual({ ok: true, lines: [{ itemId: 'drv', quantity: 2, serials: ['D-1', 'D-2'] }, { itemId: 'cable', quantity: 12.5, serials: null }] });
    expect(buildLines([{ itemId: 'fuse', quantity: '1.5', serials: '' }], items)).toEqual({ ok: false, error: 'stockForm.wholeUnits' });
  });

  it('catches typing mistakes before they reach the server', () => {
    expect(buildLines([], items)).toEqual({ ok: false, error: 'stockForm.linesRequired' });
    expect(buildLines([{ itemId: 'fuse', quantity: '1', serials: '' }, { itemId: 'fuse', quantity: '2', serials: '' }], items)).toEqual({ ok: false, error: 'stockForm.duplicateItem' });
    expect(buildLines([{ itemId: 'drv', quantity: '3', serials: 'D-1 D-2' }], items)).toEqual({ ok: false, error: 'stockForm.serialPerUnit' });
    expect(buildLines([{ itemId: 'drv', quantity: '', serials: 'D-1 D-1' }], items)).toEqual({ ok: false, error: 'stockForm.duplicateSerial' });
    expect(buildLines([{ itemId: 'fuse', quantity: '1', serials: 'X' }], items)).toEqual({ ok: false, error: 'stockForm.notSerialTracked' });
    expect(buildLines([{ itemId: 'fuse', quantity: '0', serials: '' }], items)).toEqual({ ok: false, error: 'stockForm.quantityInvalid' });
  });

  it('shows which serials are missing or unexpected', () => {
    expect(serialDifference(['A', 'B'], ['B', 'C'])).toEqual({ missing: ['A'], unexpected: ['C'] });
  });
});
