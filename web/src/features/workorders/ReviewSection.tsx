import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { ChecklistTemplate, ChecklistResult, ReviewEvidence, WorkOrderReview } from '../../shared/types';
import { RETURN_REASONS } from '../../shared/types';
import { formatDateTime, formatMoney, formatQuantity, shortHash } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { EvidenceImage } from './EvidenceImage';

/** Photos in the order the supervisor reads them: before → test → after. Superseded photos are shown last, marked. */
export function groupEvidence(evidence: ReviewEvidence[]): { kind: ReviewEvidence['kind']; items: ReviewEvidence[] }[] {
  const order: ReviewEvidence['kind'][] = ['BEFORE', 'TEST', 'AFTER'];
  return order
    .map((kind) => ({
      kind,
      items: evidence
        .filter((e) => e.kind === kind)
        .sort((a, b) => Number(a.supersededBy !== null) - Number(b.supersededBy !== null) || a.announcedAt.localeCompare(b.announcedAt)),
    }))
    .filter((g) => g.items.length > 0);
}

/** A checklist answer differs from what the template expects (shown highlighted to the reviewer). */
export function unexpectedAnswers(template: ChecklistTemplate | null, checklist: ChecklistResult | null): string[] {
  if (!template || !checklist) return [];
  return template.items.filter((i) => checklist.answers[i.code] !== undefined && checklist.answers[i.code] !== i.expected).map((i) => i.code);
}

/**
 * The supervisor's review of the field work (docs/lean-pilot/09 §9.3): before/after photos, diagnosis, material with cost,
 * checklist, arrivals, submissions, flags. Approve stays disabled until every photo is VERIFIED; the server re-checks everything.
 */
