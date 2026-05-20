import { adminApi, adminActions } from './adminApi.js';
import { normalizeAdminPath, resolveAdminPage } from './adminRoutes.js';
import { renderDashboardHtml } from './dashboard.js';
import type { DashboardData } from '../services/api.js';

export function startAdminApp(root: HTMLElement) {
  if (location.pathname.startsWith('/dashboard')) {
    history.replaceState({}, '', normalizeAdminPath(location.pathname));
  }
  let latest: DashboardData | undefined;
  const render = (data: DashboardData) => {
    latest = data;
    const route = resolveAdminPage(location.pathname, location.hash);
    root.innerHTML = renderDashboardHtml(data, { page: route.page, warning: route.unknownPage ? `Unknown dashboard page: ${route.unknownPage}` : undefined });
  };
  document.addEventListener('click', (event) => {
    const el = (event.target as HTMLElement).closest('a[data-router-link]') as HTMLAnchorElement | null;
    if (!el) return;
    const href = el.getAttribute('href');
    if (!href || href.startsWith('http')) return;
    event.preventDefault();
    history.pushState({}, '', normalizeAdminPath(href));
    if (latest) render(latest);
  });
  void adminApi.loadDashboardData().then(render);
  void adminActions.getOpsSummary();
}
