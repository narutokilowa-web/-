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

/** Money is always shown with its currency (SYP by default in the pilot data; never assumed). */
export function formatMoney(money: { amount: number; currency: string } | null | undefined, lang: string): string {
  if (!money) return '—';
  const amount = money.amount.toLocaleString(lang === 'ar' ? 'ar-SY-u-nu-latn' : 'en-GB', { maximumFractionDigits: 2 });
  return `${amount} ${money.currency}`;
}

export function formatQuantity(quantity: number): string {
  return Number.isInteger(quantity) ? String(quantity) : quantity.toFixed(3).replace(/0+$/, '').replace(/\.$/, '');
}

export function shortHash(hash: string | null | undefined, length = 16): string {
  if (!hash) return '—';
  return hash.length > length ? `${hash.slice(0, length)}…` : hash;
}
