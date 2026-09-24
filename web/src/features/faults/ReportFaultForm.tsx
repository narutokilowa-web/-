import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useApi } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import { FAULT_CATEGORIES, POWER_AT_INSPECTION, POWER_INFO_SOURCES } from '../../shared/types';
import { ProblemView } from '../../shared/ProblemView';
import { emptyFaultForm, faultFormErrors, toReportFaultRequest, type FaultFormState } from './faultForm';

/** Supervisor: report a fault on this light (category + power at this moment). Duplicates become confirmations. */
export function ReportFaultForm({ assetId, onReported }: { assetId: string; onReported: (faultId: string, result: string) => void }) {
  const { t } = useTranslation();
  const { post } = useApi();
  const [form, setForm] = useState<FaultFormState>(emptyFaultForm);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const missing = faultFormErrors(form);
  const set = (patch: Partial<FaultFormState>) => setForm({ ...form, ...patch });

  async function submit() {
    setBusy(true);
    setProblem(null);
    const res = await post<{ faultId: string; result: string }>('/api/v1/faults', toReportFaultRequest(assetId, form));
    setBusy(false);
    if (!res.ok) {
      setProblem(res.problem);
      return;
    }
    setMessage(t(`fault.result.${res.data.result}`));
    setForm(emptyFaultForm);
    onReported(res.data.faultId, res.data.result);
  }

  return (
    <form className="panel" data-testid="report-fault" onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <h3>{t('fault.reportTitle')}</h3>
      <label>
        <span>{t('fault.category')}</span>
        <select value={form.category} data-testid="fault-category" onChange={(e) => set({ category: e.target.value as FaultFormState['category'] })}>
          <option value="">—</option>
          {FAULT_CATEGORIES.map((c) => <option key={c} value={c}>{t(`faultCategory.${c}`)}</option>)}
        </select>
      </label>
      <fieldset>
        <legend>{t('fault.powerQuestion')}</legend>
        {POWER_AT_INSPECTION.map((p) => (
          <label key={p} className="inline">
            <input type="radio" name="power" value={p} checked={form.powerAtInspection === p} data-testid={`fault-power-${p}`}
              onChange={() => set({ powerAtInspection: p })} />
            {t(`power.${p}`)}
          </label>
        ))}
      </fieldset>
      <label>
        <span>{t('fault.powerSource')}</span>
        <select value={form.powerInfoSource} onChange={(e) => set({ powerInfoSource: e.target.value })}>
          <option value="">—</option>
          {POWER_INFO_SOURCES.map((s) => <option key={s} value={s}>{t(`powerSourceInfo.${s}`)}</option>)}
        </select>
      </label>
      <label>
        <span>{t('fault.description')}</span>
        <textarea value={form.description} maxLength={1000} data-testid="fault-description" onChange={(e) => set({ description: e.target.value })} />
      </label>
      <button type="submit" className="primary" disabled={busy || missing.length > 0} data-testid="fault-submit">{t('fault.report')}</button>
      {message && <p className="hint" data-testid="fault-result">{message}</p>}
      {problem && <ProblemView problem={problem} />}
    </form>
  );
}
