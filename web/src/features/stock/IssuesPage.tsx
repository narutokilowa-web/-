import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { StockIssue, StockItem, Technician } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { hasRole, useSession } from '../../app/MeContext';
import { DocumentFlags, StockNav } from './StockNav';
import { LinesEditor, LinesView, emptyLine } from './LinesEditor';
import { buildLines, type LineDraft } from './stockForm';

const STATES = ['ISSUED', 'DISPUTED', 'ACKNOWLEDGED', 'CANCELLED', ''] as const;

/**
 * Stock issues (docs/lean-pilot/04 §4.9): the warehouse issues to a technician (WAREHOUSE → IN_TRANSIT); the material is in
 * their custody only when they acknowledge it on the phone. A disputed or unanswered issue is cancelled here with a reason
 * (REVERSAL back to the warehouse); nothing is edited or deleted.
 */
export function IssuesPage() {
  const { t, i18n } = useTranslation();
  const session = useSession();
  const warehouse = hasRole(session, 'WAREHOUSE');
  const [params, setParams] = useSearchParams();
  const state = params.get('state') ?? 'ISSUED';
  const { post } = useApi();
  const list = useResource<StockIssue[]>(`/api/v1/stock/issues${state ? `?state=${state}` : ''}`);
  const items = useResource<StockItem[]>(warehouse ? '/api/v1/stock/items' : null);
  const technicians = useResource<Technician[]>(warehouse ? '/api/v1/stock/technicians' : null);
  const [technicianId, setTechnicianId] = useState('');
  const [lines, setLines] = useState<LineDraft[]>([emptyLine()]);
  const [note, setNote] = useState('');
  const [requestedBy, setRequestedBy] = useState('');
  const [formError, setFormError] = useState<string | null>(null);
  const [created, setCreated] = useState<string | null>(null);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);

  async function issue() {
    setFormError(null);
    setProblem(null);
    setCreated(null);
    if (!technicianId) return setFormError('stockForm.technicianRequired');
    const built = buildLines(lines, items.result?.ok ? items.result.data : []);
    if (!built.ok) return setFormError(built.error);
    setBusy(true);
    const res = await post<{ issueId: string; code: string }>('/api/v1/stock/issues', {
      technicianId, lines: built.lines, note: note.trim() || null, requestedBy: requestedBy.trim() || null,
    });
    setBusy(false);
    if (!res.ok) return setProblem(res.problem);
    setCreated(res.data.code);
    setLines([emptyLine()]);
    setNote('');
    setRequestedBy('');
    list.reload();
  }

  async function cancel(id: string) {
    setProblem(null);
    setBusy(true);
    const res = await post(`/api/v1/stock/issues/${id}/cancel`, { reason: (reasons[id] ?? '').trim() || null });
    setBusy(false);
    if (!res.ok) setProblem(res.problem);
    list.reload();
  }

  return (
    <section data-testid="stock-issues">
      <h1>{t('issues.title')}</h1>
      <StockNav />
      <p className="hint">{t('issues.hint')}</p>
      {warehouse && (
        <div className="panel" data-testid="issue-form">
          <h3>{t('issues.new')}</h3>
          <label>{t('issues.technician')}{' '}
            <select value={technicianId} data-testid="issue-technician" onChange={(e) => setTechnicianId(e.target.value)}>
              <option value="">—</option>
              {technicians.result?.ok && technicians.result.data.map((x) => <option key={x.userId} value={x.userId}>{x.displayName} ({x.username})</option>)}
            </select>
          </label>
          {items.result?.ok && <LinesEditor items={items.result.data} lines={lines} onChange={setLines} />}
          <input placeholder={t('issues.requestedBy')} value={requestedBy} onChange={(e) => setRequestedBy(e.target.value)} />
          <input placeholder={t('issues.note')} value={note} data-testid="issue-note" onChange={(e) => setNote(e.target.value)} />
          {formError && <p role="alert" data-testid="form-error">{t(formError)}</p>}
          {created && <p data-testid="issue-created">{t('issues.created', { code: created })}</p>}
          <button type="button" className="primary" disabled={busy} data-testid="issue-submit" onClick={() => void issue()}>{t('issues.issue')}</button>
        </div>
      )}
      <div className="filters">
        {STATES.map((s) => (
          <button key={s || 'all'} type="button" className={s === state ? 'primary' : ''} data-testid={`issues-filter-${s || 'ALL'}`} onClick={() => setParams({ state: s })}>
            {s ? t(`issueState.${s}`) : t('common.all')}
          </button>
        ))}
      </div>
      {problem && <ProblemView problem={problem} />}
      {!list.result ? <p>{t('auth.loading')}</p> : !list.result.ok ? <ProblemView problem={list.result.problem} /> : list.result.data.length === 0 ? <p>{t('common.empty')}</p> : (
        <table className="table" data-testid="issues">
          <thead><tr><th>{t('issues.code')}</th><th>{t('issues.technician')}</th><th>{t('issues.lines')}</th><th>{t('issues.state')}</th><th /></tr></thead>
          <tbody>
            {list.result.data.map((x) => (
              <tr key={x.id} data-issue-id={x.id} data-code={x.code}>
                <td><strong>{x.code}</strong><div className="hint">{formatDateTime(x.issuedAt, i18n.language)} · {x.issuedByName}</div></td>
                <td>{x.technicianName}</td>
                <td><LinesView lines={x.lines} />{x.note && <div className="hint">{x.note}</div>}</td>
                <td>
                  <span className={`badge issue-${x.state}`} data-testid="issue-state">{t(`issueState.${x.state}`)}</span>
                  {x.overdue && <span className="badge warn" data-testid="issue-overdue">{t('issues.overdue')}</span>}
                  {x.disputeReason && <div className="hint" data-testid="issue-dispute-reason">{t('issues.disputeReason', { reason: x.disputeReason })}</div>}
                  {x.cancelReason && <div className="hint">{x.cancelledByName}: {x.cancelReason}</div>}
                  <DocumentFlags flags={x.flags} />
                </td>
                <td>
                  {warehouse && (x.state === 'ISSUED' || x.state === 'DISPUTED') && (
                    <div className="row-actions">
                      <input placeholder={t('issues.cancelReason')} value={reasons[x.id] ?? ''} data-testid="issue-cancel-reason"
                        onChange={(e) => setReasons({ ...reasons, [x.id]: e.target.value })} />
                      <button type="button" disabled={busy} data-testid="issue-cancel" onClick={() => void cancel(x.id)}>{t('issues.cancel')}</button>
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
