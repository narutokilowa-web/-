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
import { DevicesPage } from '../features/devices/DevicesPage';
import { FaultListPage } from '../features/faults/FaultListPage';
import { FaultDetailPage } from '../features/faults/FaultDetailPage';
import { WorkOrderListPage } from '../features/workorders/WorkOrderListPage';
import { WorkOrderDetailPage } from '../features/workorders/WorkOrderDetailPage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { AuditSearchPage } from '../features/audit/AuditSearchPage';
import { StockBalancesPage } from '../features/stock/StockBalancesPage';
import { IssuesPage } from '../features/stock/IssuesPage';
import { ReturnsPage } from '../features/stock/ReturnsPage';
import { CoreReturnsPage } from '../features/stock/CoreReturnsPage';
import { ReconciliationPage } from '../features/stock/ReconciliationPage';
import { StockHistoryPage } from '../features/stock/StockHistoryPage';
import { RuleSettingsPage } from '../features/settings/RuleSettingsPage';
import { UsersPage } from '../features/users/UsersPage';
import { QuarantinePage } from '../features/quarantine/QuarantinePage';
import { NeedsReassignmentPage } from '../features/workorders/NeedsReassignmentPage';

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
      <Layout environment={config.environment} storageMode={config.storageMode}>
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
      <Layout environment={config.environment} storageMode={config.storageMode}>
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
              <Layout environment={config.environment} storageMode={config.storageMode} onLogout={() => void auth.signoutRedirect()}>
                <Routes>
                  <Route path="/" element={<HomePage />} />
                  <Route path="/registrations" element={<RegistrationListPage />} />
                  <Route path="/registrations/new" element={<RegistrationFormPage />} />
                  <Route path="/registrations/:id" element={<RegistrationDetailPage />} />
                  <Route path="/registrations/:id/edit" element={<RegistrationFormPage />} />
                  <Route path="/assets" element={<AssetListPage />} />
                  <Route path="/assets/:id" element={<AssetPage />} />
                  <Route path="/t/:token" element={<TagResolvePage />} />
                  <Route path="/devices" element={<DevicesPage />} />
                  <Route path="/faults" element={<FaultListPage />} />
                  <Route path="/faults/:id" element={<FaultDetailPage />} />
                  <Route path="/work-orders" element={<WorkOrderListPage />} />
                  <Route path="/work-orders/needs-reassignment" element={<NeedsReassignmentPage />} />
                  <Route path="/work-orders/:id" element={<WorkOrderDetailPage />} />
                  <Route path="/dashboard" element={<DashboardPage />} />
                  <Route path="/audit" element={<AuditSearchPage />} />
                  <Route path="/stock" element={<StockBalancesPage />} />
                  <Route path="/stock/issues" element={<IssuesPage />} />
                  <Route path="/stock/returns" element={<ReturnsPage />} />
                  <Route path="/stock/core-returns" element={<CoreReturnsPage />} />
                  <Route path="/stock/reconciliation" element={<ReconciliationPage />} />
                  <Route path="/stock/history" element={<StockHistoryPage />} />
                  <Route path="/settings/rules" element={<RuleSettingsPage />} />
                  <Route path="/users" element={<UsersPage />} />
                  <Route path="/sync/quarantine" element={<QuarantinePage />} />
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
