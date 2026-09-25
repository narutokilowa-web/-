import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { useResource } from '../../shared/useApi';
import type { AuditSearchEntry, HistoryEntry } from '../../shared/types';
import { ProblemView } from '../../shared/ProblemView';
import { HistoryTimeline } from '../assets/HistoryTimeline';

export const AUDIT_ENTITY_TYPES = ['work_order', 'street_light', 'fault', 'evidence', 'stock_movement', 'device', 'registration', 'rule_setting', 'recommendation'] as const;

/** Builds the query string of GET /api/v1/audit/events from the form (empty fields are left out). */
export function auditQuery(entityType: string, entityId: string, action: string): string {
  const q = new URLSearchParams();
  if (entityType) q.set('entityType', entityType);
  if (entityId.trim()) q.set('entityId', entityId.trim());
  if (action.trim()) q.set('action', action.trim());
  q.set('take', '200');
  return q.toString();
}

/** The auditor searches the tamper-evident audit log by entity (docs/lean-pilot/11 §11.2 M3 item 6). */
export function AuditSearchPage() {
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const [entityType, setEntityType] = useState(params.get('entityType') ?? '');
  const [entityId, setEntityId] = useState(params.get('entityId') ?? '');
  const [action, setAction] = useState(params.get('action') ?? '');
  const query = params.toString();
  const { result } = useResource<AuditSearchEntry[]>(query ? `/api/v1/audit/events?${query}` : null);

  function submit(e: FormEvent) {
    e.preventDefault();
    setParams(new URLSearchParams(auditQuery(entityType, entityId, action)));
  }

  const entries: HistoryEntry[] = result?.ok ? result.data.map((x) => ({ ...x })) : [];
  return (
    <section>
      <h1>{t('audit.title')}</h1>
      <p className="hint">{t('audit.hint')}</p>
      <form className="panel" onSubmit={submit} data-testid="audit-search">
        <div className="grid-2">
          <label>
            <span>{t('audit.entityType')}</span>
            <select value={entityType} data-testid="audit-entity-type" onChange={(e) => setEntityType(e.target.value)}>
              <option value="">{t('common.all')}</option>
              {AUDIT_ENTITY_TYPES.map((x) => <option key={x} value={x}>{t(`entityType.${x}`)}</option>)}
            </select>
          </label>
          <label>
            <span>{t('audit.entityId')}</span>
            <input value={entityId} data-testid="audit-entity-id" dir="ltr" onChange={(e) => setEntityId(e.target.value)} />
          </label>
          <label>
            <span>{t('audit.action')}</span>
            <input value={action} data-testid="audit-action" dir="ltr" placeholder="work_order.closed" onChange={(e) => setAction(e.target.value)} />
          </label>
        </div>
        <div className="actions"><button type="submit" className="primary" data-testid="audit-submit">{t('common.search')}</button></div>
      </form>
      {query && (!result ? <p>{t('auth.loading')}</p> : !result.ok ? <ProblemView problem={result.problem} /> : result.data.length === 0 ? <p>{t('common.empty')}</p> : (
        <>
          <p className="hint" data-testid="audit-count">{t('audit.count', { count: result.data.length })}</p>
          <HistoryTimeline entries={entries} showEntity />
        </>
      ))}
    </section>
  );
}
