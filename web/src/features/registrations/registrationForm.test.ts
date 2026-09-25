import { describe, expect, it } from 'vitest';
import { emptyForm, fromRegistration, syntheticForm, toRequest } from './registrationForm';
import type { Registration } from '../../shared/types';

describe('registration form mapping', () => {
  it('converts strings to typed request values and blanks to null', () => {
    const request = toRequest({ ...emptyForm(), latitude: '33.5138', longitude: '36.2765', poleHeightM: '8.5', addressText: '  ' });
    expect(request.attributes.latitude).toBe(33.5138);
    expect(request.attributes.poleHeightM).toBe(8.5);
    expect(request.attributes.addressText).toBeNull();
    expect(request.attributes.installedOn).toBeNull();
  });

  it('turns a YEAR precision into January 1st of that year', () => {
    const request = toRequest({ ...syntheticForm(1), installedOnPrecision: 'YEAR', installedOn: '2019' });
    expect(request.attributes.installedOn).toBe('2019-01-01');
  });

  it('drops solar fields for grid-powered lights', () => {
    const request = toRequest({ ...syntheticForm(1), powerSource: 'GRID', solarPanelW: '100' });
    expect(request.attributes.solarPanelW).toBeNull();
    const solar = toRequest({ ...syntheticForm(1), powerSource: 'SOLAR', solarPanelW: '100' });
    expect(solar.attributes.solarPanelW).toBe(100);
  });

  it('round-trips an existing registration', () => {
    const form = { ...syntheticForm(7), installedOnPrecision: 'YEAR' as const, installedOn: '2019' };
    const request = toRequest(form);
    const registration = { reason: request.reason, projectRef: null, attributes: request.attributes } as unknown as Registration;
    expect(toRequest(fromRegistration(registration))).toEqual(request);
  });
});
