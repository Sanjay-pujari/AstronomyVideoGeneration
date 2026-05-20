export const PUBLIC_ROUTES = ['/', '/tonights-sky', '/regions', '/events', '/videos', '/alerts', '/about'] as const;

export function isPublicRoute(pathname: string) {
  const path = pathname.replace(/\/+$/, '') || '/';
  return path === '/' || path === '/tonights-sky' || path.startsWith('/regions/') || path === '/events' || path.startsWith('/events/') || path === '/videos' || path === '/alerts' || path === '/about';
}
