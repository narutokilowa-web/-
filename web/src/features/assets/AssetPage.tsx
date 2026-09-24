import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { Asset, HistoryEntry } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { AttributesView } from './AttributesView';
import { HistoryTimeline } from './HistoryTimeline';
import { QrCode } from './QrCode';

export function AssetPage() {
  const { id } = useParams();
  const { t, i18n } = useTranslation();
  const asset = useResource<Asset>(`/api/v1/assets/${id}`);
  const history = useResource<HistoryEntry[]>(`/api/v1/assets/${id}/history`);

  if (!asset.result) return <p>{t('auth.loading')}</p>;
  if (!asset.result.ok) return <p role="alert">{t(`error.${asset.result.problem.code}`, { defaultValue: asset.result.problem.code })}</p>;
  const a = asset.result.data;

  return (
    <section>
      <h1>
        <span data-testid="asset-code">{a.assetCode ?? t('asset.noCode')}</span>{' '}
        <span className={`badge state-${a.lifecycleState}`} data-testid="lifecycle-state">{t(`lifecycle.${a.lifecycleState}`)}</span>{' '}
        {a.operationalStatus && <span className={`badge status-${a.operationalStatus}`} data-testid="operational-status">{t(`status.${a.operationalStatus}`)}</span>}
      </h1>

      <div className="asset-top">
        {a.tag && (
          <div className="tag-box" data-testid="tag-box">
            <QrCode payload={a.tag.qrPayload} />
            <p className="human-code" data-testid="human-code">{a.tag.humanCode}</p>
            <Link className="button primary" to={`/assets/${a.id}/label`} data-testid="print-label">{t('asset.printLabel')}</Link>
          </div>
        )}
        <AttributesView attributes={a.attributes} />
      </div>

      <h2>{t('asset.versions')}</h2>
      <table className="table" data-testid="versions">
        <thead><tr><th>#</th><th>{t('asset.approvedBy')}</th><th>{t('asset.approvedAt')}</th><th>{t('asset.snapshotHash')}</th></tr></thead>
        <tbody>
          {a.versions.map((v) => (
            <tr key={v.versionNo}>
              <td>{v.versionNo}</td>
              <td>{v.approvedByName}</td>
              <td>{formatDateTime(v.approvedAt, i18n.language)}</td>
              <td><code title={v.snapshotHash}>{v.snapshotHash.slice(0, 16)}…</code></td>
            </tr>
          ))}
        </tbody>
      </table>
      {a.registrationId && <p><Link to={`/registrations/${a.registrationId}`}>{t('asset.viewRegistration')}</Link></p>}

      <h2>{t('asset.history')}</h2>
      <p className="hint">{t('asset.historyHint')}</p>
      {history.result?.ok ? <HistoryTimeline entries={history.result.data} /> : <p>{t('auth.loading')}</p>}
    </section>
  );
}
