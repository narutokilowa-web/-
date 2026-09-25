import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { hasRole, useSession } from './MeContext';
import { useResource } from '../shared/useApi';
import type { Device, Fault, QuarantineEntry, WorkOrderListItem } from '../shared/types';

/** "What needs me now?" (docs/lean-pilot/09 §9.1): a queue per role, not charts. */
export function HomePage() {
  const session = useSession();
  const { t } = useTranslation();
  const supervisor = hasRole(session, 'SUPERVISOR');
  const deviceApprover = hasRole(session, 'SUPERVISOR', 'ADMIN');
  const openFaults = useResource<Fault[]>(supervisor ? '/api/v1/faults?state=OPEN' : null);
  const newWorkOrders = useResource<WorkOrderListItem[]>(supervisor ? '/api/v1/work-orders?state=NEW' : null);
  const pendingReview = useResource<WorkOrderListItem[]>(supervisor ? '/api/v1/work-orders?state=PENDING_REVIEW' : null);
  const pendingDevices = useResource<Device[]>(deviceApprover ? '/api/v1/devices?status=PENDING_APPROVAL' : null);
  const needsReassignment = useResource<WorkOrderListItem[]>(supervisor ? '/api/v1/work-orders/needs-reassignment' : null);
  const quarantined = useResource<QuarantineEntry[]>(supervisor ? '/api/v1/sync/quarantine?state=PENDING' : null);
  const count = <T,>(r: { result: { ok: true; data: T[] } | { ok: false } | null }) => (r.result?.ok ? r.result.data.length : '…');

  return (
    <section data-testid="me">
      <h1>{t('me.welcome', { name: session.me.displayName })}</h1>
      <ul className="cards">
        {supervisor && (
          <li><Link to="/faults?state=OPEN" data-testid="home-open-faults">{t('home.openFaults')} <strong>{count(openFaults)}</strong></Link></li>
        )}
        {supervisor && (
          <li><Link to="/work-orders?state=NEW" data-testid="home-unassigned">{t('home.unassignedWorkOrders')} <strong>{count(newWorkOrders)}</strong></Link></li>
        )}
        {supervisor && (
          <li><Link to="/work-orders?state=PENDING_REVIEW" data-testid="home-pending-review">{t('home.pendingReview')} <strong>{count(pendingReview)}</strong></Link></li>
        )}
        {supervisor && (
          <li><Link to="/work-orders/needs-reassignment" data-testid="home-needs-reassignment">{t('home.needsReassignment')} <strong>{count(needsReassignment)}</strong></Link></li>
        )}
        {supervisor && (
          <li><Link to="/sync/quarantine" data-testid="home-quarantine">{t('home.quarantine')} <strong>{count(quarantined)}</strong></Link></li>
        )}
        {hasRole(session, 'SUPERVISOR', 'AUDITOR') && (
          <li><Link to="/dashboard" data-testid="home-dashboard">{t('home.dashboard')}</Link></li>
        )}
        {deviceApprover && (
          <li><Link to="/devices?status=PENDING_APPROVAL" data-testid="home-pending-devices">{t('home.pendingDevices')} <strong>{count(pendingDevices)}</strong></Link></li>
        )}
        {hasRole(session, 'WAREHOUSE') && (
          <li><Link to="/stock" data-testid="home-stock">{t('home.stock')}</Link></li>
        )}
        {hasRole(session, 'AUDITOR') && (
          <li><Link to="/audit" data-testid="home-audit">{t('home.audit')}</Link></li>
        )}
        {hasRole(session, 'ADMIN') && (
          <li><Link to="/users" data-testid="home-users">{t('home.users')}</Link></li>
        )}
        {hasRole(session, 'ADMIN') && (
          <li><Link to="/settings/rules" data-testid="home-settings">{t('home.settings')}</Link></li>
        )}
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
      {hasRole(session, 'TECHNICIAN') && <p className="hint">{t('home.technicianUsesPhone')}</p>}
      {session.me.roles.length === 0 && <p>{t('me.noRoles')}</p>}
    </section>
  );
}
