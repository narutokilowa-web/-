import { describe, expect, it } from 'vitest';
import { emptyFaultForm, faultFormErrors, toReportFaultRequest } from './faultForm';

describe('fault form', () => {
  it('requires a category and the power observation', () => {
    expect(faultFormErrors(emptyFaultForm)).toEqual(['category', 'powerAtInspection']);
    expect(faultFormErrors({ ...emptyFaultForm, category: 'LIGHT_OFF', powerAtInspection: 'UNKNOWN' })).toEqual([]);
  });

  it('maps blanks to null and keeps the chosen values', () => {
    const request = toReportFaultRequest('a1', { ...emptyFaultForm, category: 'FLICKERING', powerAtInspection: 'CONFIRMED_UNAVAILABLE', description: '  ' });
    expect(request).toEqual({
      assetId: 'a1',
      category: 'FLICKERING',
      powerAtInspection: 'CONFIRMED_UNAVAILABLE',
      powerInfoSource: null,
      powerNote: null,
      description: null,
    });
  });
});
