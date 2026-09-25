import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { RuleSetting } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { ProblemView } from '../../shared/ProblemView';

/** Admin: the repeated-failure rule's threshold and window are data in rule_settings; every change is audited with a reason. */
export function RuleSettingsPage() {
  const { t } = useTranslation();
  const settings = useResource<RuleSetting[]>('/api/v1/settings/rules');
  const [saved, setSaved] = useState(false);
  if (!settings.result) return <p>{t('auth.loading')}</p>;
  if (!settings.result.ok) return <ProblemView problem={settings.result.problem} />;
  const current = settings.result.data.find((s) => s.ruleCode === 'REPEATED_FAILURE');
  return (
    <section>
      <h1>{t('settings.title')}</h1>
      {current ? <RepeatedFailureForm key={current.version} current={current} saved={saved} onSaving={() => setSaved(false)} onSaved={() => { setSaved(true); settings.reload(); }} /> : <p>{t('common.empty')}</p>}
    </section>
  );
}

function RepeatedFailureForm({ current, saved, onSaving, onSaved }: { current: RuleSetting; saved: boolean; onSaving: () => void; onSaved: () => void }) {
  const { t, i18n } = useTranslation();
  const { post } = useApi();
  const [threshold, setThreshold] = useState(String(current.params.threshold ?? ''));
  const [windowDays, setWindowDays] = useState(String(current.params.window_days ?? ''));
  const [enabled, setEnabled] = useState(current.enabled);
  const [reason, setReason] = useState('');
  const [problem, setProblem] = useState<Problem | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setProblem(null);
    onSaving();
    const res = await post('/api/v1/settings/rules/repeated-failure', {
      threshold: Number(threshold), windowDays: Number(windowDays), enabled, reason: reason.trim() || null,
    });
    if (!res.ok) {
      setProblem(res.problem);
      return;
    }
    onSaved();
  }

  return (
    <>
      <form className="panel" onSubmit={(e) => void submit(e)} data-testid="rule-settings">
        <h3>{t('settings.repeatedFailure')}</h3>
        <p className="hint">{t('settings.repeatedFailureHint')}</p>
        <p className="hint" data-testid="rule-version">{t('settings.lastChanged', { at: formatDateTime(current.changedAt, i18n.language), version: current.version })}</p>
        <div className="grid-2">
          <label><span>{t('settings.threshold')}</span><input type="number" min={1} max={100} value={threshold} data-testid="rule-threshold" onChange={(e) => setThreshold(e.target.value)} /></label>
          <label><span>{t('settings.windowDays')}</span><input type="number" min={1} max={3650} value={windowDays} data-testid="rule-window" onChange={(e) => setWindowDays(e.target.value)} /></label>
        </div>
        <label className="inline"><input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /> {t('settings.enabled')}</label>
        <label><span>{t('settings.reason')}</span><input value={reason} data-testid="rule-reason" onChange={(e) => setReason(e.target.value)} /></label>
        <div className="actions"><button type="submit" className="primary" data-testid="rule-save">{t('settings.save')}</button></div>
        {saved && <p role="status" data-testid="rule-saved">{t('settings.saved')}</p>}
      </form>
      {problem && <ProblemView problem={problem} />}
    </>
  );
}
