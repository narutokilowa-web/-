import { useTranslation } from 'react-i18next';
import { NavLink } from 'react-router';
import type { DocumentFlag } from '../../shared/types';

/** The warehouse area (M5): balances, issues, returns, failed parts, reconciliation, history. */
export function StockNav() {
  const { t } = useTranslation();
  const tabs: [string, string][] = [
    ['/stock', 'stockNav.balances'],
    ['/stock/issues', 'stockNav.issues'],
    ['/stock/returns', 'stockNav.returns'],
    ['/stock/core-returns', 'stockNav.coreReturns'],
    ['/stock/reconciliation', 'stockNav.reconciliation'],
    ['/stock/history', 'stockNav.history'],
  ];
  return (
    <nav className="filters" data-testid="stock-nav">
      {tabs.map(([to, key]) => (
        <NavLink key={to} to={to} end className={({ isActive }) => (isActive ? 'tab active' : 'tab')} data-testid={`stock-nav-${to.split('/')[2] ?? 'balances'}`}>
          {t(key)}
        </NavLink>
      ))}
    </nav>
  );
}

/** Flags on a stock document: signals for review, never verdicts. */
export function DocumentFlags({ flags }: { flags: DocumentFlag[] }) {
  const { t } = useTranslation();
  if (flags.length === 0) return null;
  return (
    <div className="row-actions" data-testid="document-flags">
      {flags.map((f) => (
        <span key={f.id} className={`badge severity-${f.severity}`} data-testid="document-flag" data-rule={f.ruleCode}>
          {t(`flag.${f.ruleCode}`, { defaultValue: f.ruleCode })}
        </span>
      ))}
    </div>
  );
}
