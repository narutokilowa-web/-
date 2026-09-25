import { useTranslation } from 'react-i18next';
import type { StockItem } from '../../shared/types';
import type { LineDraft } from './stockForm';

export const emptyLine = (): LineDraft => ({ itemId: '', quantity: '', serials: '' });

/** Lines of a stock document: item, quantity, serials (one per unit for serial-tracked items). */
export function LinesEditor({ items, lines, onChange }: { items: StockItem[]; lines: LineDraft[]; onChange: (lines: LineDraft[]) => void }) {
  const { t, i18n } = useTranslation();
  const set = (i: number, patch: Partial<LineDraft>) => onChange(lines.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  return (
    <div className="lines" data-testid="lines-editor">
      {lines.map((line, i) => {
        const item = items.find((x) => x.id === line.itemId);
        return (
          <div key={i} className="row-actions" data-testid="line">
            <select value={line.itemId} data-testid="line-item" onChange={(e) => set(i, { itemId: e.target.value })}>
              <option value="">{t('stockForm.chooseItem')}</option>
              {items.map((x) => <option key={x.id} value={x.id}>{x.sku} — {i18n.language === 'ar' ? x.nameAr : x.nameEn}</option>)}
            </select>
            <input inputMode="decimal" placeholder={t('stockForm.quantity')} value={line.quantity} data-testid="line-quantity"
              onChange={(e) => set(i, { quantity: e.target.value })} style={{ width: '6rem' }} />
            {item?.serialTracked && (
              <input placeholder={t('stockForm.serials')} value={line.serials} data-testid="line-serials" onChange={(e) => set(i, { serials: e.target.value })} />
            )}
            {item && <span className="hint">{item.unit}</span>}
            {lines.length > 1 && <button type="button" onClick={() => onChange(lines.filter((_, j) => j !== i))}>{t('stockForm.removeLine')}</button>}
          </div>
        );
      })}
      <button type="button" data-testid="line-add" onClick={() => onChange([...lines, emptyLine()])}>{t('stockForm.addLine')}</button>
    </div>
  );
}

/** A document's lines, read-only. */
export function LinesView({ lines }: { lines: { movementId: string; sku: string; nameAr: string; nameEn: string; unit: string; quantity: number; serials: string[] }[] }) {
  const { i18n } = useTranslation();
  return (
    <ul className="plain" data-testid="document-lines">
      {lines.map((l) => (
        <li key={l.movementId}>
          <code>{l.sku}</code> {i18n.language === 'ar' ? l.nameAr : l.nameEn} × {l.quantity} {l.unit}
          {l.serials.length > 0 && <span className="hint"> · {l.serials.join(', ')}</span>}
        </li>
      ))}
    </ul>
  );
}
