import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { DashboardSummary } from '../../shared/types';
import { formatDateTime, shortHash } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';

/** DEMO 1 dashboard (docs/lean-pilot/11 §11.2 M3 item 5): counts only, plus the recently completed work orders. */
export function DashboardPage() {
  const { t, i18n } = useTranslation();
  const { result } = useResource<DashboardSummary>('/api/v1/dashboard/summary');
  if (!result) return <p>{t('auth.loading')}</p>;
  if (!result.ok) return <ProblemView problem={result.problem} />;
  const d = result.data;
  const openWorkOrders = d.openWorkOrdersByState.reduce((n, x) => n + x.count, 0);

  return (
    <section data-testid="dashboard">
      <h1>{t('dashboard.title')}</h1>
      <div className="grid-2">
        <div className="panel" data-testid="dash-lights">
          <h3>{t('dashboard.lightsByStatus')}</h3>
          <ul className="counts">
            {d.lightsByOperationalStatus.map((x) => (
              <li key={x.key} data-key={x.key}><span className={`badge status-${x.key}`}>{t(`status.${x.key}`, { defaultValue: x.key })}</span> <strong>{x.count}</strong></li>
            ))}
          </ul>
        </div>
        <div className="panel" data-testid="dash-faults">
          <h3>{t('dashboard.openFaults')}</h3>
          <p className="big"><Link to="/faults?state=OPEN">{d.openFaults}</Link></p>
          <p className="hint">{t('dashboard.withoutPower', { count: d.openFaultsReportedWithoutPower })}</p>
        </div>
        <div className="panel" data-testid="dash-work-orders">
          <h3>{t('dashboard.openWorkOrders')} ({openWorkOrders})</h3>
          <ul className="counts">
            {d.openWorkOrdersByState.map((x) => (
              <li key={x.key} data-key={x.key}><Link to={`/work-orders?state=${x.key}`}><span className={`badge wo-${x.key}`}>{t(`workOrderState.${x.key}`)}</span></Link> <strong>{x.count}</strong></li>
            ))}
          </ul>
        </div>
        <div className="panel" data-testid="dash-pending-review">
          <h3>{t('dashboard.pendingReview')}</h3>
          <p className="big"><Link to="/work-orders?state=PENDING_REVIEW">{d.pendingReview}</Link></p>
        </div>
      </div>

      <h2>{t('dashboard.recentlyCompleted')}</h2>
      {d.recentlyCompleted.length === 0 ? <p>{t('common.empty')}</p> : (
        <table className="table" data-testid="recently-completed">
          <thead><tr><th>{t('workOrder.code')}</th><th>{t('asset.code')}</th><th>{t('review.diagnosis')}</th><th>{t('dashboard.closedAt')}</th><th>{t('review.seal')}</th></tr></thead>
          <tbody>
            {d.recentlyCompleted.map((w) => (
              <tr key={w.id} data-work-order-id={w.id}>
                <td><Link to={`/work-orders/${w.id}`}>{w.code}</Link></td>
                <td><Link to={`/assets/${w.assetId}`}>{w.assetCode}</Link> <span className="hint">{w.assetAddress}</span></td>
                <td>{w.diagnosisCode ? t(`diagnosis.${w.diagnosisCode}`, { defaultValue: w.diagnosisCode }) : '—'}</td>
                <td>{formatDateTime(w.closedAt, i18n.language)} ({w.closedByName})</td>
                <td><code title={w.sealHash ?? ''}>{shortHash(w.sealHash, 12)}</code></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
