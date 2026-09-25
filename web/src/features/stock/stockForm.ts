import type { StockItem } from '../../shared/types';

/** One line as typed by the warehouse officer: an item, a quantity, and for serial-tracked items one serial per unit. */
export interface LineDraft {
  itemId: string;
  quantity: string;
  serials: string;
}

export interface StockLineBody {
  itemId: string;
  quantity: number;
  serials: string[] | null;
}

/** Serials typed separated by commas, spaces or new lines; blanks dropped; order kept. */
export function parseSerials(text: string): string[] {
  return text.split(/[\s,،]+/).map((s) => s.trim()).filter((s) => s.length > 0);
}

/**
 * Turns the drafts into request lines, or returns the i18n key of the first problem. The server checks everything again
 * (stock, serial location, duplicates); this only catches typing mistakes early.
 */
export function buildLines(drafts: LineDraft[], items: StockItem[]): { ok: true; lines: StockLineBody[] } | { ok: false; error: string } {
  const filled = drafts.filter((d) => d.itemId !== '');
  if (filled.length === 0) return { ok: false, error: 'stockForm.linesRequired' };
  if (new Set(filled.map((d) => d.itemId)).size !== filled.length) return { ok: false, error: 'stockForm.duplicateItem' };
  const lines: StockLineBody[] = [];
  for (const d of filled) {
    const item = items.find((i) => i.id === d.itemId);
    if (!item) return { ok: false, error: 'stockForm.unknownItem' };
    const serials = parseSerials(d.serials);
    const quantity = item.serialTracked && d.quantity.trim() === '' ? serials.length : Number(d.quantity.replace(',', '.'));
    if (!Number.isFinite(quantity) || quantity <= 0) return { ok: false, error: 'stockForm.quantityInvalid' };
    if (item.unit === 'EA' && !Number.isInteger(quantity)) return { ok: false, error: 'stockForm.wholeUnits' };
    if (item.serialTracked) {
      if (serials.length !== quantity) return { ok: false, error: 'stockForm.serialPerUnit' };
      if (new Set(serials).size !== serials.length) return { ok: false, error: 'stockForm.duplicateSerial' };
    } else if (serials.length > 0) {
      return { ok: false, error: 'stockForm.notSerialTracked' };
    }
    lines.push({ itemId: item.id, quantity, serials: item.serialTracked ? serials : null });
  }
  return { ok: true, lines };
}

/** The serials of a document that the officer has not ticked, and ticked ones that are not in it. */
export function serialDifference(expected: string[], counted: string[]): { missing: string[]; unexpected: string[] } {
  return { missing: expected.filter((s) => !counted.includes(s)), unexpected: counted.filter((s) => !expected.includes(s)) };
}
