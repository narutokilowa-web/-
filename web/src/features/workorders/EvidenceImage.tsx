import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiBlobUrl } from '../../shared/api';
import { useSession } from '../../app/MeContext';

/**
 * An EXIF-free server preview of a photo, fetched with the bearer token (never by plain URL), so only roles allowed by
 * evidence.preview see it. Click opens the larger preview.
 */
export function EvidenceImage({ evidenceId, alt }: { evidenceId: string; alt: string }) {
  const { token } = useSession();
  const { t } = useTranslation();
  const [thumb, setThumb] = useState<string | null>(null);
  const [large, setLarge] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let url: string | null = null;
    let cancelled = false;
    void apiBlobUrl(token, `/api/v1/evidence/${evidenceId}/preview?size=thumb`).then((r) => {
      if (cancelled) {
        if (r.ok) URL.revokeObjectURL(r.url);
        return;
      }
      if (r.ok) {
        url = r.url;
        setThumb(r.url);
      } else setFailed(true);
    });
    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [token, evidenceId]);

  useEffect(() => () => {
    if (large) URL.revokeObjectURL(large);
  }, [large]);

  async function openLarge() {
    const r = await apiBlobUrl(token, `/api/v1/evidence/${evidenceId}/preview`);
    if (r.ok) setLarge(r.url);
  }

  if (failed) return <div className="evidence-placeholder">{t('review.previewUnavailable')}</div>;
  if (!thumb) return <div className="evidence-placeholder">{t('auth.loading')}</div>;
  return (
    <>
      <button type="button" className="evidence-thumb" onClick={() => void openLarge()} aria-label={t('review.openPhoto')}>
        <img src={thumb} alt={alt} data-testid="evidence-thumb" />
      </button>
      {large && (
        <div className="lightbox" role="dialog" aria-modal="true" onClick={() => setLarge(null)}>
          <img src={large} alt={alt} />
          <button type="button" onClick={() => setLarge(null)}>{t('common.close')}</button>
        </div>
      )}
    </>
  );
}
