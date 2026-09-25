import type { ReactNode } from 'react';
import { useContext } from 'react';
import { useTranslation } from 'react-i18next';
import { NavLink } from 'react-router';
import { SessionContext, hasRole } from './MeContext';
import { RoleList } from './RoleList';

export function Layout({ environment, storageMode = 'local', onLogout, children }: { environment: string; storageMode?: string; onLogout?: () => void; children: ReactNode }) {
  const { t, i18n } = useTranslation();
  const session = useContext(SessionContext);
  const otherLanguage = i18n.language === 'ar' ? 'en' : 'ar';

  return (
    <div className="app">
      <div className="env-banner" data-testid="env-banner">
        {t('env.banner', { env: environment })}{storageMode === 'local' && <> · <span data-testid="storage-banner">{t('env.localStorage')}</span></>}
      </div>
      <header className="topbar">
        <strong>{t('app.title')}</strong>
        {session && (
          <nav className="nav">
            <NavLink to="/">{t('nav.home')}</NavLink>
            {hasRole(session, 'REGISTRAR', 'APPROVER', 'AUDITOR') && <NavLink to="/registrations" end>{t('nav.registrations')}</NavLink>}
            {hasRole(session, 'REGISTRAR') && <NavLink to="/registrations/new" data-testid="nav-new-registration">{t('nav.newRegistration')}</NavLink>}
            {hasRole(session, 'TECHNICIAN', 'INSPECTOR', 'SUPERVISOR', 'REGISTRAR', 'APPROVER', 'AUDITOR') && <NavLink to="/assets">{t('nav.assets')}</NavLink>}
            {hasRole(session, 'SUPERVISOR', 'AUDITOR') && <NavLink to="/dashboard" data-testid="nav-dashboard">{t('nav.dashboard')}</NavLink>}
            {hasRole(session, 'SUPERVISOR', 'AUDITOR') && <NavLink to="/faults" data-testid="nav-faults">{t('nav.faults')}</NavLink>}
            {hasRole(session, 'SUPERVISOR', 'AUDITOR') && <NavLink to="/work-orders" data-testid="nav-work-orders">{t('nav.workOrders')}</NavLink>}
            {hasRole(session, 'SUPERVISOR', 'ADMIN') && <NavLink to="/devices" data-testid="nav-devices">{t('nav.devices')}</NavLink>}
            {hasRole(session, 'WAREHOUSE', 'SUPERVISOR', 'AUDITOR') && <NavLink to="/stock" data-testid="nav-stock">{t('nav.stock')}</NavLink>}
            {hasRole(session, 'AUDITOR') && <NavLink to="/audit" data-testid="nav-audit">{t('nav.audit')}</NavLink>}
            {hasRole(session, 'ADMIN') && <NavLink to="/settings/rules" data-testid="nav-settings">{t('nav.settings')}</NavLink>}
          </nav>
        )}
        <div className="topbar-end">
          {session && (
            <span className="whoami" data-testid="whoami">
              <span data-testid="username">{session.me.username}</span>
            </span>
          )}
          <button type="button" onClick={() => void i18n.changeLanguage(otherLanguage)}>{t('language.switch')}</button>
          {onLogout && <button type="button" data-testid="logout" onClick={onLogout}>{t('auth.logout')}</button>}
        </div>
      </header>
      <main className="page">{children}</main>
      {session && (
        <footer className="footer">
          <RoleList roles={session.me.roles} />
          <span data-testid="roles-source">{t('me.rolesSource')}</span>
        </footer>
      )}
    </div>
  );
}
