import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { CoreReturn } from '../../shared/types';
import { display, formatDateTime } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { hasRole, useSession } from '../../app/MeContext';
import { EvidenceImage } from '../workorders/EvidenceImage';
import { DocumentFlags, StockNav } from './StockNav';

const STATES = ['DECLARED_WITH_PART', 'DECLARED_UNRECOVERABLE', 'OPEN', 'RECEIVED', 'WAIVED', ''] as const;

/**
 * Failed parts (docs/lean-pilot/04 §4.11). The warehouse receives the part the technician declared "with part" and reads its
 * serial: it is checked against the serial known to be on the light (a mismatch is recorded and flagged CRITICAL for review),
 * or records it as not presented. A supervisor other than the technician may waive an unrecoverable part (always flagged INFO).
 */
export function CoreReturnsPage() {
  const { t, i18n } = useTranslation();
  const session = useSession();
  const warehouse = hasRole(session, 'WAREHOUSE');
  const supervisor = hasRole(session, 'SUPERVISOR');
  const [params, setParams] = useSearchParams();
  const state = params.get('state') ?? (supervisor && !warehouse ? 'DECLARED_UNRECOVERABLE' : 'DECLARED_WITH_PART');
  const { post } = useApi();
  const list = useResource<CoreReturn[]>(`/api/v1/stock/core-returns${state ? `?state=${state}` : ''}`);
  const [serials, setSerials] = useState<Record<string, string>>({});
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);

  async function act(c: CoreReturn, path: 'receive' | 'waive', body: object) {
    setProblem(null);
    setBusy(true);
    const res = await post(`/api/v1/stock/core-returns/${c.id}/${path}`, body);
    setBusy(false);
    if (!res.ok) setProblem(res.problem);
    list.reload();
  }

  const reason = (id: string) => (reasons[id] ?? '').trim() || null;

  return (
    <section data-testid="core-returns">
      <h1>{t('cores.title')}</h1>
      {(warehouse || hasRole(session, 'AUDITOR') || supervisor) && <StockNav />}
      <p className="hint">{t('cores.hint')}</p>
      <div className="filters">
        {STATES.map((s) => (
          <button key={s || 'all'} type="button" className={s === state ? 'primary' : ''} data-testid={`cores-filter-${s || 'ALL'}`} onClick={() => setParams({ state: s })}>
            {s ? t(`coreState.${s}`) : t('common.all')}
          </button>
        ))}
      </div>
      {problem && <ProblemView problem={problem} />}
      {!list.result ? <p>{t('auth.loading')}</p> : !list.result.ok ? <ProblemView problem={list.result.problem} /> : list.result.data.length === 0 ? <p>{t('common.empty')}</p> : (
        <table className="table" data-testid="cores">
          <thead><tr><th>{t('cores.code')}</th><th>{t('cores.part')}</th><th>{t('cores.serials')}</th><th>{t('cores.state')}</th><th /></tr></thead>
          <tbody>
            {list.result.data.map((c) => (
              <tr key={c.id} data-core-id={c.id} data-code={c.code}>
                <td>
                  <strong>{c.code}</strong>
                  <div className="hint"><Link to={`/work-orders/${c.workOrderId}`}>{c.workOrderCode}</Link> · <Link to={`/assets/${c.assetId}`}>{display(c.assetCode)}</Link></div>
                  <div className="hint">{c.technicianName} · {formatDateTime(c.declaredAt ?? c.createdAt, i18n.language)}</div>
                </td>
                <td><code>{c.sku}</code> {i18n.language === 'ar' ? c.nameAr : c.nameEn} × {c.quantity}</td>
                <td className="hint">
                  <div>{t('cores.expected', { serial: display(c.expectedSerial) })}</div>
                  <div>{t('cores.declared', { serial: display(c.declaredSerial) })}</div>
                  <div>{t('cores.installed', { serial: display(c.installedSerial) })}</div>
                  {c.receivedSerial && <div data-testid="core-received-serial">{t('cores.received', { serial: c.receivedSerial })}</div>}
                </td>
                <td>
                  <span className={`badge core-${c.state}`} data-testid="core-state">{t(`coreState.${c.state}`)}</span>
                  {c.serialMismatch && <span className="badge severity-CRITICAL" data-testid="core-mismatch">{t('cores.mismatch')}</span>}
                  {c.overdue && <span className="badge warn">{t('cores.overdue')}</span>}
                  {c.declaredReason && <div className="hint">{c.declaredReason}</div>}
                  {c.notPresentedReason && <div className="hint">{c.receivedByName}: {c.notPresentedReason}</div>}
                  {c.waiveReason && <div className="hint">{c.waivedByName}: {c.waiveReason}</div>}
                  <DocumentFlags flags={c.flags} />
                  {supervisor && c.evidenceId && <EvidenceImage evidenceId={c.evidenceId} alt={t('cores.photo')} />}
                </td>
                <td>
                  {warehouse && c.state === 'DECLARED_WITH_PART' && (
                    <div className="row-actions">
                      {c.serialTracked && (
                        <input placeholder={t('cores.readSerial')} value={serials[c.id] ?? ''} data-testid="core-serial"
                          onChange={(e) => setSerials({ ...serials, [c.id]: e.target.value })} />
                      )}
                      <button type="button" className="primary" disabled={busy} data-testid="core-receive"
                        onClick={() => void act(c, 'receive', { outcome: 'RECEIVED', serial: (serials[c.id] ?? '').trim() || null })}>{t('cores.receive')}</button>
                      <input placeholder={t('cores.notPresentedReason')} value={reasons[c.id] ?? ''} data-testid="core-reason"
                        onChange={(e) => setReasons({ ...reasons, [c.id]: e.target.value })} />
                      <button type="button" disabled={busy} data-testid="core-not-presented"
                        onClick={() => void act(c, 'receive', { outcome: 'NOT_PRESENTED', reason: reason(c.id) })}>{t('cores.notPresented')}</button>
                    </div>
                  )}
                  {supervisor && c.state === 'DECLARED_UNRECOVERABLE' && c.technicianId !== session.me.userId && (
                    <div className="row-actions">
                      <input placeholder={t('cores.waiveReason')} value={reasons[c.id] ?? ''} data-testid="core-waive-reason"
                        onChange={(e) => setReasons({ ...reasons, [c.id]: e.target.value })} />
                      <button type="button" disabled={busy} data-testid="core-waive" onClick={() => void act(c, 'waive', { reason: reason(c.id) })}>{t('cores.waive')}</button>
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
