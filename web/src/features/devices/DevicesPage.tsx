import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { Device } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';

const FILTERS = ['PENDING_APPROVAL', 'ACTIVE', 'REVOKED', 'REJECTED', ''] as const;

/**
 * Supervisor/admin: approve or reject phones waiting for approval, revoke active ones.
 * Revocation is server-side only (BYOD): the phone is rejected on its next request; nothing on it is touched.
 */
export function DevicesPage() {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const status = params.get('status') ?? 'PENDING_APPROVAL';
  const { post } = useApi();
  const { result, reload } = useResource<Device[]>(`/api/v1/devices${status ? `?status=${status}` : ''}`);
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);

  async function decide(id: string, action: 'approve' | 'reject' | 'revoke') {
    setBusy(true);
    setProblem(null);
    const reason = (reasons[id] ?? '').trim();
    const res = await post(`/api/v1/devices/${id}/${action}`, { reason: reason === '' ? null : reason });
    setBusy(false);
    if (!res.ok) setProblem(res.problem);
    else reload();
  }

  return (
    <section>
      <h1>{t('device.title')}</h1>
      <p className="hint">{t('device.byodHint')}</p>
      <div className="filters">
        {FILTERS.map((f) => (
          <button key={f || 'all'} type="button" className={f === status ? 'primary' : ''} data-testid={`device-filter-${f || 'ALL'}`}
            onClick={() => setParams(f ? { status: f } : { status: '' })}>
            {f ? t(`deviceStatus.${f}`) : t('common.all')}
          </button>
        ))}
      </div>
      {problem && <ProblemView problem={problem} />}
      {!result ? <p>{t('auth.loading')}</p> : !result.ok ? <ProblemView problem={result.problem} /> : result.data.length === 0 ? <p>{t('common.empty')}</p> : (
        <table className="table" data-testid="devices">
          <thead>
            <tr>
              <th>{t('device.user')}</th><th>{t('device.model')}</th><th>{t('device.status')}</th><th>{t('device.requestedAt')}</th>
              <th>{t('device.lastSync')}</th><th>{t('device.decision')}</th><th />
            </tr>
          </thead>
          <tbody>
            {result.data.map((d) => (
              <tr key={d.id} data-device-id={d.id} data-username={d.username}>
                <td>{d.userDisplayName} <span className="hint">({d.username})</span></td>
                <td>{d.model} · {d.platform} {d.osVersion} · v{d.appVersion}</td>
                <td><span className={`badge device-${d.status}`} data-testid="device-status">{t(`deviceStatus.${d.status}`)}</span></td>
                <td>{formatDateTime(d.requestedAt, i18n.language)}</td>
                <td>
                  {formatDateTime(d.lastPullAt ?? d.lastPushAt, i18n.language)}
                  {d.lastReportedPendingCommands ? <span className="hint"> · {t('device.pending', { count: d.lastReportedPendingCommands })}</span> : null}
                </td>
                <td className="hint">
                  {d.revokeReason ? `${t('device.revokedBy', { name: d.revokedByName ?? t('roles.SYSTEM') })}: ${t(`deviceReason.${d.revokeReason}`, { defaultValue: d.revokeReason })}` : null}
                  {!d.revokeReason && d.decidedAt ? `${d.decidedByName ?? t('roles.SYSTEM')}${d.decisionReason ? `: ${t(`deviceReason.${d.decisionReason}`, { defaultValue: d.decisionReason })}` : ''}` : null}
                </td>
                <td>
                  {(d.status === 'PENDING_APPROVAL' || d.status === 'ACTIVE') && (
                    <div className="row-actions">
                      <input placeholder={t('device.reason')} value={reasons[d.id] ?? ''} data-testid="device-reason"
                        onChange={(e) => setReasons({ ...reasons, [d.id]: e.target.value })} />
                      {d.status === 'PENDING_APPROVAL' && (
                        <>
                          <button type="button" className="primary" disabled={busy} data-testid="device-approve" onClick={() => void decide(d.id, 'approve')}>{t('device.approve')}</button>
                          <button type="button" disabled={busy} data-testid="device-reject" onClick={() => void decide(d.id, 'reject')}>{t('device.reject')}</button>
                        </>
                      )}
                      {d.status === 'ACTIVE' && (
                        <button type="button" className="danger" disabled={busy} data-testid="device-revoke" onClick={() => void decide(d.id, 'revoke')}>{t('device.revoke')}</button>
                      )}
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
