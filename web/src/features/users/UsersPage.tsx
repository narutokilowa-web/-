import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { UserAccount } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';
import { useSession } from '../../app/MeContext';

/**
 * Admin: offboarding (docs/lean-pilot/05 §5.6). Disabling an employee revokes every device and offline grant, ends the
 * Keycloak sessions (after commit, retried) and queues their open work orders for reassignment — all server-side.
 * Nothing on the employee's personal phone is read or deleted.
 */
export function UsersPage() {
  const { t, i18n } = useTranslation();
  const session = useSession();
  const { post } = useApi();
  const { result, reload } = useResource<UserAccount[]>('/api/v1/users');
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [confirming, setConfirming] = useState<string | null>(null);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);

  async function decide(id: string, action: 'disable' | 'enable') {
    setBusy(true);
    setProblem(null);
    const reason = (reasons[id] ?? '').trim();
    const res = await post(`/api/v1/users/${id}/${action}`, { reason: reason === '' ? null : reason });
    setBusy(false);
    setConfirming(null);
    if (!res.ok) setProblem(res.problem);
    else {
      setReasons({ ...reasons, [id]: '' });
      reload();
    }
  }

  return (
    <section>
      <h1>{t('users.title')}</h1>
      <p className="hint">{t('users.hint')}</p>
      {problem && <ProblemView problem={problem} />}
      {!result ? <p>{t('auth.loading')}</p> : !result.ok ? <ProblemView problem={result.problem} /> : (
        <table className="table" data-testid="users">
          <thead>
            <tr><th>{t('users.user')}</th><th>{t('users.roles')}</th><th>{t('users.status')}</th><th>{t('users.devices')}</th><th>{t('users.identityProvider')}</th><th /></tr>
          </thead>
          <tbody>
            {result.data.map((u) => (
              <tr key={u.id} data-user-id={u.id} data-username={u.username}>
                <td>{u.displayName} <span className="hint">({u.username})</span></td>
                <td>{u.roles.map((r) => t(`roles.${r}`, { defaultValue: r })).join('، ')}</td>
                <td>
                  <span className={`badge user-${u.status}`} data-testid="user-status">{t(`userStatus.${u.status}`)}</span>
                  {u.disabledAt && (
                    <div className="hint" data-testid="user-disabled-by">
                      {t('users.disabledBy', { name: u.disabledByName ?? '?', at: formatDateTime(u.disabledAt, i18n.language) })}: {u.disableReason}
                    </div>
                  )}
                </td>
                <td>
                  {u.devices.length === 0 ? '—' : u.devices.map((d) => (
                    <div key={d.id} className="hint" data-testid="user-device" data-status={d.status}>
                      {d.model} · {t(`deviceStatus.${d.status}`)}{d.revokeReason ? ` (${t(`deviceReason.${d.revokeReason}`, { defaultValue: d.revokeReason })})` : ''}
                    </div>
                  ))}
                </td>
                <td>
                  {u.idpPending === 0 ? <span className="hint">{t('users.idpInSync')}</span> : (
                    <span className={u.idpAlert ? 'badge alert' : 'hint'} data-testid="user-idp" title={u.idpLastError ?? undefined}>
                      {u.idpAlert ? t('users.idpAlert', { count: u.idpPending }) : t('users.idpPending', { count: u.idpPending })}
                    </span>
                  )}
                </td>
                <td>
                  {u.id !== session.me.userId && (
                    <div className="row-actions">
                      <input placeholder={t('users.reason')} value={reasons[u.id] ?? ''} data-testid="user-reason"
                        onChange={(e) => setReasons({ ...reasons, [u.id]: e.target.value })} />
                      {u.status === 'ACTIVE' && confirming !== u.id && (
                        <button type="button" className="danger" disabled={busy} data-testid="user-disable" onClick={() => setConfirming(u.id)}>{t('users.disable')}</button>
                      )}
                      {u.status === 'ACTIVE' && confirming === u.id && (
                        <>
                          <span className="hint">{t('users.confirmDisable')}</span>
                          <button type="button" className="danger" disabled={busy} data-testid="user-disable-confirm" onClick={() => void decide(u.id, 'disable')}>{t('users.confirm')}</button>
                          <button type="button" disabled={busy} onClick={() => setConfirming(null)}>{t('users.cancel')}</button>
                        </>
                      )}
                      {u.status === 'DISABLED' && (
                        <button type="button" disabled={busy} data-testid="user-enable" onClick={() => void decide(u.id, 'enable')}>{t('users.enable')}</button>
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
