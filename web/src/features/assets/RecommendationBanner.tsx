import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import type { Recommendation } from '../../shared/types';
import { formatDateTime } from '../../shared/format';

/**
 * "Recurrent Failure — Recommendation: Full inspection recommended" with what the rule counted and its configured
 * threshold and window (docs/lean-pilot/11 §11.2 M3 item 10). One transparent rule: no score, no prediction.
 */
export function RecommendationBanner({ recommendations }: { recommendations: Recommendation[] }) {
  const { t, i18n } = useTranslation();
  if (recommendations.length === 0) return null;
  return (
    <>
      {recommendations.map((r) => (
        <div key={r.id} className="recommendation" role="status" data-testid="recommendation">
          <strong>{t('recommendation.title')}</strong>
          <p>{t(`recommendation.${r.code}`)}</p>
          <p className="hint" data-testid="recommendation-rule">{t('recommendation.rule', { count: r.count, threshold: r.threshold, days: r.windowDays })}</p>
          <ul>
            {r.countedFailures.map((c) => (
              <li key={c.workOrderId}>
                <Link to={`/work-orders/${c.workOrderId}`}>{c.workOrderCode}</Link> — {t(`diagnosis.${c.diagnosisCode}`, { defaultValue: c.diagnosisCode })} — {formatDateTime(c.resolvedAt, i18n.language)}
              </li>
            ))}
          </ul>
        </div>
      ))}
    </>
  );
}
