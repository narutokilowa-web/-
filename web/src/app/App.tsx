import { useEffect, useState } from 'react';
import { useAuth } from 'react-oidc-context';
import { useTranslation } from 'react-i18next';
import { BrowserRouter, Route, Routes } from 'react-router';
import { fetchMe, type MeResult } from '../shared/api';
import type { RuntimeConfig } from '../shared/config';
import { SessionContext } from './MeContext';
import { Layout } from './Layout';
import { HomePage } from './HomePage';
import { RegistrationListPage } from '../features/registrations/RegistrationListPage';
import { RegistrationFormPage } from '../features/registrations/RegistrationFormPage';
import { RegistrationDetailPage } from '../features/registrations/RegistrationDetailPage';
import { AssetListPage } from '../features/assets/AssetListPage';
import { AssetPage } from '../features/assets/AssetPage';
import { LabelPage } from '../features/assets/LabelPage';
import { TagResolvePage } from '../features/assets/TagResolvePage';

export const RETURN_PATH_KEY = 'mfop.returnPath';

export function App({ config }: { config: RuntimeConfig }) {
  const auth = useAuth();
  const { t } = useTranslation();
  const [me, setMe] = useState<MeResult | null>(null);
  const token = auth.user?.access_token;

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchMe(token).then((r) => {
      if (!cancelled) setMe(r);
    });
    return () => {
      cancelled = true;
    };
  }, [token]);

  if (auth.isLoading) return <p className="page">{t('auth.loading')}</p>;

  if (!auth.isAuthenticated || !token) {
    return (
      <Layout environment={config.environment}>
        {auth.error && <p role="alert">{t('auth.error')}</p>}
        <button
          type="button"
          className="primary"
          data-testid="login"
          onClick={() => {
            try {
              sessionStorage.setItem(RETURN_PATH_KEY, window.location.pathname);
            } catch {
              /* storage unavailable: return to home */
            }
            void auth.signinRedirect();
          }}
        >
          {t('auth.login')}
        </button>
      </Layout>
    );
  }

  if (me === null) return <p className="page">{t('auth.loading')}</p>;
  if (me.kind !== 'ok') {
    return (
      <Layout environment={config.environment}>
        <p role="alert">{me.kind === 'forbidden' ? t(`me.${me.code}`, { defaultValue: me.code }) : t('me.loadError', { status: me.status })}</p>
      </Layout>
    );
  }

  return (
    <SessionContext.Provider value={{ token, me: me.user }}>
      <BrowserRouter>
        <Routes>
          <Route path="/assets/:id/label" element={<LabelPage organizationName={config.organizationNameAr} />} />
          <Route
            path="*"
            element={
              <Layout environment={config.environment} onLogout={() => void auth.signoutRedirect()}>
                <Routes>
                  <Route path="/" element={<HomePage />} />
                  <Route path="/registrations" element={<RegistrationListPage />} />
                  <Route path="/registrations/new" element={<RegistrationFormPage />} />
                  <Route path="/registrations/:id" element={<RegistrationDetailPage />} />
                  <Route path="/registrations/:id/edit" element={<RegistrationFormPage />} />
                  <Route path="/assets" element={<AssetListPage />} />
                  <Route path="/assets/:id" element={<AssetPage />} />
                  <Route path="/t/:token" element={<TagResolvePage />} />
                  <Route path="*" element={<p>{t('common.notFound')}</p>} />
                </Routes>
              </Layout>
            }
          />
        </Routes>
      </BrowserRouter>
    </SessionContext.Provider>
  );
}
