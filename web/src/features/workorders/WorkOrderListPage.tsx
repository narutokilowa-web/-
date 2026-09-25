import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { WorkOrderListItem } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';

const STATES = ['NEW', 'ASSIGNED', 'ON_SITE', 'IN_PROGRESS', 'PENDING_REVIEW', 'RETURNED', 'CLOSED', 'CANCELLED', ''] as const;

export function WorkOrderListPage() {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const state = params.get('state') ?? '';
  const { result } = useResource<WorkOrderListItem[]>(`/api/v1/work-orders${state ? `?state=${state}` : ''}`);
  return (
    <section>
      <h1>{t('workOrder.listTitle')}</h1>
      <div className="filters">
        {STATES.map((s) => (
          <button key={s || 'all'} type="button" className={s === state ? 'primary' : ''} onClick={() => setParams({ state: s })}>
            {s ? t(`workOrderState.${s}`) : t('common.all')}
          </button>
        ))}
      </div>
      {!result ? <p>{t('auth.loading')}</p> : !result.ok ? <ProblemView problem={result.problem} /> : result.data.length === 0 ? <p>{t('common.empty')}</p> : (
        <table className="table" data-testid="work-orders">
          <thead>
            <tr><th>{t('workOrder.code')}</th><th>{t('workOrder.state')}</th><th>{t('workOrder.priority')}</th><th>{t('asset.code')}</th><th>{t('fault.category')}</th><th>{t('workOrder.assignee')}</th><th>{t('workOrder.createdAt')}</th></tr>
          </thead>
          <tbody>
            {result.data.map((w) => (
              <tr key={w.id} data-work-order-id={w.id}>
                <td><Link to={`/work-orders/${w.id}`}>{w.code}</Link></td>
                <td><span className={`badge wo-${w.state}`}>{t(`workOrderState.${w.state}`)}</span></td>
                <td>{t(`priority.${w.priority}`)}</td>
                <td>{w.assetCode} <span className="hint">{w.assetAddress}</span></td>
                <td>{t(`faultCategory.${w.faultCategory}`)}</td>
                <td>{w.assigneeName ?? '—'}</td>
                <td>{formatDateTime(w.createdAt, i18n.language)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
