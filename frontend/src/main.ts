import { api, loadDashboardData } from './services/api.js';
import { mockDashboardData } from './services/mockData.js';
import { renderDashboardHtml } from './ui/dashboard.js';

const root = document.getElementById('root');

function renderLoading() {
  if (root) root.innerHTML = '<main class="app-shell"><div class="state state--loading" role="status">Loading AstroPulse telemetry…</div></main>';
}

function bindInteractions() {
  document.getElementById('refresh-dashboard')?.addEventListener('click', () => void loadAndRender());
  document.querySelectorAll<HTMLButtonElement>('[data-region-run]').forEach((button) => {
    button.addEventListener('click', async () => {
      const regionId = button.dataset.regionRun;
      if (!regionId) return;
      button.textContent = 'Starting…';
      button.disabled = true;
      try {
        await api.requestManualRun(regionId);
        button.textContent = 'Started';
      } catch {
        button.textContent = 'Manual run unavailable';
      } finally {
        button.disabled = false;
      }
    });
  });
  document.getElementById('load-run')?.addEventListener('click', async () => {
    const input = document.getElementById('run-id') as HTMLInputElement | null;
    const target = document.getElementById('pipeline-result');
    if (!input?.value || !target) return;
    target.innerHTML = '<div class="state state--loading">Loading pipeline status…</div>';
    try {
      const run = await api.getPipelineStatus(input.value);
      target.innerHTML = `<div class="status-panel"><span class="status-badge">${run.status}</span><p>Stage: ${run.stage ?? 'Not reported'}</p><p>Region: ${run.regionName ?? run.regionId ?? 'Not reported'}</p><p>${run.message ?? 'No pipeline message.'}</p></div>`;
    } catch (error) {
      target.innerHTML = `<div class="state state--error">${error instanceof Error ? error.message : 'Unable to load pipeline status'}</div>`;
    }
  });
}

async function loadAndRender() {
  renderLoading();
  try {
    const data = await loadDashboardData();
    if (root) root.innerHTML = renderDashboardHtml(data);
  } catch (error) {
    if (root) root.innerHTML = renderDashboardHtml(mockDashboardData, { error: error instanceof Error ? error.message : 'Unable to load dashboard data.' });
  }
  bindInteractions();
}

void loadAndRender();
