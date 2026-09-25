import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import { useApi, useResource } from '../../shared/useApi';
import type { Problem } from '../../shared/api';
import type { Registration } from '../../shared/types';
import { formatDateTime } from '../../shared/format';
import { AttributesView } from '../assets/AttributesView';
import { ProblemView } from '../../shared/ProblemView';

const DECISIONS: Record<string, { path: string; testId: string; needsNote: boolean }> = {
  ApproveRegistration: { path: 'approve', testId: 'approve', needsNote: false },
  ReturnRegistration: { path: 'return', testId: 'return', needsNote: true },
  RejectRegistration: { path: 'reject', testId: 'reject', needsNote: true },
};

export function RegistrationDetailPage() {
  const { id } = useParams();
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { post } = useApi();
  const { result, reload } = useResource<Registration>(`/api/v1/registrations/${id}`);
  const [note, setNote] = useState('');
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);

  if (!result) return <p>{t('auth.loading')}</p>;
  if (!result.ok) return <p role="alert">{t(`error.${result.problem.code}`, { defaultValue: result.problem.code })}</p>;
  const r = result.data;
  const can = (command: string) => r.allowedCommands.includes(command);

  async function run(path: string, body?: unknown) {
    setBusy(true);
    setProblem(null);
    const res = await post(`/api/v1/registrations/${id}/${path}`, body);
    setBusy(false);
    if (!res.ok) {
      setProblem(res.problem);
      return;
    }
    if (path === 'approve') navigate(`/assets/${r.assetId}`);
    else reload();
  }

  return (
    <section>
      <h1>
        {t('registration.detailTitle')} <span className={`badge state-${r.state}`} data-testid="registration-state">{t(`registrationState.${r.state}`)}</span>
      </h1>
      <dl className="facts">
        <dt>{t('field.reason')}</dt><dd>{t(`reason.${r.reason}`)}{r.projectRef ? ` — ${r.projectRef}` : ''}</dd>
        <dt>{t('registration.registrar')}</dt><dd data-testid="registrar-name">{r.registrarName}</dd>
        <dt>{t('registration.captureChannel')}</dt><dd>{r.captureChannel}</dd>
        <dt>{t('registration.createdAt')}</dt><dd>{formatDateTime(r.createdAt, i18n.language)}</dd>
        <dt>{t('registration.submittedAt')}</dt><dd>{formatDateTime(r.submittedAt, i18n.language)}</dd>
        {r.decidedByName && (<><dt>{t('registration.decidedBy')}</dt><dd>{r.decidedByName} — {formatDateTime(r.decidedAt, i18n.language)}</dd></>)}
        {r.decisionNote && (<><dt>{t('registration.decisionNote')}</dt><dd data-testid="decision-note">{r.decisionNote}</dd></>)}
        {r.assetCode && (<><dt>{t('asset.code')}</dt><dd><Link to={`/assets/${r.assetId}`} data-testid="asset-link">{r.assetCode}</Link></dd></>)}
      </dl>

      <AttributesView attributes={r.attributes} />

      <div className="actions">
        {can('UpdateRegistrationDraft') && <Link className="button" to={`/registrations/${id}/edit`} data-testid="edit">{t('registration.edit')}</Link>}
        {can('SubmitRegistration') && <button type="button" className="primary" disabled={busy} data-testid="submit" onClick={() => void run('submit')}>{t('registration.submit')}</button>}
        {can('DiscardDraft') && <button type="button" disabled={busy} data-testid="discard" onClick={() => void run('discard')}>{t('registration.discard')}</button>}
      </div>

      {Object.keys(DECISIONS).some(can) && (
        <div className="decision" data-testid="decision-panel">
          <label>
            <span>{t('registration.note')}</span>
            <textarea value={note} onChange={(e) => setNote(e.target.value)} data-testid="decision-note-input" />
          </label>
          <div className="actions">
            {Object.entries(DECISIONS).filter(([c]) => can(c)).map(([command, d]) => (
              <button key={command} type="button" className={command === 'ApproveRegistration' ? 'primary' : ''} disabled={busy} data-testid={d.testId}
                onClick={() => void run(d.path, { note: note.trim() === '' ? null : note.trim() })}>
                {t(`registration.command.${command}`)}
              </button>
            ))}
          </div>
          <p className="hint">{t('registration.makerChecker')}</p>
        </div>
      )}
      {problem && <ProblemView problem={problem} />}
    </section>
  );
}
