import { useTranslation } from 'react-i18next';
import { useResource } from '../../shared/useApi';
import type { Balance } from '../../shared/types';
import { formatQuantity } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { StockNav } from './StockNav';

const ORDER: Balance['locationKind'][] = ['WAREHOUSE', 'IN_TRANSIT', 'CUSTODY', 'CORE_BIN'];

/** Groups balances by location: the warehouse, material in transit (M5), each technician's custody, then failed parts received. */
export function groupBalances(balances: Balance[]): { key: string; kind: Balance['locationKind']; name: string; rows: Balance[] }[] {
  const groups = new Map<string, { key: string; kind: Balance['locationKind']; name: string; rows: Balance[] }>();
  for (const b of balances) {
    if (!ORDER.includes(b.locationKind)) continue;
    if (b.locationKind !== 'WAREHOUSE' && b.quantity === 0) continue;
    const g = groups.get(b.locationId) ?? { key: b.locationId, kind: b.locationKind, name: b.custodianName ?? b.locationName, rows: [] };
    g.rows.push(b);
    groups.set(b.locationId, g);
  }
  return [...groups.values()].sort((a, b) => (a.kind === b.kind ? a.name.localeCompare(b.name) : ORDER.indexOf(a.kind) - ORDER.indexOf(b.kind)));
}

/** Stock balances by location (docs/lean-pilot/07): warehouse, in transit, each custody, core bin. Recomputed by the verifier. */
export function StockBalancesPage() {
  const { t, i18n } = useTranslation();
  const { result } = useResource<Balance[]>('/api/v1/stock/balances');
  if (!result) return <p>{t('auth.loading')}</p>;
  if (!result.ok) return <ProblemView problem={result.problem} />;
  const groups = groupBalances(result.data);
  return (
    <section data-testid="stock">
      <h1>{t('stock.title')}</h1>
      <StockNav />
      <p className="hint">{t('stock.hint')}</p>
      {groups.length === 0 ? <p>{t('common.empty')}</p> : groups.map((g) => (
        <div key={g.key} className="panel" data-testid="stock-location" data-kind={g.kind}>
          <h3>{g.kind === 'CUSTODY' ? t('stock.custodyOf', { name: g.name }) : g.kind === 'WAREHOUSE' ? t('stock.warehouse', { name: g.name }) : t(`locationKind.${g.kind}`)}</h3>
          <table className="table">
            <thead><tr><th>{t('stock.item')}</th><th>{t('stock.quantity')}</th></tr></thead>
            <tbody>
              {g.rows.map((b) => (
                <tr key={b.itemId} data-sku={b.sku}>
                  <td>{i18n.language === 'ar' ? b.itemNameAr : b.itemNameEn} <code>{b.sku}</code></td>
                  <td className={b.quantity < 0 ? 'negative' : ''}>{formatQuantity(b.quantity)} {b.unit}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ))}
    </section>
  );
}
