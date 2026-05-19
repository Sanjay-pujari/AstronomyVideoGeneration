import { api, emptyDashboardData, getFrontendApiHealth, loadDashboardData, loadPublicPortalData, type DashboardData } from './services/api.js';
import { renderDashboardHtml, runDetails, type PageKey } from './ui/dashboard.js';
import { parsePublicRoute, renderPublicPortalHtml } from './ui/publicPortal.js';
import { resolveDashboardPage } from './ui/routes.js';

const root = document.getElementById('root');
const adminPathPages = new Set<string>(['/dashboard', '/pipeline-runs', '/regions', '/events', '/alerts', '/analytics', '/ai-optimization', '/optimization-insights', '/content-calendar', '/settings', '/tonights-sky', '/videos', '/about']);
let latestData: DashboardData | undefined;
let latestAdminData: DashboardData | undefined;
let latestAdminError: string | undefined;

function isAdminRoute() {
  return location.pathname === '/admin' || location.pathname.startsWith('/admin/') || location.pathname.startsWith('/dashboard/') || adminPathPages.has(location.pathname.replace(/\/+$/, '') || '/');
}

function resolvePage(): { page: PageKey; unknownPage?: string } {
  return resolveDashboardPage(location.pathname, location.hash);
}

function renderLoading(message = 'Loading AstroPulse…') {
  if (root) root.innerHTML = `<main class="app-shell"><div class="state state--loading" role="status">${message}</div></main>`;
}

function renderAdmin(data: DashboardData, error?: string) {
  latestAdminData = data;
  latestAdminError = error;
  const route = resolvePage();
  if (root) root.innerHTML = renderDashboardHtml(data, { error, page: route.page, warning: route.unknownPage ? `Unknown dashboard page: ${route.unknownPage}` : undefined });
  bindAdminInteractions();
}

function renderPublic(data: DashboardData) {
  latestData = data;
  if (root) root.innerHTML = renderPublicPortalHtml(data, parsePublicRoute(location.pathname));
  bindPublicInteractions();
}

function publishDiagnostics() {
  const report = JSON.stringify(getFrontendApiHealth(), null, 2);
  const encoded = encodeURIComponent(report);
  document.querySelectorAll<HTMLAnchorElement>('[data-api-health-download]').forEach((link) => {
    link.href = `data:application/json;charset=utf-8,${encoded}`;
    link.download = 'frontend-api-health.json';
  });
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

function bindPublicInteractions() {
  document.querySelectorAll<HTMLFormElement>('[data-alert-preferences]').forEach((form) => {
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      const state = form.querySelector<HTMLElement>('[data-alert-form-state]');
      const formData = new FormData(form);
      const hasEventType = formData.getAll('eventTypes').length > 0;
      if (!form.checkValidity() || !hasEventType) {
        if (state) state.textContent = 'Choose an email, region, language, time, and at least one event type.';
        return;
      }
      const subscriberId = String(formData.get('subscriberId') ?? '').trim();
      const payload = {
        email: String(formData.get('email') ?? '').trim(),
        regionId: String(formData.get('regionId') ?? '').trim(),
        language: String(formData.get('language') ?? 'en'),
        eventTypes: formData.getAll('eventTypes').map(String),
        preferredAlertTimeLocal: String(formData.get('preferredAlertTimeLocal') ?? '18:00'),
        minimumEventScore: Number(formData.get('minimumEventScore') ?? 0.65),
        preferredChannel: 'Email' as const
      };
      if (state) state.textContent = subscriberId ? 'Updating alert preferences…' : 'Creating alert subscription…';
      void (async () => {
        try {
          const result = subscriberId
            ? await api.updateAlertPreferences(subscriberId, { eventTypes: payload.eventTypes, preferredAlertTimeLocal: payload.preferredAlertTimeLocal, minimumEventScore: payload.minimumEventScore, dailySkyGuideReminderEnabled: payload.eventTypes.includes('DailySkyGuide'), specialEventAlertsEnabled: payload.eventTypes.some((type) => type !== 'DailySkyGuide'), preferredChannel: 'Email', language: payload.language })
            : await api.subscribeToAlerts(payload);
          form.querySelector<HTMLInputElement>('input[name="subscriberId"]')!.value = result.subscriberId;
          if (state) state.textContent = `Alert subscription saved. Subscriber ID: ${result.subscriberId}`;
        } catch (error) {
          if (state) state.textContent = error instanceof Error ? error.message : 'Unable to save alert subscription.';
        }
      })();
    });
  });

  document.querySelectorAll<HTMLButtonElement>('[data-alert-test]').forEach((button) => {
    button.addEventListener('click', async () => {
      const form = button.closest<HTMLFormElement>('form[data-alert-preferences]');
      const subscriberId = form?.querySelector<HTMLInputElement>('input[name="subscriberId"]')?.value.trim();
      const state = form?.querySelector<HTMLElement>('[data-alert-form-state]');
      if (!subscriberId) { if (state) state.textContent = 'Save the subscription before sending a test alert.'; return; }
      try {
        const result = await api.sendTestAlert(subscriberId);
        if (state) state.textContent = result.message;
      } catch (error) {
        if (state) state.textContent = error instanceof Error ? error.message : 'Unable to send test alert.';
      }
    });
  });
  document.querySelectorAll<HTMLButtonElement>('[data-alert-unsubscribe]').forEach((button) => {
    button.addEventListener('click', async () => {
      const form = button.closest<HTMLFormElement>('form[data-alert-preferences]');
      const subscriberIdInput = form?.querySelector<HTMLInputElement>('input[name="subscriberId"]');
      const subscriberId = subscriberIdInput?.value.trim();
      const state = form?.querySelector<HTMLElement>('[data-alert-form-state]');
      if (!subscriberId) { if (state) state.textContent = 'Save the subscription before unsubscribing.'; return; }
      try {
        await api.unsubscribeAlerts(subscriberId);
        if (subscriberIdInput) subscriberIdInput.value = '';
        if (state) state.textContent = 'Alert subscription unsubscribed.';
      } catch (error) {
        if (state) state.textContent = error instanceof Error ? error.message : 'Unable to unsubscribe.';
      }
    });
  });

  document.querySelectorAll<HTMLSelectElement>('[data-region-selector]').forEach((select) => {
    select.addEventListener('change', () => {
      if (select.value) navigate(`/regions/${encodeURIComponent(select.value)}`);
    });
  });
}

