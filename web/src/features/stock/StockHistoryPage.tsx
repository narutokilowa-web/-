import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { StockHistory, StockItem } from '../../shared/types';
import { formatDateTime, formatQuantity } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { StockNav } from './StockNav';

const TYPES = ['', 'RECEIPT', 'ISSUE', 'ACKNOWLEDGE', 'CONSUME', 'RETURN_DECLARE', 'RETURN_RECEIVE', 'CORE_RECEIVE', 'REVERSAL'] as const;

/** Stock history (M5): the append-only ledger by item, serial, technician custody or type; a serial also shows where that unit is now. */
export function StockHistoryPage() {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const [serial, setSerial] = useState(params.get('serial') ?? '');
  const items = useResource<StockItem[]>('/api/v1/stock/items');
  const query = new URLSearchParams();
  for (const k of ['itemId', 'serial', 'technicianId', 'type']) {
    const v = params.get(k);
    if (v) query.set(k, v);
  }
  const { result } = useResource<StockHistory>(`/api/v1/stock/movements?${query.toString()}`);
  const set = (k: string, v: string) => {
    const next = new URLSearchParams(params);
    if (v) next.set(k, v);
    else next.delete(k);
    setParams(next);
  };

  return (
    <section data-testid="stock-history">
      <h1>{t('history.title')}</h1>
      <StockNav />
      <p className="hint">{t('history.hint')}</p>
      <div className="filters">
        <select value={params.get('itemId') ?? ''} data-testid="history-item" onChange={(e) => set('itemId', e.target.value)}>
          <option value="">{t('history.anyItem')}</option>
          {items.result?.ok && items.result.data.map((x) => <option key={x.id} value={x.id}>{x.sku}</option>)}
        </select>
        <input placeholder={t('history.serial')} value={serial} data-testid="history-serial" onChange={(e) => setSerial(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter') set('serial', serial.trim()); }} />
        <button type="button" onClick={() => set('serial', serial.trim())}>{t('common.search')}</button>
        <select value={params.get('type') ?? ''} data-testid="history-type" onChange={(e) => set('type', e.target.value)}>
          {TYPES.map((x) => <option key={x || 'all'} value={x}>{x ? t(`movementType.${x}`) : t('history.anyType')}</option>)}
        </select>
        {params.get('technicianId') && <button type="button" onClick={() => set('technicianId', '')}>{t('history.clearTechnician')}</button>}
      </div>
      {!result ? <p>{t('auth.loading')}</p> : !result.ok ? <ProblemView problem={result.problem} /> : (
        <>
          {result.data.serial && (
            <div className="panel" data-testid="history-serial-status">
              <strong>{result.data.serial.sku} · {result.data.serial.serial}</strong>
              <span>{t('history.where', { where: result.data.serial.locationKind ? t(`locationKind.${result.data.serial.locationKind}`) : '—' })}</span>
              {result.data.serial.installedAssetId && (
                <span>{t('history.installedOn')} <Link to={`/assets/${result.data.serial.installedAssetId}`}>{result.data.serial.installedAssetId.slice(0, 8)}</Link> · {formatDateTime(result.data.serial.installedAt, i18n.language)}</span>
              )}
              {result.data.serial.removedFromAssetId && (
                <span>{t('history.removedFrom')} <Link to={`/assets/${result.data.serial.removedFromAssetId}`}>{result.data.serial.removedFromAssetId.slice(0, 8)}</Link>
                  {result.data.serial.historical && <span className="hint"> ({t('history.historical')})</span>}</span>
              )}
            </div>
          )}
          {result.data.truncated && <p className="hint">{t('history.truncated')}</p>}
          {result.data.movements.length === 0 ? <p>{t('common.empty')}</p> : (
            <table className="table" data-testid="history-movements">
              <thead><tr><th>{t('history.when')}</th><th>{t('history.type')}</th><th>{t('stock.item')}</th><th>{t('history.fromTo')}</th><th>{t('history.by')}</th><th>{t('history.document')}</th></tr></thead>
              <tbody>
                {result.data.movements.map((m) => (
                  <tr key={m.id} data-type={m.type}>
                    <td className="hint">{formatDateTime(m.occurredAt, i18n.language)}</td>
                    <td>{t(`movementType.${m.type}`, { defaultValue: m.type })}{m.exceedsCustody && <span className="badge warn">{t('history.exceeds')}</span>}</td>
                    <td><code>{m.sku}</code> × {formatQuantity(m.quantity)} {m.unit}{m.serials.length > 0 && <div className="hint">{m.serials.join(', ')}</div>}</td>
                    <td>{t(`locationKind.${m.fromKind}`)} → {t(`locationKind.${m.toKind}`)}<div className="hint">{m.fromName} → {m.toName}</div></td>
                    <td>{m.actorName}</td>
                    <td>
                      {m.documentCode ?? '—'}
                      {m.workOrderId && <div><Link to={`/work-orders/${m.workOrderId}`}>{t('history.workOrder')}</Link></div>}
                      {m.note && <div className="hint">{m.note}</div>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </section>
  );
}
