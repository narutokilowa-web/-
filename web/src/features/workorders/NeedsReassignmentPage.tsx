import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { WorkOrderListItem } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';

/** Open work orders whose assignee was offboarded (docs/lean-pilot/05 §5.6 step 7): reassign or cancel each one. */
export function NeedsReassignmentPage() {
  const { t, i18n } = useTranslation();
  const { result } = useResource<WorkOrderListItem[]>('/api/v1/work-orders/needs-reassignment');
  return (
    <section>
      <h1>{t('needsReassignment.title')}</h1>
      <p className="hint">{t('needsReassignment.hint')}</p>
      {!result ? <p>{t('auth.loading')}</p> : !result.ok ? <ProblemView problem={result.problem} /> : result.data.length === 0 ? <p>{t('common.empty')}</p> : (
        <table className="table" data-testid="needs-reassignment">
          <thead>
            <tr><th>{t('workOrder.code')}</th><th>{t('workOrder.state')}</th><th>{t('asset.code')}</th><th>{t('workOrder.assignee')}</th><th>{t('workOrder.createdAt')}</th></tr>
          </thead>
          <tbody>
            {result.data.map((w) => (
              <tr key={w.id} data-work-order-id={w.id}>
                <td><Link to={`/work-orders/${w.id}`}>{w.code}</Link></td>
                <td><span className={`badge wo-${w.state}`}>{t(`workOrderState.${w.state}`)}</span></td>
                <td>{w.assetCode} <span className="hint">{w.assetAddress}</span></td>
                <td>{w.assigneeName ?? '—'} <span className="badge alert">{t('userStatus.DISABLED')}</span></td>
                <td>{formatDateTime(w.createdAt, i18n.language)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
