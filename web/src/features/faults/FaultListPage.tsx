import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { Fault } from '../../shared/types';
import { ProblemView } from '../../shared/ProblemView';
import { FaultTable } from './FaultTable';

const STATES = ['OPEN', 'IN_WORK_ORDER', 'DISMISSED', 'RESOLVED', ''] as const;

export function FaultListPage() {
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const state = params.get('state') ?? 'OPEN';
  const { result } = useResource<Fault[]>(`/api/v1/faults${state ? `?state=${state}` : ''}`);
  return (
    <section>
      <h1>{t('fault.listTitle')}</h1>
      <div className="filters">
        {STATES.map((s) => (
          <button key={s || 'all'} type="button" className={s === state ? 'primary' : ''} onClick={() => setParams({ state: s })}>
            {s ? t(`faultState.${s}`) : t('common.all')}
          </button>
        ))}
      </div>
      {!result ? <p>{t('auth.loading')}</p> : !result.ok ? <ProblemView problem={result.problem} /> : result.data.length === 0 ? <p>{t('common.empty')}</p> : <FaultTable faults={result.data} />}
    </section>
  );
}