function navigate(path: string) {
  if (path.startsWith('#')) {
    if (location.hash === path) return;
    location.hash = path;
  } else {
    if (location.pathname === path) return;
    history.pushState({}, '', path);
  }
  if (isAdminRoute()) {
    if (latestAdminData) renderAdmin(latestAdminData, latestAdminError);
    else void loadAndRenderAdmin();
  } else if (latestData) {
    renderPublic(latestData);
  } else {
    void loadAndRenderPublic();
  }
}

function bindAdminInteractions() {
  bindPublicInteractions();
  document.getElementById('refresh-dashboard')?.addEventListener('click', () => void loadAndRenderAdmin());
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
  publishDiagnostics();
  const bindRunAction = async (button: HTMLButtonElement, action: () => Promise<unknown>, idleLabel: string) => {
    const original = button.textContent || idleLabel;
    button.textContent = 'Running…';
    button.disabled = true;
    try {
      await action();
      button.textContent = 'Done';
    } catch (error) {
      button.textContent = error instanceof Error ? 'Failed' : 'Unavailable';
    } finally {
      setTimeout(() => { button.textContent = original; button.disabled = false; }, 1600);
    }
  };

  document.querySelectorAll<HTMLButtonElement>('[data-ai-opt-run]').forEach((button) => {
    button.addEventListener('click', () => { const runId = button.dataset.aiOptRun; if (!runId) return; void bindRunAction(button, () => api.runAiOptimization(runId), 'Run AI Optimization'); });
  });
  document.querySelectorAll<HTMLButtonElement>('[data-analytics-init]').forEach((button) => {
    button.addEventListener('click', () => { const runId = button.dataset.analyticsInit; if (!runId) return; void bindRunAction(button, () => api.initializeAnalytics(runId), 'Initialize Analytics'); });
  });
  document.querySelectorAll<HTMLButtonElement>('[data-intel-backfill]').forEach((button) => {
    button.addEventListener('click', () => { const runId = button.dataset.intelBackfill; if (!runId) return; void bindRunAction(button, () => api.backfillIntelligence(runId), 'Backfill Intelligence'); });
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
      if (location.pathname !== '/pipeline-runs') navigate('/pipeline-runs');
      if (input && runId) input.value = runId;
      if (runId) await loadRun(runId);
    });
  });
  document.querySelectorAll<HTMLAnchorElement>('a[data-router-link]').forEach((link) => {
    link.addEventListener('click', (event) => {
      const href = link.getAttribute('href');
      if (!href || href.startsWith('http')) return;
      event.preventDefault();
      navigate(href);
    });
  });
}

async function loadAndRenderAdmin() {
  renderLoading('Loading AstroPulse telemetry…');
  try {
    const data = await loadDashboardData();
    renderAdmin(data, data.apiError);
  } catch {
    renderAdmin(emptyDashboardData(), 'Analytics service temporarily unavailable.');
  }
}

async function loadAndRenderPublic() {
  renderLoading('Loading AstroPulse sky guides…');
  try {
    renderPublic(await loadPublicPortalData());
  } catch {
    renderPublic(emptyDashboardData());
  }
}

window.addEventListener('popstate', () => {
  if (isAdminRoute()) {
    if (latestAdminData) renderAdmin(latestAdminData, latestAdminError);
  } else if (latestData) {
    renderPublic(latestData);
  }
});
window.addEventListener('hashchange', () => {
  if (isAdminRoute()) {
    if (latestAdminData) renderAdmin(latestAdminData, latestAdminError);
  }
});

if (isAdminRoute() && !adminPathPages.has(location.pathname.replace(/\/+$/, '') || '/') && !location.pathname.startsWith('/admin') && !location.pathname.startsWith('/dashboard/')) {
  navigate('/dashboard');
} else if (isAdminRoute()) {
  void loadAndRenderAdmin();
} else {
  void loadAndRenderPublic();
}
