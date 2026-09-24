import { describe, expect, it } from 'vitest';
import ar from './ar.json';
import en from './en.json';

function keys(obj: object, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) =>
    typeof v === 'object' && v !== null ? keys(v as object, `${prefix}${k}.`) : [`${prefix}${k}`],
  );
}

describe('locales', () => {
  it('Arabic and English have exactly the same keys', () => {
    expect(keys(ar).sort()).toEqual(keys(en).sort());
  });

  it('never claim immutability (ADR-0007)', () => {
    const text = JSON.stringify([ar, en]).toLowerCase();
    expect(text).not.toContain('immutable');
    expect(text).not.toContain('غير قابل للتعديل');
  });
});
