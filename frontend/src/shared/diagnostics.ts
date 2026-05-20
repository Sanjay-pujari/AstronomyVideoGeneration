export function publishDiagnosticsReport(report: unknown) {
  const encoded = encodeURIComponent(JSON.stringify(report, null, 2));
  document.querySelectorAll<HTMLAnchorElement>('[data-api-health-download]').forEach((link) => {
    link.href = `data:application/json;charset=utf-8,${encoded}`;
    link.download = 'frontend-api-health.json';
  });
}
