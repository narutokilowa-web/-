import { useTranslation } from 'react-i18next';
import type { ChangeValue, HistoryEntry } from '../../shared/types';
import { formatDateTime } from '../../shared/format';

const show = (v: ChangeValue) => (v === null ? '∅' : Array.isArray(v) ? v.join(', ') : String(v));

/** Everything the audit log recorded about the asset, newest first: who, when, what (old → new), why, hash. */
export function HistoryTimeline({ entries }: { entries: HistoryEntry[] }) {
  const { t, i18n } = useTranslation();
  const newestFirst = [...entries].sort((a, b) => b.seq - a.seq);
  return (
    <ol className="timeline" data-testid="history">
      {newestFirst.map((e) => (
        <li key={e.seq} data-action={e.action} data-seq={e.seq}>
          <div className="timeline-head">
            <strong>{t(`action.${e.action}`, { defaultValue: e.action })}</strong>
            <span>{formatDateTime(e.occurredAt, i18n.language)}</span>
            <span data-testid="actor">{e.actorName}{e.actorRoles.length > 0 ? ` (${e.actorRoles.map((r) => t(`roles.${r}`, { defaultValue: r })).join('، ')})` : ''}</span>
          </div>
          {e.reason && <p className="reason">{t('history.reason')}: {e.reason}</p>}
          {Object.keys(e.changes).length > 0 && (
            <table className="changes">
              <thead>
                <tr><th>{t('history.field')}</th><th>{t('history.old')}</th><th>{t('history.new')}</th></tr>
              </thead>
              <tbody>
                {Object.entries(e.changes).map(([field, c]) => (
                  <tr key={field} data-field={field}>
                    <td><code>{field}</code></td>
                    <td className="old">{show(c.old)}</td>
                    <td className="new">{show(c.new)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {e.facts && (
            <p className="facts-line">
              {Object.entries(e.facts).map(([k, v]) => (<span key={k}><code>{k}</code>: <code>{show(v)}</code> </span>))}
            </p>
          )}
          <p className="hash" title={t('history.hashTitle')}>#{e.seq} · <code>{e.hash.slice(0, 16)}…</code></p>
        </li>
      ))}
    </ol>
  );
}
