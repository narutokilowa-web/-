import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { PolicySetting, RuleSetting } from '../../shared/types';
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
      <SecurityPolicies />
    </section>
  );
}

/**
 * M4 security policies (docs/lean-pilot/05 §5.5, §5.9): offline-grant lifetime, grant-window tolerance, app lock and the
 * minimum field-app version are data, validated against their allowed range and changed only with a reason (audited).
 */
function SecurityPolicies() {
  const { t, i18n } = useTranslation();
  const { post } = useApi();
  const policies = useResource<PolicySetting[]>('/api/v1/settings/policy');
  const [values, setValues] = useState<Record<string, string>>({});
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [problem, setProblem] = useState<Problem | null>(null);
  const [savedKey, setSavedKey] = useState<string | null>(null);
  if (!policies.result) return null;
  if (!policies.result.ok) return <ProblemView problem={policies.result.problem} />;

  async function save(key: string) {
    setProblem(null);
    setSavedKey(null);
    const res = await post(`/api/v1/settings/policy/${key}`, { value: (values[key] ?? '').trim(), reason: (reasons[key] ?? '').trim() || null });
    if (!res.ok) {
      setProblem(res.problem);
      return;
    }
    setSavedKey(key);
    setValues({ ...values, [key]: '' });
    setReasons({ ...reasons, [key]: '' });
    policies.reload();
  }

  return (
    <div className="panel" data-testid="security-policies">
      <h3>{t('policy.title')}</h3>
      <p className="hint">{t('policy.hint')}</p>
      <table className="table">
        <thead><tr><th>{t('policy.setting')}</th><th>{t('policy.current')}</th><th>{t('policy.newValue')}</th><th /></tr></thead>
        <tbody>
          {policies.result.data.map((p) => (
            <tr key={p.key} data-policy={p.key}>
              <td>{t(`policy.${p.key}`, { defaultValue: p.description })}<div className="hint">{t('policy.default', { value: p.default })}</div></td>
              <td>
                <strong data-testid={`policy-value-${p.key}`}>{p.value}</strong>
                <div className="hint">{t('settings.lastChanged', { at: formatDateTime(p.changedAt, i18n.language), version: p.version })}</div>
              </td>
              <td>
                <div className="row-actions">
                  <input value={values[p.key] ?? ''} placeholder={p.value} data-testid={`policy-input-${p.key}`} onChange={(e) => setValues({ ...values, [p.key]: e.target.value })} />
                  <input value={reasons[p.key] ?? ''} placeholder={t('settings.reason')} data-testid={`policy-reason-${p.key}`} onChange={(e) => setReasons({ ...reasons, [p.key]: e.target.value })} />
                </div>
              </td>
              <td>
                <button type="button" className="primary" data-testid={`policy-save-${p.key}`} onClick={() => void save(p.key)}>{t('settings.save')}</button>
                {savedKey === p.key && <span role="status" className="hint" data-testid="policy-saved"> {t('settings.saved')}</span>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {problem && <ProblemView problem={problem} />}
    </div>
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
