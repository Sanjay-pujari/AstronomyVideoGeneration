import { api, loadDashboardData, type DashboardData } from './services/api.js';
import { mockDashboardData } from './services/mockData.js';
import { renderDashboardHtml, runDetails, type PageKey } from './ui/dashboard.js';

const root = document.getElementById('root');
const validPages = new Set<PageKey>(['dashboard', 'runs', 'regions', 'events', 'analytics', 'settings']);
let latestData: DashboardData | undefined;
let latestError: string | undefined;

function currentPage(): PageKey {
  const page = location.hash.replace(/^#/, '') as PageKey;
  return validPages.has(page) ? page : 'dashboard';
}

function renderLoading() {
  if (root) root.innerHTML = '<main class="app-shell"><div class="state state--loading" role="status">Loading AstroPulse telemetry…</div></main>';
}

function render(data: DashboardData, error?: string) {
  latestData = data;
  latestError = error;
  if (root) root.innerHTML = renderDashboardHtml(data, { error, page: currentPage() });
  bindInteractions();
}

async function loadRun(runId: string) {
  const target = document.getElementById('pipeline-result');
  if (!target) return;
  target.innerHTML = '<div class="state state--loading">Loading pipeline status…</div>';
  try {
    const run = await api.getPipelineStatus(runId);
    target.innerHTML = runDetails(run);
  } catch (error) {
    target.innerHTML = `<div class="state state--error">${error instanceof Error ? error.message : 'Unable to load pipeline status'}</div>`;
  }
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
        await api.requestRegionRunNow(regionId);
        button.textContent = 'Started';
      } catch (error) {
        button.textContent = error instanceof Error ? 'Run unavailable' : 'Manual run unavailable';
      } finally {
        setTimeout(() => {
          button.textContent = 'Run now';
          button.disabled = false;
        }, 1400);
      }
    });
  });

  document.querySelectorAll<HTMLButtonElement>('[data-schedule-run]').forEach((button) => {
    button.addEventListener('click', async () => {
      const scheduleName = button.dataset.scheduleRun;
      if (!scheduleName) return;
      button.textContent = 'Starting…';
      button.disabled = true;
      try {
        await api.requestSchedulerRunNow(scheduleName);
        button.textContent = 'Started';
      } catch {
        button.textContent = 'Unavailable';
      } finally {
        setTimeout(() => {
          button.textContent = 'Run schedule';
          button.disabled = false;
        }, 1400);
      }
    });
  });
  document.getElementById('load-run')?.addEventListener('click', async () => {
    const input = document.getElementById('run-id') as HTMLInputElement | null;
    if (!input?.value) return;
    await loadRun(input.value);
  });
  document.querySelectorAll<HTMLButtonElement>('[data-load-run]').forEach((button) => {
    button.addEventListener('click', async () => {
      const runId = button.dataset.loadRun;
      const input = document.getElementById('run-id') as HTMLInputElement | null;
      if (location.hash !== '#runs') location.hash = '#runs';
      await Promise.resolve();
      if (input && runId) input.value = runId;
      if (runId) await loadRun(runId);
    });
  });
}

async function loadAndRender() {
  renderLoading();
  try {
    const data = await loadDashboardData();
    render(data);
  } catch (error) {
    render(mockDashboardData, error instanceof Error ? error.message : 'Unable to load dashboard data.');
  }
}

window.addEventListener('hashchange', () => {
  if (latestData) render(latestData, latestError);
});

void loadAndRender();
