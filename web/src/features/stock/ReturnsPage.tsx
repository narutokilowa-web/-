import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { CustodyLine, Reconciliation, StockItem, StockReturn } from '../../shared/types';
import { formatDateTime, formatQuantity } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { hasRole, useSession } from '../../app/MeContext';
import { DocumentFlags, StockNav } from './StockNav';
import { LinesEditor, LinesView, emptyLine } from './LinesEditor';
import { buildLines, serialDifference, type LineDraft } from './stockForm';

const STATES = ['DECLARED', 'RECEIVED', 'REJECTED', ''] as const;

/**
 * Returned material (docs/lean-pilot/04 §4.10). An unused return is declared by the technician on the phone (CUSTODY →
 * IN_TRANSIT); here the warehouse officer ticks every serial actually handed over and receives it, or rejects it with a reason
 * (REVERSAL back to the technician's custody, WARNING flag). For a disabled (offboarded) technician, the officer records what
 * was handed back from that custody; nothing is written off.
 */
export function ReturnsPage() {
  const { t, i18n } = useTranslation();
  const session = useSession();
  const warehouse = hasRole(session, 'WAREHOUSE');
  const [params, setParams] = useSearchParams();
  const state = params.get('state') ?? 'DECLARED';
  const { post } = useApi();
  const list = useResource<StockReturn[]>(`/api/v1/stock/returns${state ? `?state=${state}` : ''}`);
  const [ticks, setTicks] = useState<Record<string, string[]>>({});
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);

  async function decide(r: StockReturn, action: 'receive' | 'reject') {
    setProblem(null);
    setBusy(true);
    const body = action === 'receive'
      ? { confirmedSerials: ticks[r.id] ?? [], note: null }
      : { reason: (reasons[r.id] ?? '').trim() || null };
    const res = await post(`/api/v1/stock/returns/${r.id}/${action}`, body);
    setBusy(false);
    if (!res.ok) setProblem(res.problem);
    list.reload();
  }

  function tick(returnId: string, serial: string, on: boolean) {
    const current = ticks[returnId] ?? [];
    setTicks({ ...ticks, [returnId]: on ? [...current, serial] : current.filter((s) => s !== serial) });
  }

  return (
    <section data-testid="stock-returns">
      <h1>{t('returns.title')}</h1>
      <StockNav />
      <p className="hint">{t('returns.hint')}</p>
      <div className="filters">
        {STATES.map((s) => (
          <button key={s || 'all'} type="button" className={s === state ? 'primary' : ''} data-testid={`returns-filter-${s || 'ALL'}`} onClick={() => setParams({ state: s })}>
            {s ? t(`returnState.${s}`) : t('common.all')}
          </button>
        ))}
      </div>
      {problem && <ProblemView problem={problem} />}
      {!list.result ? <p>{t('auth.loading')}</p> : !list.result.ok ? <ProblemView problem={list.result.problem} /> : list.result.data.length === 0 ? <p>{t('common.empty')}</p> : (
        <table className="table" data-testid="returns">
          <thead><tr><th>{t('returns.code')}</th><th>{t('returns.technician')}</th><th>{t('returns.lines')}</th><th>{t('returns.state')}</th><th /></tr></thead>
          <tbody>
            {list.result.data.map((r) => {
              const serials = r.lines.flatMap((l) => l.serials);
              const diff = serialDifference(serials, ticks[r.id] ?? []);
              return (
                <tr key={r.id} data-return-id={r.id} data-code={r.code}>
                  <td><strong>{r.code}</strong><div className="hint">{t(`returnKind.${r.kind}`)} · {formatDateTime(r.declaredAt, i18n.language)}</div></td>
                  <td>{r.technicianName}{r.kind === 'OFFBOARDING' && <div className="hint">{t('returns.recordedBy', { name: r.declaredByName })}</div>}</td>
                  <td><LinesView lines={r.lines} />{r.note && <div className="hint">{r.note}</div>}</td>
                  <td>
                    <span className={`badge return-${r.state}`} data-testid="return-state">{t(`returnState.${r.state}`)}</span>
                    {r.rejectReason && <div className="hint">{r.decidedByName}: {r.rejectReason}</div>}
                    <DocumentFlags flags={r.flags} />
                  </td>
                  <td>
                    {warehouse && r.state === 'DECLARED' && (
                      <div className="panel">
                        {serials.length > 0 && (
                          <div className="serial-ticks" data-testid="return-serial-ticks">
                            <span className="hint">{t('returns.tickSerials')}</span>
                            {serials.map((s) => (
                              <label key={s}><input type="checkbox" data-testid="return-serial" data-serial={s} checked={(ticks[r.id] ?? []).includes(s)}
                                onChange={(e) => tick(r.id, s, e.target.checked)} /> {s}</label>
                            ))}
                          </div>
                        )}
                        <div className="row-actions">
                          <button type="button" className="primary" disabled={busy || diff.missing.length > 0} data-testid="return-receive"
                            onClick={() => void decide(r, 'receive')}>{t('returns.receive')}</button>
                          <input placeholder={t('returns.rejectReason')} value={reasons[r.id] ?? ''} data-testid="return-reject-reason"
                            onChange={(e) => setReasons({ ...reasons, [r.id]: e.target.value })} />
                          <button type="button" disabled={busy} data-testid="return-reject" onClick={() => void decide(r, 'reject')}>{t('returns.reject')}</button>
                        </div>
                      </div>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      {warehouse && <OffboardingReturn onDone={list.reload} />}
    </section>
  );
}

/** The warehouse records what a disabled technician handed back (M5 offboarding). Only their custody can be returned. */
function OffboardingReturn({ onDone }: { onDone: () => void }) {
  const { t, i18n } = useTranslation();
  const { post } = useApi();
  const report = useResource<Reconciliation>('/api/v1/stock/reconciliation');
  const items = useResource<StockItem[]>('/api/v1/stock/items');
  const [userId, setUserId] = useState('');
  const [lines, setLines] = useState<LineDraft[]>([emptyLine()]);
  const [note, setNote] = useState('');
  const [final, setFinal] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [remaining, setRemaining] = useState<CustodyLine[] | null>(null);
  const disabled = report.result?.ok ? report.result.data.custodians.filter((c) => !c.active) : [];
  const holder = disabled.find((c) => c.userId === userId);
  const theirItems = items.result?.ok && holder ? items.result.data.filter((i) => holder.items.some((h) => h.itemId === i.id)) : [];

  async function record() {
    setError(null);
    setProblem(null);
    const filled = lines.filter((l) => l.itemId !== '');
    const built = filled.length === 0 ? { ok: true as const, lines: [] } : buildLines(filled, theirItems);
    if (!built.ok) return setError(built.error);
    const res = await post<{ returnId: string | null; code: string | null; remaining: CustodyLine[] }>('/api/v1/stock/offboarding-returns', {
      technicianId: userId, lines: built.lines, final, note: note.trim() || null,
    });
    if (!res.ok) return setProblem(res.problem);
    setRemaining(res.data.remaining);
    setLines([emptyLine()]);
    setNote('');
    report.reload();
    onDone();
  }

  return (
    <div className="panel" data-testid="offboarding-return">
      <h3>{t('offboarding.title')}</h3>
      <p className="hint">{t('offboarding.hint')}</p>
      {disabled.length === 0 ? <p>{t('offboarding.none')}</p> : (
        <>
          <select value={userId} data-testid="offboarding-user" onChange={(e) => { setUserId(e.target.value); setRemaining(null); }}>
            <option value="">—</option>
            {disabled.map((c) => <option key={c.userId} value={c.userId}>{c.name}</option>)}
          </select>
          {holder && (
            <>
              <ul className="plain" data-testid="offboarding-custody">
                {holder.items.map((h) => (
                  <li key={h.itemId}><code>{h.sku}</code> {i18n.language === 'ar' ? h.nameAr : h.nameEn}: {formatQuantity(h.quantity)} {h.unit}
                    {h.serials.length > 0 && <span className="hint"> · {h.serials.join(', ')}</span>}</li>
                ))}
              </ul>
              <LinesEditor items={theirItems} lines={lines} onChange={setLines} />
              <input placeholder={t('offboarding.note')} value={note} data-testid="offboarding-note" onChange={(e) => setNote(e.target.value)} />
              <label><input type="checkbox" checked={final} data-testid="offboarding-final" onChange={(e) => setFinal(e.target.checked)} /> {t('offboarding.final')}</label>
              {error && <p role="alert">{t(error)}</p>}
              {problem && <ProblemView problem={problem} />}
              <button type="button" className="primary" data-testid="offboarding-submit" onClick={() => void record()}>{t('offboarding.record')}</button>
              {remaining && (
                <p data-testid="offboarding-remaining">
                  {remaining.length === 0 ? t('offboarding.nothingLeft') : t('offboarding.left', { items: remaining.map((r) => `${r.sku} ${formatQuantity(r.quantity)}`).join(', ') })}
                </p>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}
