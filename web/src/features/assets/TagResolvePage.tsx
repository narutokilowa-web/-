import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate, useParams } from 'react-router';
import { useApi } from '../../shared/useApi';

/** Target of the QR URL (/t/CODE.CHECK): resolves the tag and opens the asset. */
export function TagResolvePage() {
  const { token } = useParams();
  const { t } = useTranslation();
  const { get } = useApi();
  const [assetId, setAssetId] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    void get<{ assetId: string }>(`/api/v1/tags/${encodeURIComponent(token ?? '')}`).then((r) => {
      if (r.ok) setAssetId(r.data.assetId);
      else setFailed(true);
    });
  }, [get, token]);

  if (assetId) return <Navigate to={`/assets/${assetId}`} replace />;
  if (failed) return <p role="alert" data-testid="tag-not-found">{t('error.TAG_NOT_FOUND')}</p>;
  return <p>{t('auth.loading')}</p>;
}
