import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { Registration } from '../../shared/types';
import { emptyForm, fromRegistration, syntheticForm, toRequest, type RegistrationFormValues } from './registrationForm';
import { ProblemView } from '../../shared/ProblemView';

type Field = keyof RegistrationFormValues;

export function RegistrationFormPage() {
  const { id } = useParams();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { post } = useApi();
  const existing = useResource<Registration>(id ? `/api/v1/registrations/${id}` : null);
  const [form, setForm] = useState<RegistrationFormValues>(emptyForm());
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (existing.result?.ok) setForm(fromRegistration(existing.result.data));
  }, [existing.result]);

  const set = (field: Field) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [field]: e.target.value }));
  const solar = form.powerSource !== 'GRID';

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setProblem(null);
    const result = id
      ? await post<{ id: string }>(`/api/v1/registrations/${id}/update`, toRequest(form))
      : await post<{ id: string }>('/api/v1/registrations', toRequest(form));
    setBusy(false);
    if (result.ok) navigate(`/registrations/${result.data.id}`);
    else setProblem(result.problem);
  }

  const input = (field: Field, type = 'text', extra: Record<string, string> = {}) => (
    <label>
      <span>{t(`field.${field}`)}</span>
      <input name={field} type={type} value={form[field]} onChange={set(field)} {...extra} />
    </label>
  );
  const select = (field: Field, options: string[], group: string) => (
    <label>
      <span>{t(`field.${field}`)}</span>
      <select name={field} value={form[field]} onChange={set(field)}>
        {options.map((o) => (
          <option key={o} value={o}>{t(`${group}.${o}`)}</option>
        ))}
      </select>
    </label>
  );

  return (
    <section>
      <h1>{id ? t('registration.editTitle') : t('registration.newTitle')}</h1>
      <p className="hint">{t('registration.syntheticHint')}</p>
      {!id && (
        <button type="button" data-testid="fill-synthetic" onClick={() => setForm(syntheticForm())}>{t('registration.fillSynthetic')}</button>
      )}
      <form onSubmit={onSubmit} className="form" data-testid="registration-form">
        <fieldset>
          <legend>{t('registration.sectionRegistration')}</legend>
          {select('reason', ['BASELINE_SURVEY', 'NEW_INSTALLATION', 'MISSED_IN_SURVEY'], 'reason')}
          {form.reason === 'NEW_INSTALLATION' && input('projectRef')}
        </fieldset>
        <fieldset>
          <legend>{t('registration.sectionLocation')}</legend>
          {input('latitude', 'number', { step: '0.000001', required: 'required' })}
          {input('longitude', 'number', { step: '0.000001', required: 'required' })}
          {input('addressText')}
          {input('circuitRef')}
        </fieldset>
        <fieldset>
          <legend>{t('registration.sectionOwnership')}</legend>
          {select('responsibleParty', ['MUNICIPALITY', 'ELECTRICITY_AUTHORITY', 'OTHER_PUBLIC_ENTITY', 'SHARED'], 'responsibleParty')}
          {input('responsibleEntityName')}
        </fieldset>
        <fieldset>
          <legend>{t('registration.sectionEquipment')}</legend>
          {select('powerSource', ['GRID', 'SOLAR', 'HYBRID'], 'powerSource')}
          {select('observedStatus', ['OPERATIONAL', 'FAULTY', 'UNKNOWN'], 'status')}
          {input('poleMaterial')}
          {input('poleHeightM', 'number', { step: '0.1' })}
          {input('luminaireMake')}
          {input('luminaireModel')}
          {input('luminaireWattageW', 'number')}
          {input('luminaireSerial')}
          {solar && input('solarPanelW', 'number')}
          {solar && input('solarBatteryType')}
          {solar && input('solarBatteryAh', 'number')}
          {solar && input('solarControllerModel')}
        </fieldset>
        <fieldset>
          <legend>{t('registration.sectionInstallation')}</legend>
          {select('installedOnPrecision', ['EXACT', 'YEAR', 'UNKNOWN'], 'precision')}
          {form.installedOnPrecision === 'EXACT' && input('installedOn', 'date')}
          {form.installedOnPrecision === 'YEAR' && input('installedOn', 'number', { min: '1950', max: '2100', placeholder: '2019' })}
          {input('warrantyUntil', 'date')}
          {input('supplier')}
        </fieldset>
        {problem && <ProblemView problem={problem} />}
        <button type="submit" className="primary" disabled={busy} data-testid="save-draft">{t('registration.saveDraft')}</button>
      </form>
    </section>
  );
}
