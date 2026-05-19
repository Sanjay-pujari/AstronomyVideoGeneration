import type { PageKey } from './dashboard.js';

const validPages = new Set<PageKey>(['dashboard', 'pipeline-runs', 'regions', 'events', 'alerts', 'analytics', 'ai-optimization', 'optimization-insights', 'content-calendar', 'settings', 'tonights-sky', 'videos', 'about']);

/** Admin paths that do not overlap with public portal routes (/, /events, /alerts, etc.). */
const adminOnlyPaths = new Set([
  '/dashboard',
  '/pipeline-runs',
  '/regions',
  '/analytics',
  '/ai-optimization',
  '/optimization-insights',
  '/content-calendar',
  '/settings'
]);

export function normalizePathname(pathname: string) {
  return pathname.replace(/\/+$/, '') || '/';
}

export function isAdminRoute(pathname = typeof location !== 'undefined' ? location.pathname : '/') {
  const path = normalizePathname(pathname);
  if (path === '/admin' || path.startsWith('/admin/')) return true;
  if (path === '/dashboard' || path.startsWith('/dashboard/')) return true;
  return adminOnlyPaths.has(path);
}

export function resolveDashboardPage(pathname: string, hash: string): { page: PageKey; unknownPage?: string } {
  const hashPage = hash.replace(/^#\/?/, '').trim().toLowerCase();
  if (hashPage) {
    return validPages.has(hashPage as PageKey) ? { page: hashPage as PageKey } : { page: 'dashboard', unknownPage: hashPage };
  }
  const cleanPath = normalizePathname(pathname);
  if (cleanPath === '/admin' || cleanPath === '/dashboard' || cleanPath === '/admin/dashboard') return { page: 'dashboard' };
  const page = cleanPath.startsWith('/admin/') ? cleanPath.slice('/admin/'.length) : cleanPath.startsWith('/dashboard/') ? cleanPath.slice('/dashboard/'.length) : cleanPath.slice(1);
  return validPages.has(page as PageKey) ? { page: page as PageKey } : { page: 'dashboard', unknownPage: page || cleanPath };
}
