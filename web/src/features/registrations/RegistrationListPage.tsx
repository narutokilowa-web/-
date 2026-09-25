import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { Registration } from '../../shared/types';
import { formatDateTime } from '../../shared/format';

const STATES = ['', 'DRAFT', 'SUBMITTED', 'RETURNED', 'APPROVED', 'REJECTED', 'DISCARDED'];

export function RegistrationListPage() {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const state = params.get('state') ?? '';
  const { result } = useResource<Registration[]>(`/api/v1/registrations${state ? `?state=${state}` : ''}`);

  return (
    <section>
      <h1>{state === 'SUBMITTED' ? t('registration.queueTitle') : t('registration.listTitle')}</h1>
      <label className="inline">
        <span>{t('registration.filterState')}</span>
        <select value={state} onChange={(e) => setParams(e.target.value ? { state: e.target.value } : {})} data-testid="state-filter">
          {STATES.map((s) => (
            <option key={s} value={s}>{s ? t(`registrationState.${s}`) : t('common.all')}</option>
          ))}
        </select>
      </label>
      {!result && <p>{t('auth.loading')}</p>}
      {result && !result.ok && <p role="alert">{result.problem.code}</p>}
      {result?.ok && result.data.length === 0 && <p data-testid="empty">{t('common.empty')}</p>}
      {result?.ok && result.data.length > 0 && (
        <table className="table" data-testid="registrations">
          <thead>
            <tr>
              <th>{t('registration.state')}</th>
              <th>{t('field.addressText')}</th>
              <th>{t('registration.registrar')}</th>
              <th>{t('registration.createdAt')}</th>
              <th>{t('asset.code')}</th>
            </tr>
          </thead>
          <tbody>
            {result.data.map((r) => (
              <tr key={r.id} data-registration-id={r.id}>
                <td><Link to={`/registrations/${r.id}`}><span className={`badge state-${r.state}`}>{t(`registrationState.${r.state}`)}</span></Link></td>
                <td><Link to={`/registrations/${r.id}`}>{r.attributes.addressText ?? `${r.attributes.latitude}, ${r.attributes.longitude}`}</Link></td>
                <td>{r.registrarName}</td>
                <td>{formatDateTime(r.createdAt, i18n.language)}</td>
                <td>{r.assetCode ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
