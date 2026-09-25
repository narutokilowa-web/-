import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { HistoryEntry, Technician, WorkOrder } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { HistoryTimeline } from '../assets/HistoryTimeline';
import { ReviewSection } from './ReviewSection';

/** States in which there is field work to look at (arrival onwards). */
const FIELD_STATES = ['ON_SITE', 'IN_PROGRESS', 'PENDING_REVIEW', 'RETURNED', 'CLOSED'];

/**
 * Work-order page (supervisor): assign / reassign to a technician with an approved device, cancel; review of the field work
 * (approve / return) and the closure seal; timeline from the audit log.
 */
export function WorkOrderDetailPage() {
  const { id } = useParams();
  const { t, i18n } = useTranslation();
  const { post } = useApi();
  const wo = useResource<WorkOrder>(`/api/v1/work-orders/${id}`);
  const timeline = useResource<HistoryEntry[]>(`/api/v1/work-orders/${id}/timeline`);
  const canAssign = wo.result?.ok && wo.result.data.allowedCommands.some((c) => c === 'Assign' || c === 'Reassign');
  const technicians = useResource<Technician[]>(canAssign ? '/api/v1/technicians' : null);
  const [assigneeId, setAssigneeId] = useState('');
  const [reason, setReason] = useState('');
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);

  if (!wo.result) return <p>{t('auth.loading')}</p>;
  if (!wo.result.ok) return <ProblemView problem={wo.result.problem} />;
  const w = wo.result.data;
  const can = (c: string) => w.allowedCommands.includes(c);

  async function run(path: string, body: unknown) {
    setBusy(true);
    setProblem(null);
    const res = await post(`/api/v1/work-orders/${w.id}/${path}`, body);
    setBusy(false);
    if (!res.ok) {
      setProblem(res.problem);
      return;
    }
    setReason('');
    setAssigneeId('');
    wo.reload();
    timeline.reload();
  }

  return (
    <section>
      <h1>
        <span data-testid="wo-code">{w.code}</span>{' '}
        <span className={`badge wo-${w.state}`} data-testid="wo-state">{t(`workOrderState.${w.state}`)}</span>{' '}
        <span className="badge">{t(`priority.${w.priority}`)}</span>
      </h1>
      <dl className="facts">
        <dt>{t('asset.code')}</dt>
        <dd>{w.asset && <Link to={`/assets/${w.asset.id}`}>{w.asset.assetCode}</Link>} <span className="hint">{w.asset?.addressText}</span></dd>
        <dt>{t('fault.category')}</dt><dd><Link to={`/faults/${w.faultId}`}>{t(`faultCategory.${w.faultCategory}`)}</Link> ({t(`faultState.${w.faultState}`)})</dd>
        <dt>{t('workOrder.assignee')}</dt><dd data-testid="wo-assignee">{w.assigneeName ?? '—'}</dd>
        <dt>{t('workOrder.sla')}</dt><dd>{w.slaDueAt ? formatDateTime(w.slaDueAt, i18n.language) : t('workOrder.noSla')}</dd>
        <dt>{t('workOrder.createdBy')}</dt><dd>{w.createdByName} — {formatDateTime(w.createdAt, i18n.language)}</dd>
        {w.note && (<><dt>{t('workOrder.note')}</dt><dd>{w.note}</dd></>)}
        {w.cancelReason && (<><dt>{t('workOrder.cancelReason')}</dt><dd>{w.cancelReason}</dd></>)}
      </dl>

      {(canAssign || can('Cancel')) && (
        <div className="panel" data-testid="wo-actions">
          {canAssign && (
            <label>
              <span>{t('workOrder.technician')}</span>
              <select value={assigneeId} data-testid="wo-assignee-select" onChange={(e) => setAssigneeId(e.target.value)}>
                <option value="">—</option>
                {technicians.result?.ok && technicians.result.data.map((x) => (
                  <option key={x.userId} value={x.userId} disabled={!x.hasActiveDevice || x.userId === w.assigneeId}>
                    {x.displayName} ({x.username}){x.hasActiveDevice ? '' : ` — ${t('workOrder.noActiveDevice')}`}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label>
            <span>{t('workOrder.reason')}</span>
            <input value={reason} data-testid="wo-reason" onChange={(e) => setReason(e.target.value)} />
          </label>
          <div className="actions">
            {can('Assign') && <button type="button" className="primary" disabled={busy || !assigneeId} data-testid="wo-assign" onClick={() => void run('assign', { assigneeId })}>{t('workOrder.assign')}</button>}
            {can('Reassign') && <button type="button" disabled={busy || !assigneeId} data-testid="wo-reassign" onClick={() => void run('reassign', { assigneeId, reason: reason.trim() || null })}>{t('workOrder.reassign')}</button>}
            {can('Cancel') && <button type="button" className="danger" disabled={busy} data-testid="wo-cancel" onClick={() => void run('cancel', { reason: reason.trim() || null })}>{t('workOrder.cancel')}</button>}
          </div>
          <p className="hint">{t('workOrder.assignHint')}</p>
        </div>
      )}
      {problem && <ProblemView problem={problem} />}

      {FIELD_STATES.includes(w.state) && (
        <ReviewSection workOrderId={w.id} allowedCommands={w.allowedCommands} onChanged={() => { wo.reload(); timeline.reload(); }} />
      )}

      <h2>{t('workOrder.assignments')}</h2>
      {w.assignments.length === 0 ? <p>{t('common.empty')}</p> : (
        <table className="table" data-testid="assignments">
          <thead><tr><th>{t('workOrder.technician')}</th><th>{t('workOrder.state')}</th><th>{t('workOrder.startedAt')}</th><th>{t('workOrder.endedAt')}</th><th>{t('workOrder.endReason')}</th></tr></thead>
          <tbody>
            {w.assignments.map((a) => (
              <tr key={a.id}>
                <td>{a.assigneeName}</td>
                <td>{t(`assignmentState.${a.state}`)}</td>
                <td>{formatDateTime(a.startedAt, i18n.language)} ({a.startedByName})</td>
                <td>{formatDateTime(a.endedAt, i18n.language)}</td>
                <td>{a.endReason ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>{t('workOrder.timeline')}</h2>
      {timeline.result?.ok ? <HistoryTimeline entries={timeline.result.data} /> : <p>{t('auth.loading')}</p>}
    </section>
  );
}
