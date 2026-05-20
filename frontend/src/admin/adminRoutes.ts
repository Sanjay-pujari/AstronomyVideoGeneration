import type { PageKey } from './dashboard.js';

const validPages = new Set<PageKey>(['dashboard', 'pipeline-runs', 'regions', 'events', 'alerts', 'analytics', 'ai-optimization', 'optimization-insights', 'content-calendar', 'settings']);

export function isAdminRoute(pathname = typeof location !== 'undefined' ? location.pathname : '/') {
  const path = pathname.replace(/\/+$/, '') || '/';
  return path === '/admin' || path.startsWith('/admin/') || path === '/dashboard' || path.startsWith('/dashboard/');
}

export function normalizeAdminPath(pathname: string) {
  const path = pathname.replace(/\/+$/, '') || '/';
  if (path === '/dashboard') return '/admin/dashboard';
  if (path.startsWith('/dashboard/')) return `/admin/${path.slice('/dashboard/'.length)}`;
  if (path === '/admin') return '/admin/dashboard';
  return path;
}

export function resolveAdminPage(pathname: string, hash: string): { page: PageKey; unknownPage?: string } {
  const hashPage = hash.replace(/^#\/?/, '').trim().toLowerCase();
  if (hashPage) return validPages.has(hashPage as PageKey) ? { page: hashPage as PageKey } : { page: 'dashboard', unknownPage: hashPage };
  const normalized = normalizeAdminPath(pathname);
  const page = normalized.replace('/admin/', '');
  return validPages.has(page as PageKey) ? { page: page as PageKey } : { page: 'dashboard', unknownPage: page };
}
