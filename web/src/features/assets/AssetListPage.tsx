import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { AssetListItem } from '../../shared/types';

export function AssetListPage() {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const [submitted, setSubmitted] = useState('');
  const { result } = useResource<AssetListItem[]>(`/api/v1/assets${submitted ? `?q=${encodeURIComponent(submitted)}` : ''}`);
  return (
    <section>
      <h1>{t('asset.listTitle')}</h1>
      <form className="inline" onSubmit={(e) => { e.preventDefault(); setSubmitted(query); }}>
        <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('asset.searchPlaceholder')} data-testid="asset-search" />
        <button type="submit">{t('common.search')}</button>
      </form>
      {result?.ok && result.data.length === 0 && <p>{t('common.empty')}</p>}
      {result?.ok && result.data.length > 0 && (
        <table className="table" data-testid="assets">
          <thead>
            <tr><th>{t('asset.code')}</th><th>{t('asset.operationalStatus')}</th><th>{t('field.powerSource')}</th><th>{t('field.addressText')}</th></tr>
          </thead>
          <tbody>
            {result.data.map((a) => (
              <tr key={a.id}>
                <td><Link to={`/assets/${a.id}`}>{a.assetCode}</Link></td>
                <td>{a.operationalStatus ? t(`status.${a.operationalStatus}`) : '—'}</td>
                <td>{t(`powerSource.${a.powerSource}`)}</td>
                <td>{a.addressText ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
