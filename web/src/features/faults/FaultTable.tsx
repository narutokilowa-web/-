import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import type { Fault } from '../../shared/types';
import { formatDateTime } from '../../shared/format';

export function FaultTable({ faults, showAsset = true }: { faults: Fault[]; showAsset?: boolean }) {
  const { t, i18n } = useTranslation();
  return (
    <table className="table" data-testid="faults">
      <thead>
        <tr>
          {showAsset && <th>{t('asset.code')}</th>}
          <th>{t('fault.category')}</th><th>{t('fault.state')}</th><th>{t('fault.power')}</th><th>{t('fault.reportedBy')}</th><th>{t('fault.reportedAt')}</th><th />
        </tr>
      </thead>
      <tbody>
        {faults.map((f) => (
          <tr key={f.id} data-fault-id={f.id}>
            {showAsset && <td>{f.assetCode} <span className="hint">{f.assetAddress}</span></td>}
            <td>{t(`faultCategory.${f.category}`)}{f.confirmationCount > 0 && <span className="hint"> · {t('fault.confirmations', { count: f.confirmationCount })}</span>}</td>
            <td><span className={`badge fault-${f.state}`}>{t(`faultState.${f.state}`)}</span></td>
            <td>{t(`power.${f.powerAtInspection}`)}{f.reportedWithoutPower && <span className="badge warn" data-testid="no-power-badge"> {t('fault.noPowerBadge')}</span>}</td>
            <td>{f.reportedByName}</td>
            <td>{formatDateTime(f.reportedAt, i18n.language)}</td>
            <td><Link to={`/faults/${f.id}`} data-testid="fault-link">{t('common.open')}</Link></td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
