import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { QuarantineEntry } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { hasRole, useSession } from '../../app/MeContext';

const STATES = ['PENDING', 'APPLIED', 'DISCARDED', 'APPLY_FAILED', ''] as const;

/**
 * Commands the server held instead of applying (docs/lean-pilot/05 §5.5, docs/spec/10 §10.5): their time could not be proven
 * inside the offline grant, or the work order was cancelled meanwhile. A supervisor other than the author applies or discards
 * each one with a reason; both decisions are audited and nothing is lost.
 */
export function QuarantinePage() {
  const { t, i18n } = useTranslation();
  const session = useSession();
  const canResolve = hasRole(session, 'SUPERVISOR');
  const [params, setParams] = useSearchParams();
  const state = params.get('state') ?? 'PENDING';
  const { post } = useApi();
  const { result, reload } = useResource<QuarantineEntry[]>(`/api/v1/sync/quarantine${state ? `?state=${state}` : ''}`);
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);

  async function resolve(id: string, decision: 'APPLY' | 'DISCARD') {
    setBusy(true);
    setProblem(null);
    const reason = (reasons[id] ?? '').trim();
    const res = await post(`/api/v1/sync/quarantine/${id}/resolve`, { decision, reason: reason === '' ? null : reason });
    setBusy(false);
    if (!res.ok) setProblem(res.problem);
    reload();
  }

  return (
    <section>
      <h1>{t('quarantine.title')}</h1>
      <p className="hint">{t('quarantine.hint')}</p>
      <div className="filters">
        {STATES.map((s) => (
          <button key={s || 'all'} type="button" className={s === state ? 'primary' : ''} data-testid={`quarantine-filter-${s || 'ALL'}`} onClick={() => setParams({ state: s })}>
            {s ? t(`quarantineState.${s}`) : t('common.all')}
          </button>
        ))}
      </div>
      {problem && <ProblemView problem={problem} />}
      {!result ? <p>{t('auth.loading')}</p> : !result.ok ? <ProblemView problem={result.problem} /> : result.data.length === 0 ? <p>{t('common.empty')}</p> : (
        <table className="table" data-testid="quarantine">
          <thead>
            <tr><th>{t('quarantine.command')}</th><th>{t('quarantine.author')}</th><th>{t('quarantine.reason')}</th><th>{t('quarantine.time')}</th><th>{t('quarantine.state')}</th><th /></tr>
          </thead>
          <tbody>
            {result.data.map((q) => (
              <tr key={q.id} data-quarantine-id={q.id} data-command-id={q.commandId}>
                <td>
                  <strong>{t(`syncCommand.${q.type}`, { defaultValue: q.type })}</strong>
                  <details><summary className="hint">{t('quarantine.payload')}</summary><pre className="payload">{JSON.stringify(q.payload, null, 2)}</pre></details>
                </td>
                <td>{q.userName}<div className="hint">{t('quarantine.device', { id: q.deviceId.slice(0, 8) })}</div></td>
                <td data-testid="quarantine-reason">{t(`error.${q.reasonCode}`, { defaultValue: q.reasonCode })}</td>
                <td className="hint">
                  <div>{t('quarantine.received', { at: formatDateTime(q.receivedAt, i18n.language) })}</div>
                  <div>{t('quarantine.deviceTime', { at: formatDateTime(q.deviceTime, i18n.language) })}</div>
                  <div>{t('quarantine.estimated', { at: formatDateTime(q.estimatedTime, i18n.language) })} · {t(`timeConfidence.${q.timeConfidence}`)}</div>
                </td>
                <td>
                  <span className={`badge quarantine-${q.state}`} data-testid="quarantine-state">{t(`quarantineState.${q.state}`)}</span>
                  {q.resolvedAt && <div className="hint">{q.resolvedByName}: {q.resolutionReason}</div>}
                </td>
                <td>
                  {canResolve && q.state === 'PENDING' && q.userId !== session.me.userId && (
                    <div className="row-actions">
                      <input placeholder={t('quarantine.decisionReason')} value={reasons[q.id] ?? ''} data-testid="quarantine-decision-reason"
                        onChange={(e) => setReasons({ ...reasons, [q.id]: e.target.value })} />
                      <button type="button" className="primary" disabled={busy} data-testid="quarantine-apply" onClick={() => void resolve(q.id, 'APPLY')}>{t('quarantine.apply')}</button>
                      <button type="button" disabled={busy} data-testid="quarantine-discard" onClick={() => void resolve(q.id, 'DISCARD')}>{t('quarantine.discard')}</button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
