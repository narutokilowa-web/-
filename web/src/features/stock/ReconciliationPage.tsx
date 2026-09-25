import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { Reconciliation } from '../../shared/types';
import { formatDateTime, formatQuantity } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { StockNav } from './StockNav';

/**
 * Warehouse reconciliation (M5): per item, what is where (warehouse, in transit, custody, consumed, core bin); stored balances
 * against the ledger; in-transit stock against the open issues and returns; serial counts; the core bin against received cores;
 * custody left with disabled users. Findings are signals to look at; stock only changes through the commands of its flow.
 */
export function ReconciliationPage() {
  const { t, i18n } = useTranslation();
  const { result } = useResource<Reconciliation>('/api/v1/stock/reconciliation');
  if (!result) return <p>{t('auth.loading')}</p>;
  if (!result.ok) return <ProblemView problem={result.problem} />;
  const r = result.data;
  const name = (x: { nameAr: string; nameEn: string }) => (i18n.language === 'ar' ? x.nameAr : x.nameEn);
  return (
    <section data-testid="reconciliation">
      <h1>{t('reconciliation.title')}</h1>
      <StockNav />
      <p className="hint">{t('reconciliation.hint')}</p>
      <p>
        <span className={`badge ${r.balanced ? 'user-ACTIVE' : 'alert'}`} data-testid="reconciliation-status">
          {r.balanced ? t('reconciliation.balanced') : t('reconciliation.notBalanced')}
        </span>{' '}
        <span className="hint">{t('reconciliation.generated', { at: formatDateTime(r.generatedAt, i18n.language), count: r.movementsChecked })}</span>
      </p>
      <div className="panel" data-testid="reconciliation-open">
        <h3>{t('reconciliation.open')}</h3>
        <ul className="plain">
          <li><Link to="/stock/issues?state=ISSUED">{t('reconciliation.issuesAwaiting', { count: r.open.issuesAwaitingAcknowledgment, overdue: r.open.issuesOverdue })}</Link></li>
          <li><Link to="/stock/issues?state=DISPUTED">{t('reconciliation.issuesDisputed', { count: r.open.issuesDisputed })}</Link></li>
          <li><Link to="/stock/returns?state=DECLARED">{t('reconciliation.returnsDeclared', { count: r.open.returnsDeclared })}</Link></li>
          <li><Link to="/stock/core-returns?state=OPEN">{t('reconciliation.coresOpen', { count: r.open.coreReturnsOpen })}</Link></li>
          <li><Link to="/stock/core-returns?state=DECLARED_WITH_PART">{t('reconciliation.coresWithPart', { count: r.open.coreReturnsDeclaredWithPart, overdue: r.open.coreReturnsOverdue })}</Link></li>
          <li><Link to="/stock/core-returns?state=DECLARED_UNRECOVERABLE">{t('reconciliation.coresUnrecoverable', { count: r.open.coreReturnsUnrecoverable })}</Link></li>
        </ul>
      </div>
      {r.findings.length > 0 && (
        <div className="panel" data-testid="reconciliation-findings">
          <h3>{t('reconciliation.findings')}</h3>
          <ul className="plain">
            {r.findings.map((f, i) => (
              <li key={i} data-testid="reconciliation-finding" data-code={f.code}>
                <span className={`badge severity-${f.severity}`}>{t(`reconciliationFinding.${f.code}`, { defaultValue: f.code })}</span> <span className="hint">{f.message}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
      <table className="table" data-testid="reconciliation-items">
        <thead>
          <tr>
            <th>{t('stock.item')}</th><th>{t('reconciliation.received')}</th><th>{t('reconciliation.warehouse')}</th><th>{t('reconciliation.inTransit')}</th>
            <th>{t('reconciliation.custody')}</th><th>{t('reconciliation.consumed')}</th><th>{t('reconciliation.coreBin')}</th>
          </tr>
        </thead>
        <tbody>
          {r.items.map((x) => (
            <tr key={x.itemId} data-sku={x.sku}>
              <td><Link to={`/stock/history?itemId=${x.itemId}`}><code>{x.sku}</code></Link> {name(x)}</td>
              <td>{formatQuantity(x.receivedNet)}</td>
              <td>{formatQuantity(x.warehouse)}</td>
              <td>{formatQuantity(x.inTransit)}</td>
              <td className={x.custody < 0 ? 'negative' : ''}>{formatQuantity(x.custody)}</td>
              <td>{formatQuantity(x.consumed)}</td>
              <td>{formatQuantity(x.coreBin)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <h2>{t('reconciliation.custodians')}</h2>
      <table className="table" data-testid="reconciliation-custodians">
        <thead><tr><th>{t('reconciliation.technician')}</th><th>{t('reconciliation.holds')}</th><th>{t('reconciliation.openDocs')}</th></tr></thead>
        <tbody>
          {r.custodians.map((c) => (
            <tr key={c.userId} data-user-id={c.userId} data-active={c.active}>
              <td>
                <Link to={`/stock/history?technicianId=${c.userId}`}>{c.name}</Link>
                {!c.active && <span className="badge user-DISABLED">{t('reconciliation.disabled')}</span>}
              </td>
              <td>{c.items.length === 0 ? '—' : c.items.map((h) => `${h.sku} ${formatQuantity(h.quantity)}`).join(', ')}</td>
              <td className="hint">{t('reconciliation.docs', { issues: c.openIssues, returns: c.openReturns, cores: c.openCoreReturns })}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
