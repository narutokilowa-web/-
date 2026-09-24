import { useAuth } from 'react-oidc-context';
import { useTranslation } from 'react-i18next';
import { MePage } from './MePage';

export function App({ environment }: { environment: string }) {
  const auth = useAuth();
  const { t, i18n } = useTranslation();
  const otherLanguage = i18n.language === 'ar' ? 'en' : 'ar';

  return (
    <main style={{ fontFamily: 'system-ui, sans-serif', maxWidth: 720, margin: '2rem auto', padding: '0 16px' }}>
      <div data-testid="env-banner" style={{ background: '#fde68a', padding: '4px 8px', borderRadius: 4 }}>
        {t('env.banner', { env: environment })}
      </div>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
        <div>
          <h1>{t('app.title')}</h1>
          <p>{t('app.subtitle')}</p>
        </div>
        <button type="button" onClick={() => void i18n.changeLanguage(otherLanguage)}>
          {t('language.switch')}
        </button>
      </header>

      {auth.isLoading && <p>{t('auth.loading')}</p>}
      {auth.error && <p role="alert">{t('auth.error')}</p>}

      {!auth.isLoading && !auth.isAuthenticated && (
        <button type="button" data-testid="login" onClick={() => void auth.signinRedirect()}>
          {t('auth.login')}
        </button>
      )}

      {auth.isAuthenticated && auth.user && (
        <>
          <MePage accessToken={auth.user.access_token} />
          <button type="button" data-testid="logout" onClick={() => void auth.signoutRedirect()}>
            {t('auth.logout')}
          </button>
        </>
      )}
    </main>
  );
}
