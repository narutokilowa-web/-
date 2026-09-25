import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { Asset } from '../../shared/types';
import { QrCode } from './QrCode';

/** A6 (105 × 148 mm) label for printing and fixing to the pole. Uses the browser's print dialog. */
export function LabelPage({ organizationName }: { organizationName: string }) {
  const { id } = useParams();
  const { t } = useTranslation();
  const { result } = useResource<Asset>(`/api/v1/assets/${id}`);
  if (!result) return <p>{t('auth.loading')}</p>;
  if (!result.ok || !result.data.tag) return <p role="alert">{t('asset.noTag')}</p>;
  const a = result.data;
  const tag = a.tag!;
  return (
    <div className="label-page">
      <div className="no-print label-toolbar">
        <button type="button" className="primary" onClick={() => window.print()} data-testid="print-now">{t('asset.printNow')}</button>
        <span className="hint">{t('asset.printHint')}</span>
        <Link to={`/assets/${a.id}`} data-testid="back-to-asset">{t('asset.backToAsset')}</Link>
      </div>
      <article className="label" data-testid="label">
        <header>{organizationName}</header>
        <QrCode payload={tag.qrPayload} size={260} testId="label-qr" />
        <p className="label-asset" data-testid="label-asset-code">{a.assetCode}</p>
        <p className="label-code" data-testid="label-human-code">{tag.humanCode}</p>
        <footer>{t('asset.labelFooter')}</footer>
      </article>
    </div>
  );
}