export function ReviewSection({ workOrderId, allowedCommands, onChanged }: { workOrderId: string; allowedCommands: string[]; onChanged: () => void }) {
  const { t, i18n } = useTranslation();
  const { post } = useApi();
  const review = useResource<WorkOrderReview>(`/api/v1/work-orders/${workOrderId}/review`);
  const [note, setNote] = useState('');
  const [reasons, setReasons] = useState<string[]>([]);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);
  const r = review.result?.ok ? review.result.data : null;
  const pending = r?.pendingEvidence ?? 0;

  // While photos are still being verified on the server, refresh so Approve enables itself.
  const { reload } = review;
  useEffect(() => {
    if (pending === 0) return;
    const timer = window.setInterval(reload, 3000);
    return () => window.clearInterval(timer);
  }, [pending, reload]);

  if (!review.result) return <p>{t('auth.loading')}</p>;
  if (!review.result.ok) return <ProblemView problem={review.result.problem} />;
  if (!r) return null;
  const ar = i18n.language === 'ar';
  const unexpected = unexpectedAnswers(r.template, r.checklist);
  const canApproveCommand = allowedCommands.includes('ApproveWorkOrder');
  const canReturnCommand = allowedCommands.includes('ReturnWorkOrder');
  const evidenceById = new Map(r.evidence.map((e) => [e.id, e]));

  async function decide(path: 'approve' | 'return') {
    setBusy(true);
    setProblem(null);
    const body = path === 'approve' ? { note: note.trim() || null } : { reasons, note: note.trim() || null };
    const res = await post(`/api/v1/work-orders/${workOrderId}/${path}`, body);
    setBusy(false);
    if (!res.ok) {
      setProblem(res.problem);
      review.reload();
      return;
    }
    setNote('');
    setReasons([]);
    review.reload();
    onChanged();
  }

  return (
    <section data-testid="review">
      <h2>{t('review.title')}</h2>

      {r.sealHash && (
        <div className="panel seal" data-testid="seal">
          <h3>{t('review.sealTitle')}</h3>
          <p>{t('review.closedBy', { name: r.closedByName ?? '—', at: formatDateTime(r.closedAt, i18n.language) })}</p>
          <p className="hash-full" data-testid="seal-hash"><code>{r.sealHash}</code></p>
          <p className="hint">{t('review.sealHint')}</p>
        </div>
      )}

      {r.flags.length > 0 && (
        <div className="panel flags" data-testid="flags">
          <h3>{t('review.flags')}</h3>
          <ul>
            {r.flags.map((f) => (
              <li key={f.id} data-flag={f.ruleCode}>
                <span className={`badge severity-${f.severity}`}>{t(`severity.${f.severity}`)}</span>{' '}
                {t(`flag.${f.ruleCode}`, { defaultValue: f.ruleCode })}
              </li>
            ))}
          </ul>
        </div>
      )}

      <dl className="facts">
        <dt>{t('review.diagnosis')}</dt>
        <dd data-testid="review-diagnosis">{r.diagnosis ? (ar ? r.diagnosis.nameAr : r.diagnosis.nameEn) : '—'}{r.diagnosisNote ? ` — ${r.diagnosisNote}` : ''}</dd>
        <dt>{t('review.rootCause')}</dt><dd>{r.rootCauseNote ?? '—'}</dd>
        <dt>{t('review.test')}</dt>
        <dd data-testid="review-test">{r.testMethod ? t(`testMethod.${r.testMethod}`, { defaultValue: r.testMethod }) : '—'}{r.testResult ? ` — ${t(`testResult.${r.testResult}`)}` : ''}</dd>
        <dt>{t('review.arrived')}</dt><dd>{formatDateTime(r.firstArrivedAt, i18n.language)}</dd>
        <dt>{t('review.started')}</dt><dd>{formatDateTime(r.workStartedAt, i18n.language)}</dd>
        <dt>{t('review.submitted')}</dt><dd>{formatDateTime(r.submittedAt, i18n.language)}</dd>
        {r.workMinutes !== null && (<><dt>{t('review.workMinutes')}</dt><dd>{r.workMinutes}</dd></>)}
        {r.noMaterialReason && (<><dt>{t('review.noMaterialReason')}</dt><dd>{r.noMaterialReason}</dd></>)}
      </dl>

      <h3>{t('review.photos')}</h3>
      {r.evidence.length === 0 ? <p>{t('common.empty')}</p> : (
        <div className="evidence-groups" data-testid="evidence">
          {groupEvidence(r.evidence).map((g) => (
            <div key={g.kind} className="evidence-group">
              <h4>{t(`evidenceKind.${g.kind}`)} ({g.items.filter((e) => !e.supersededBy).length})</h4>
              <ul className="evidence-grid">
                {g.items.map((e) => (
                  <li key={e.id} className={e.supersededBy ? 'superseded' : ''} data-testid="evidence-item" data-state={e.state}>
                    {e.hasPreview ? <EvidenceImage evidenceId={e.id} alt={t(`evidenceKind.${e.kind}`)} /> : <div className="evidence-placeholder">{t(`evidenceState.${e.state}`)}</div>}
                    <span className={`badge evidence-${e.state}`}>{t(`evidenceState.${e.state}`)}</span>
                    <span className="hint">{e.capturedByName} · {formatDateTime(e.capturedAt ?? e.announcedAt, i18n.language)}</span>
                    {e.supersededBy && <span className="hint">{t('review.superseded')}{e.supersedeReason ? `: ${e.supersedeReason}` : ''}</span>}
                    <code className="hash" title={e.sha256Device}>{shortHash(e.sha256Device, 12)}</code>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}

      <h3>{t('review.materials')}</h3>
      {r.materials.length === 0 ? <p>{t('review.noMaterials')}</p> : (
        <table className="table" data-testid="materials">
          <thead><tr><th>{t('stock.item')}</th><th>{t('stock.quantity')}</th><th>{t('stock.serials')}</th><th>{t('stock.unitCost')}</th><th>{t('stock.totalCost')}</th><th /></tr></thead>
          <tbody>
            {r.materials.map((m) => (
              <tr key={m.id}>
                <td>{ar ? m.itemNameAr : m.itemNameEn} <code>{m.sku}</code></td>
                <td>{formatQuantity(m.quantity)} {m.unit}</td>
                <td>{m.serials.length > 0 ? m.serials.join('، ') : '—'}</td>
                <td>{formatMoney(m.unitCost, i18n.language)}</td>
                <td data-testid="material-cost">{formatMoney(m.totalCost, i18n.language)}</td>
                <td>
                  {m.exceedsCustody && <span className="badge warn" data-testid="exceeds-custody">{t('review.exceedsCustody')}</span>}{' '}
                  {m.serialNotInCustody && <span className="badge warn">{t('review.serialNotInCustody')}</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {r.expectedItemsMissing.length > 0 && <p className="hint" data-testid="expected-missing">{t('review.expectedMissing', { items: r.expectedItemsMissing.join('، ') })}</p>}

      <h3>{t('review.checklist')}</h3>
      {!r.checklist || !r.template ? <p>{t('common.empty')}</p> : (
        <table className="table" data-testid="checklist">
          <thead><tr><th>{t('review.checkItem')}</th><th>{t('review.expected')}</th><th>{t('review.answer')}</th></tr></thead>
          <tbody>
            {r.template.items.map((i) => (
              <tr key={i.code} className={unexpected.includes(i.code) ? 'unexpected' : ''}>
                <td>{ar ? i.textAr : i.textEn}{i.testItem ? ` (${t('review.testItem')})` : ''}</td>
                <td>{t(`answer.${i.expected}`)}</td>
                <td>{r.checklist!.answers[i.code] ? t(`answer.${r.checklist!.answers[i.code]}`) : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {r.checklist?.notTestedReason && <p className="hint">{t('review.notTestedReason')}: {r.checklist.notTestedReason}</p>}

      <h3>{t('review.arrivals')}</h3>
      {r.arrivals.length === 0 ? <p>{t('common.empty')}</p> : (
        <ul className="plain" data-testid="arrivals">
          {r.arrivals.map((a) => (
            <li key={a.id}>
              #{a.arrivalNo} · {a.userName} · {formatDateTime(a.recordedAt, i18n.language)} ·{' '}
              {a.tagScanned ? t('review.tagScanned') : t('review.manualCode', { reason: a.manualReason ?? '—' })}
            </li>
          ))}
        </ul>
      )}

      {r.submissions.length > 0 && (
        <>
          <h3>{t('review.submissions')}</h3>
          <table className="table" data-testid="submissions">
            <thead><tr><th>#</th><th>{t('review.submittedBy')}</th><th>{t('review.added')}</th><th>{t('review.payloadHash')}</th></tr></thead>
            <tbody>
              {r.submissions.map((s) => (
                <tr key={s.id}>
                  <td>{s.submissionNo}</td>
                  <td>{s.submittedByName} — {formatDateTime(s.submittedAt, i18n.language)}</td>
                  <td>
                    {s.submissionNo === 1 ? '—' : t('review.addedCounts', {
                      photos: s.addedEvidenceIds.length,
                      materials: s.addedMaterialIds.length,
                    })}
                    {s.submissionNo > 1 && s.addedEvidenceIds.length > 0 && (
                      <span className="hint"> ({s.addedEvidenceIds.map((id) => t(`evidenceKind.${evidenceById.get(id)?.kind ?? 'AFTER'}`)).join('، ')})</span>
                    )}
                  </td>
                  <td><code title={s.payloadHash}>{shortHash(s.payloadHash)}</code></td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}

      {r.reviews.length > 0 && (
        <>
          <h3>{t('review.decisions')}</h3>
          <ul className="plain" data-testid="decisions">
            {r.reviews.map((d) => (
              <li key={d.id}>
                #{d.submissionNo} · <strong>{t(`reviewDecision.${d.decision}`)}</strong> · {d.reviewerName} · {formatDateTime(d.decidedAt, i18n.language)}
                {d.reasons.length > 0 && ` · ${d.reasons.map((x) => t(`returnReason.${x}`)).join('، ')}`}
                {d.note && ` · ${d.note}`}
              </li>
            ))}
          </ul>
        </>
      )}

      {(canApproveCommand || canReturnCommand) && (
        <div className="panel" data-testid="review-actions">
          <h3>{t('review.decide')}</h3>
          {r.approveBlockedReason && <p className="hint" data-testid="approve-blocked">{t(`approveBlocked.${r.approveBlockedReason}`, { count: r.pendingEvidence })}</p>}
          {canReturnCommand && (
            <fieldset className="reasons">
              <legend>{t('review.returnReasons')}</legend>
              {RETURN_REASONS.map((x) => (
                <label key={x} className="inline">
                  <input
                    type="checkbox"
                    data-testid={`return-reason-${x}`}
                    checked={reasons.includes(x)}
                    onChange={(e) => setReasons((cur) => (e.target.checked ? [...cur, x] : cur.filter((y) => y !== x)))}
                  />
                  {t(`returnReason.${x}`)}
                </label>
              ))}
            </fieldset>
          )}
          <label>
            <span>{t('review.note')}</span>
            <textarea value={note} data-testid="review-note" onChange={(e) => setNote(e.target.value)} />
          </label>
          <div className="actions">
            {canApproveCommand && (
              <button type="button" className="primary" data-testid="wo-approve" disabled={busy || !r.canApprove} onClick={() => void decide('approve')}>
                {t('review.approve')}
              </button>
            )}
            {canReturnCommand && (
              <button type="button" className="danger" data-testid="wo-return" disabled={busy || reasons.length === 0} onClick={() => void decide('return')}>
                {t('review.return')}
              </button>
            )}
          </div>
        </div>
      )}
      {problem && <ProblemView problem={problem} />}
    </section>
  );
}
