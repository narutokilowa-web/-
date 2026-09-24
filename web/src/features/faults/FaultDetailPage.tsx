import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import { DISMISS_REASONS, PRIORITIES, type Fault } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { hasRole, useSession } from '../../app/MeContext';

/** Fault triage (supervisor): create a work order from it, or dismiss it with a reason. */
export function FaultDetailPage() {
  const { id } = useParams();
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const session = useSession();
  const { post } = useApi();
  const { result, reload } = useResource<Fault>(`/api/v1/faults/${id}`);
  const [priority, setPriority] = useState('P2');
  const [woNote, setWoNote] = useState('');
  const [dismissReason, setDismissReason] = useState('');
  const [dismissNote, setDismissNote] = useState('');
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);

  if (!result) return <p>{t('auth.loading')}</p>;
  if (!result.ok) return <ProblemView problem={result.problem} />;
  const f = result.data;
  const supervisor = hasRole(session, 'SUPERVISOR');

  async function createWorkOrder() {
    setBusy(true);
    setProblem(null);
    const res = await post<{ id: string }>('/api/v1/work-orders', { faultId: f.id, priority, note: woNote.trim() || null });
    setBusy(false);
    if (!res.ok) setProblem(res.problem);
    else navigate(`/work-orders/${res.data.id}`);
  }

  async function dismiss() {
    setBusy(true);
    setProblem(null);
    const res = await post(`/api/v1/faults/${f.id}/dismiss`, { reason: dismissReason, note: dismissNote.trim() || null });
    setBusy(false);
    if (!res.ok) setProblem(res.problem);
    else reload();
  }

  return (
    <section>
      <h1>
        {t('fault.detailTitle')} <span className={`badge fault-${f.state}`} data-testid="fault-state">{t(`faultState.${f.state}`)}</span>
      </h1>
      <dl className="facts">
        <dt>{t('asset.code')}</dt><dd><Link to={`/assets/${f.assetId}`}>{f.assetCode}</Link> <span className="hint">{f.assetAddress}</span></dd>
        <dt>{t('fault.category')}</dt><dd>{t(`faultCategory.${f.category}`)}</dd>
        <dt>{t('fault.power')}</dt>
        <dd>
          {t(`power.${f.powerAtInspection}`)}{f.powerInfoSource ? ` — ${t(`powerSourceInfo.${f.powerInfoSource}`)}` : ''}
          {f.reportedWithoutPower && <span className="badge warn"> {t('fault.noPowerBadge')}</span>}
        </dd>
        <dt>{t('field.responsibleParty')}</dt><dd>{t(`responsibleParty.${f.responsibleParty}`)}</dd>
        <dt>{t('fault.description')}</dt><dd>{f.description ?? '—'}</dd>
        <dt>{t('fault.reportedBy')}</dt><dd>{f.reportedByName} — {formatDateTime(f.reportedAt, i18n.language)}</dd>
        {f.workOrderId && (<><dt>{t('workOrder.title')}</dt><dd><Link to={`/work-orders/${f.workOrderId}`} data-testid="fault-work-order">{t('common.open')}</Link></dd></>)}
        {f.dismissReason && (<><dt>{t('fault.dismissReason')}</dt><dd>{t(`dismissReason.${f.dismissReason}`)}{f.dismissNote ? ` — ${f.dismissNote}` : ''} ({f.dismissedByName})</dd></>)}
      </dl>

      {f.confirmations.length > 0 && (
        <>
          <h2>{t('fault.confirmationsTitle')}</h2>
          <ul data-testid="confirmations">
            {f.confirmations.map((c) => <li key={c.id}>{formatDateTime(c.at, i18n.language)} — {c.confirmedByName}: {t(`power.${c.powerAtInspection}`)} {c.description ?? ''}</li>)}
          </ul>
        </>
      )}

      {supervisor && f.state === 'OPEN' && (
        <div className="grid-2">
          <div className="panel" data-testid="create-work-order">
            <h3>{t('workOrder.create')}</h3>
            <label>
              <span>{t('workOrder.priority')}</span>
              <select value={priority} data-testid="wo-priority" onChange={(e) => setPriority(e.target.value)}>
                {PRIORITIES.map((p) => <option key={p} value={p}>{t(`priority.${p}`)}</option>)}
              </select>
            </label>
            <label>
              <span>{t('workOrder.note')}</span>
              <textarea value={woNote} maxLength={1000} data-testid="wo-note" onChange={(e) => setWoNote(e.target.value)} />
            </label>
            <button type="button" className="primary" disabled={busy} data-testid="wo-create" onClick={() => void createWorkOrder()}>{t('workOrder.create')}</button>
          </div>
          <div className="panel" data-testid="dismiss-fault">
            <h3>{t('fault.dismiss')}</h3>
            <select value={dismissReason} data-testid="dismiss-reason" onChange={(e) => setDismissReason(e.target.value)}>
              <option value="">—</option>
              {DISMISS_REASONS.map((r) => <option key={r} value={r}>{t(`dismissReason.${r}`)}</option>)}
            </select>
            <textarea value={dismissNote} placeholder={t('fault.dismissNote')} onChange={(e) => setDismissNote(e.target.value)} />
            <button type="button" disabled={busy || !dismissReason} data-testid="dismiss" onClick={() => void dismiss()}>{t('fault.dismiss')}</button>
          </div>
        </div>
      )}
      {problem && <ProblemView problem={problem} />}
    </section>
  );
}
