import type { ReportFaultRequest } from '../../shared/types';

export interface FaultFormState {
  category: ReportFaultRequest['category'] | '';
  powerAtInspection: ReportFaultRequest['powerAtInspection'] | '';
  powerInfoSource: string;
  powerNote: string;
  description: string;
}

export const emptyFaultForm: FaultFormState = { category: '', powerAtInspection: '', powerInfoSource: '', powerNote: '', description: '' };

/** Client-side check before sending. The server validates everything again. */
export function faultFormErrors(f: FaultFormState): string[] {
  const errors: string[] = [];
  if (!f.category) errors.push('category');
  if (!f.powerAtInspection) errors.push('powerAtInspection');
  return errors;
}

export function toReportFaultRequest(assetId: string, f: FaultFormState): ReportFaultRequest {
  const blank = (s: string) => (s.trim() === '' ? null : s.trim());
  return {
    assetId,
    category: f.category as ReportFaultRequest['category'],
    powerAtInspection: f.powerAtInspection as ReportFaultRequest['powerAtInspection'],
    powerInfoSource: (blank(f.powerInfoSource) as ReportFaultRequest['powerInfoSource']) ?? null,
    powerNote: blank(f.powerNote),
    description: blank(f.description),
  };
}
