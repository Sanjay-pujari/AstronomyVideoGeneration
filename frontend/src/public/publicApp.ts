import { publicApi } from './publicApi.js';
import { parsePublicRoute, renderPublicPortalHtml } from './publicPortal.js';
import type { DashboardData } from '../services/api.js';

export function startPublicApp(root: HTMLElement) {
  let latest: DashboardData | undefined;
  const render = (data: DashboardData) => {
    latest = data;
    root.innerHTML = renderPublicPortalHtml(data, parsePublicRoute(location.pathname));
  };
  const navigate = (path: string) => {
    if (location.pathname !== path) history.pushState({}, '', path);
    if (latest) render(latest);
  };
  document.addEventListener('click', (event) => {
    const el = (event.target as HTMLElement).closest('a[data-router-link]') as HTMLAnchorElement | null;
    if (!el) return;
    const href = el.getAttribute('href');
    if (!href || href.startsWith('http') || href.startsWith('/admin')) return;
    event.preventDefault();
    navigate(href);
  });
  void publicApi.loadPublicPortalData().then(render);
}
