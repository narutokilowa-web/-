import { useTranslation } from 'react-i18next';
import type { Problem } from './api';

export function ProblemView({ problem }: { problem: Problem }) {
  const { t } = useTranslation();
  return (
    <div role="alert" className="problem" data-testid="problem" data-code={problem.code}>
      <strong>{t(`error.${problem.code}`, { defaultValue: problem.detail ?? problem.code })}</strong>
      {problem.details && (
        <ul>
          {Object.entries(problem.details).map(([k, v]) => (
            <li key={k}>{t(`field.${k}`, { defaultValue: k })}: {v}</li>
          ))}
        </ul>
      )}
    </div>
  );
}
