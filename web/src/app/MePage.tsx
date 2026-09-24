import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { fetchMe, type MeResult } from '../shared/api';
import { RoleList } from './RoleList';

export function MePage({ accessToken }: { accessToken: string }) {
  const { t } = useTranslation();
  const [result, setResult] = useState<MeResult | null>(null);

  useEffect(() => {
    let cancelled = false;
    void fetchMe(accessToken).then((r) => {
      if (!cancelled) setResult(r);
    });
    return () => {
      cancelled = true;
    };
  }, [accessToken]);

  if (result === null) return <p>{t('auth.loading')}</p>;
  if (result.kind === 'forbidden') return <p role="alert">{t(`me.${result.code}`, { defaultValue: result.code })}</p>;
  if (result.kind === 'error') return <p role="alert">{t('me.loadError', { status: result.status })}</p>;

  const { user } = result;
  return (
    <section data-testid="me">
      <h2>{t('me.welcome', { name: user.displayName })}</h2>
      <p>
        {t('me.username')}: <strong data-testid="username">{user.username}</strong>
      </p>
      <h3>{t('me.roles')}</h3>
      <RoleList roles={user.roles} />
      <p data-testid="roles-source">{t('me.rolesSource')}</p>
    </section>
  );
}
