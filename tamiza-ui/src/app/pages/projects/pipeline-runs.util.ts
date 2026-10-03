/** "8.2 s" or "3 min 5 s" between two ISO times; null when either is missing. */
export function durationText(start: string | null, end: string | null): string | null {
  if (!start || !end) {
    return null;
  }
  const seconds = Math.max(0, (Date.parse(end) - Date.parse(start)) / 1000);
  if (seconds < 60) {
    return `${seconds.toFixed(1)} s`;
  }
  const minutes = Math.floor(seconds / 60);
  return `${minutes} min ${Math.round(seconds - minutes * 60)} s`;
}
