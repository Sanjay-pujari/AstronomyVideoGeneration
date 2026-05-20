export function formatDateTime(value?: string) {
  if (!value) return 'Date to be announced';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'full', timeStyle: 'short' }).format(date);
}
