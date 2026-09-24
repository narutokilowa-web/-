export function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString(lang === 'ar' ? 'ar-SY-u-nu-latn' : 'en-GB', {
    timeZone: 'Asia/Damascus',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

export function display(value: unknown): string {
  if (value === null || value === undefined || value === '') return '—';
  return String(value);
}
