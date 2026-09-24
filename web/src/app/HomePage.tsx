import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { hasRole, useSession } from './MeContext';

export function HomePage() {
  const session = useSession();
  const { t } = useTranslation();
  return (
    <section data-testid="me">
      <h1>{t('me.welcome', { name: session.me.displayName })}</h1>
      <ul className="cards">
        {hasRole(session, 'REGISTRAR') && (
          <li><Link to="/registrations/new">{t('home.register')}</Link></li>
        )}
        {hasRole(session, 'REGISTRAR') && (
          <li><Link to="/registrations">{t('home.myRegistrations')}</Link></li>
        )}
        {hasRole(session, 'APPROVER') && (
          <li><Link to="/registrations?state=SUBMITTED" data-testid="home-approval-queue">{t('home.approvalQueue')}</Link></li>
        )}
        {hasRole(session, 'TECHNICIAN', 'INSPECTOR', 'SUPERVISOR', 'REGISTRAR', 'APPROVER', 'AUDITOR') && (
          <li><Link to="/assets">{t('home.assets')}</Link></li>
        )}
      </ul>
      {session.me.roles.length === 0 && <p>{t('me.noRoles')}</p>}
    </section>
  );
}
