import type { PageKey } from './dashboard.js';

const validPages = new Set<PageKey>(['dashboard', 'pipeline-runs', 'regions', 'events', 'alerts', 'analytics', 'ai-optimization', 'optimization-insights', 'content-calendar', 'settings', 'tonights-sky', 'videos', 'about']);

export function resolveDashboardPage(pathname: string, hash: string): { page: PageKey; unknownPage?: string } {
  const hashPage = hash.replace(/^#\/?/, '').trim().toLowerCase();
  if (hashPage) {
    return validPages.has(hashPage as PageKey) ? { page: hashPage as PageKey } : { page: 'dashboard', unknownPage: hashPage };
  }
  const cleanPath = pathname.replace(/\/+$/, '') || '/';
  if (cleanPath === '/admin' || cleanPath === '/dashboard' || cleanPath === '/admin/dashboard') return { page: 'dashboard' };
  const page = cleanPath.startsWith('/admin/') ? cleanPath.slice('/admin/'.length) : cleanPath.startsWith('/dashboard/') ? cleanPath.slice('/dashboard/'.length) : cleanPath.slice(1);
  return validPages.has(page as PageKey) ? { page: page as PageKey } : { page: 'dashboard', unknownPage: page || cleanPath };
}
