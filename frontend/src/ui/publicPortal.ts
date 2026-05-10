import type { AstroEvent, DashboardData, MediaItem, Region } from '../services/api.js';
import { escapeHtml } from './dashboard.js';

export type PublicPageKey = 'home' | 'tonight' | 'events' | 'videos' | 'region' | 'about';

const SOCIAL_LINKS = [
  { label: 'YouTube', href: 'https://www.youtube.com/@AstroPulse' },
  { label: 'Instagram', href: 'https://www.instagram.com/astropulse' },
  { label: 'Facebook', href: 'https://www.facebook.com/AstroPulse' }
];

const PUBLIC_NAV = [
  { label: 'Home', href: '/' },
  { label: "Tonight's Sky", href: '/tonight' },
  { label: 'Events', href: '/events' },
  { label: 'Videos', href: '/videos' },
  { label: 'About', href: '/about' }
];

export type PublicRoute = {
  page: PublicPageKey;
  regionId?: string;
};

export function parsePublicRoute(pathname: string): PublicRoute {
  const path = pathname.replace(/\/+$/, '') || '/';
  if (path === '/tonight') return { page: 'tonight' };
  if (path === '/events') return { page: 'events' };
  if (path === '/videos') return { page: 'videos' };
  if (path === '/about') return { page: 'about' };
  const regionMatch = path.match(/^\/regions\/([^/]+)$/);
  if (regionMatch) return { page: 'region', regionId: decodeURIComponent(regionMatch[1]) };
  return { page: 'home' };
}

function formatDate(value?: string) {
  if (!value) return 'Date to be announced';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'full', timeStyle: 'short' }).format(date);
}

function itemDate(item: MediaItem) {
  return item.publishedAt ?? item.publishedUtc ?? item.createdAt ?? item.collectedUtc;
}

function eventDate(event: AstroEvent) {
  return event.startsAt ?? event.startUtc;
}

function regionLabel(region?: Region | string) {
  if (!region) return 'Global skywatchers';
  if (typeof region === 'string') return region;
  return region.displayName ?? region.name ?? region.id;
}

function contentType(item: MediaItem) {
  return String(item.contentType ?? item.platformContentType ?? '').toLowerCase();
}

function isShort(item: MediaItem) {
  const type = contentType(item);
  return type.includes('short') || type.includes('reel') || type.includes('story') || (item.durationSeconds !== undefined && item.durationSeconds <= 90);
}

function isReel(item: MediaItem) {
  return contentType(item).includes('reel') || String(item.platform ?? '').toLowerCase().includes('instagram');
}

function eventKey(event: AstroEvent) {
  return `${event.title} ${event.id}`.toLowerCase();
}

function regionKey(region?: string) {
  return String(region ?? '').toLowerCase();
}

function safeExternalUrl(url?: string) {
  if (!url) return undefined;
  try {
    const parsed = new URL(url);
    if (!['http:', 'https:'].includes(parsed.protocol)) return undefined;
    parsed.search = '';
    parsed.hash = '';
    return parsed.toString();
  } catch {
    return undefined;
  }
}

function socialLinks() {
  return `<div class="public-socials" aria-label="AstroPulse social channels">${SOCIAL_LINKS.map((link) => `<a href="${escapeHtml(link.href)}" target="_blank" rel="noopener noreferrer">Follow on ${escapeHtml(link.label)}</a>`).join('')}</div>`;
}

function publicNav(active: PublicPageKey) {
  return `<nav class="public-nav" aria-label="Public pages"><a class="brand" href="/">AstroPulse</a><div>${PUBLIC_NAV.map((link) => {
    const key = link.href === '/' ? 'home' : link.href.slice(1);
    return `<a class="nav-link ${key === active ? 'nav-link--active' : ''}" href="${link.href}">${escapeHtml(link.label)}</a>`;
  }).join('')}</div></nav>`;
}

function setSeo(title: string, description: string) {
  if (typeof document === 'undefined') return;
  document.title = title;
  let descriptionMeta = document.querySelector<HTMLMetaElement>('meta[name="description"]');
  if (!descriptionMeta) {
    descriptionMeta = document.createElement('meta');
    descriptionMeta.name = 'description';
    document.head.appendChild(descriptionMeta);
  }
  descriptionMeta.content = description;
}

function emptyState(message = 'Content coming soon. Check back after the next AstroPulse sky guide is published.') {
  return `<div class="state state--empty public-empty">${escapeHtml(message)}</div>`;
}

function regionSelector(regions: Region[], selectedId?: string) {
  if (!regions.length) return emptyState('Regional sky guides are coming soon.');
  return `<label class="region-picker">Choose your region <select data-region-selector>${regions.map((region) => `<option value="${escapeHtml(region.id)}" ${region.id === selectedId ? 'selected' : ''}>${escapeHtml(regionLabel(region))}</option>`).join('')}</select></label>`;
}

function mediaCard(item: MediaItem) {
  const safe = safeExternalUrl(item.url ?? item.previewUrl);
  const meta = [item.platform, item.regionName ?? item.locationName, itemDate(item) ? formatDate(itemDate(item)) : undefined].filter(Boolean).join(' • ');
  return `<article class="public-card media-card"><span class="eyebrow">${escapeHtml(item.platform ?? 'AstroPulse')}</span><h3>${escapeHtml(item.title)}</h3><p>${escapeHtml(meta || 'Published astronomy content')}</p>${safe ? `<a class="safe-link" href="${escapeHtml(safe)}" target="_blank" rel="noopener noreferrer">Watch now</a>` : '<span class="muted">Watch link coming soon</span>'}</article>`;
}

function mediaGrid(items: MediaItem[], emptyMessage?: string) {
  return items.length ? `<div class="public-grid">${items.map(mediaCard).join('')}</div>` : emptyState(emptyMessage);
}

function eventCard(event: AstroEvent, videos: MediaItem[] = []) {
  const details = [event.eventType, event.regionName, event.visibility, eventDate(event) ? formatDate(eventDate(event)) : undefined].filter(Boolean).join(' • ');
  const matches = videos.filter((item) => eventKey(event).split(' ').some((part) => part.length > 3 && item.title.toLowerCase().includes(part))).slice(0, 2);
  return `<article class="public-card event-card"><span class="eyebrow">${escapeHtml(event.eventType ?? 'Sky event')}</span><h3>${escapeHtml(event.title)}</h3><p>${escapeHtml(details || 'Event details coming soon.')}</p>${matches.length ? `<div class="mini-links">${matches.map((item) => `<a href="${escapeHtml(safeExternalUrl(item.url ?? item.previewUrl) ?? '/videos')}">Related video: ${escapeHtml(item.title)}</a>`).join('')}</div>` : '<span class="muted">Special event videos coming soon</span>'}</article>`;
}

function eventGrid(events: AstroEvent[], media: MediaItem[], emptyMessage?: string) {
  return events.length ? `<div class="public-grid">${events.map((event) => eventCard(event, media)).join('')}</div>` : emptyState(emptyMessage);
}

function latestGuide(data: DashboardData, regionId?: string) {
  const region = data.regions.find((item) => item.id === regionId);
  const regionName = regionLabel(region);
  const candidates = [...data.latestVideos, ...(data.analytics.topContent ?? [])]
    .filter((item) => !isShort(item))
    .filter((item) => !regionId || regionKey(item.regionName ?? item.locationName).includes(regionKey(regionName)) || regionKey(item.regionName ?? item.locationName).includes(regionKey(regionId)))
    .sort((a, b) => new Date(itemDate(b) ?? 0).getTime() - new Date(itemDate(a) ?? 0).getTime());
  return candidates[0];
}

function homePage(data: DashboardData) {
  const latestVideos = data.latestVideos.slice(0, 3);
  const shortVideos = data.latestShorts.slice(0, 3);
  return `<header class="public-hero"><div><span class="eyebrow">Location-aware astronomy</span><h1>Explore tonight's sky with AstroPulse.</h1><p>Friendly astronomy guides, event previews, long videos, shorts, and reels tailored by region and date.</p>${socialLinks()}</div><div class="hero-panel"><h2>Find your sky</h2>${regionSelector(data.regions)}<a class="primary-button" href="/tonight">Open Tonight's Sky</a></div></header><section class="public-section"><h2>Latest sky guides</h2>${mediaGrid(latestVideos, 'Latest sky guides are coming soon.')}</section><section class="public-section"><h2>Latest shorts and reels</h2>${mediaGrid(shortVideos, 'Short-form sky updates are coming soon.')}</section><section class="public-section"><h2>Upcoming sky events</h2>${eventGrid(data.upcomingEvents.slice(0, 3), [...latestVideos, ...shortVideos], 'Upcoming astronomy events are coming soon.')}</section>`;
}

function tonightPage(data: DashboardData, selectedRegionId?: string) {
  const selected = selectedRegionId ?? data.regions[0]?.id;
  const region = data.regions.find((item) => item.id === selected);
  const guide = latestGuide(data, selected);
  const shorts = data.latestShorts.filter((item) => !selected || regionKey(item.regionName ?? item.locationName).includes(regionKey(regionLabel(region))) || regionKey(item.regionName ?? item.locationName).includes(regionKey(selected))).slice(0, 4);
  return `<header class="public-page-heading"><span class="eyebrow">Tonight's Sky</span><h1>${escapeHtml(regionLabel(region))}</h1><p>Choose a region to see the latest AstroPulse DailySkyGuide and short-form viewing links.</p>${regionSelector(data.regions, selected)}</header><section class="dashboard-grid"><section class="public-card card--full"><h2>Latest DailySkyGuide</h2>${guide ? mediaCard(guide) : emptyState('DailySkyGuide content coming soon for this region.')}<h3>Visible objects summary</h3>${emptyState('Visible object summaries need a public DailySkyGuide details API before they can be shown safely.')}</section><section class="public-card card--full"><h2>Watch shorts and reels</h2>${mediaGrid(shorts, 'Short and reel links are coming soon for this region.')}</section></section>`;
}

function eventsPage(data: DashboardData) {
  const events = [...data.upcomingEvents, ...data.topEvents].filter((event, index, all) => all.findIndex((item) => item.id === event.id) === index);
  return `<header class="public-page-heading"><span class="eyebrow">Astronomy events</span><h1>Upcoming sky events</h1><p>Browse meteor showers, conjunctions, lunar moments, and other highlighted viewing opportunities.</p></header>${eventGrid(events, [...data.latestVideos, ...data.latestShorts], 'Upcoming astronomy events are coming soon.')}`;
}

function videosPage(data: DashboardData) {
  const all = [...data.latestVideos, ...data.latestShorts, ...(data.analytics.topContent ?? [])].filter((item, index, items) => items.findIndex((candidate) => candidate.id === item.id) === index);
  const longVideos = all.filter((item) => !isShort(item));
  const shorts = all.filter((item) => isShort(item) && !isReel(item));
  const reels = all.filter(isReel);
  return `<header class="public-page-heading"><span class="eyebrow">Videos</span><h1>AstroPulse video library</h1><p>Filter by region, platform, or event using the labels on each published item.</p></header><section class="filter-chips" aria-label="Video filters">${['All regions', 'YouTube', 'Instagram', 'Facebook', 'Events'].map((label) => `<span>${label}</span>`).join('')}</section><section class="public-section"><h2>Long videos</h2>${mediaGrid(longVideos, 'Long-form videos are coming soon.')}</section><section class="public-section"><h2>Shorts</h2>${mediaGrid(shorts, 'Shorts are coming soon.')}</section><section class="public-section"><h2>Reels</h2>${mediaGrid(reels, 'Reels are coming soon.')}</section>`;
}

function regionPage(data: DashboardData, regionId?: string) {
  const region = data.regions.find((item) => item.id === regionId);
  if (!region) return `<header class="public-page-heading"><span class="eyebrow">Region</span><h1>Region not found</h1><p>Choose an available region below.</p>${regionSelector(data.regions)}</header>`;
  const name = regionLabel(region);
  const content = [...data.latestVideos, ...data.latestShorts, ...(data.analytics.topContent ?? [])]
    .filter((item) => regionKey(item.regionName ?? item.locationName).includes(regionKey(name)) || regionKey(item.regionName ?? item.locationName).includes(regionKey(region.id)))
    .filter((item, index, items) => items.findIndex((candidate) => candidate.id === item.id) === index)
    .slice(0, 6);
  const events = [...data.upcomingEvents, ...data.topEvents].filter((event) => !event.regionId || event.regionId === region.id || regionKey(event.regionName).includes(regionKey(name)));
  return `<header class="public-page-heading"><span class="eyebrow">Regional sky guide</span><h1>${escapeHtml(name)}</h1><p>Local timezone: ${escapeHtml(region.timezone ?? 'Coming soon')} • Language: ${escapeHtml(region.language ?? 'Coming soon')}</p></header><section class="public-section"><h2>Latest content for ${escapeHtml(name)}</h2>${mediaGrid(content, 'Regional content coming soon.')}</section><section class="public-section"><h2>Upcoming events</h2>${eventGrid(events, content, 'Regional events coming soon.')}</section>`;
}

function aboutPage() {
  return `<header class="public-page-heading"><span class="eyebrow">About AstroPulse</span><h1>Public astronomy education, tuned to your location.</h1><p>AstroPulse helps viewers understand what is visible tonight, why it matters, and where to watch concise educational astronomy videos.</p>${socialLinks()}</header><section class="public-grid"><article class="public-card"><h2>Location-aware guides</h2><p>Regional pages organize content by timezone, language, date, and sky events so viewers can plan around their local night sky.</p></article><article class="public-card"><h2>Educational purpose</h2><p>AstroPulse explains astronomy in approachable language for learners, families, classrooms, and curious skywatchers.</p></article><article class="public-card"><h2>Social viewing</h2><p>Watch long-form guides on YouTube and short updates on Instagram and Facebook without exposing internal operations data.</p></article></section>`;
}

function shell(route: PublicRoute, body: string) {
  return `<main class="app-shell public-shell">${publicNav(route.page)}${body}<footer class="public-footer"><span>© AstroPulse</span>${socialLinks()}</footer></main>`;
}

export function renderPublicPortalHtml(data: DashboardData, route: PublicRoute = { page: 'home' }) {
  const region = route.regionId ? data.regions.find((item) => item.id === route.regionId) : undefined;
  const title = route.page === 'region' && region
    ? `${regionLabel(region)} Sky Guide | AstroPulse`
    : route.page === 'events'
      ? 'Upcoming Astronomy Events | AstroPulse'
      : route.page === 'tonight'
        ? "Tonight's Sky | AstroPulse"
        : route.page === 'videos'
          ? 'Astronomy Videos, Shorts & Reels | AstroPulse'
          : route.page === 'about'
            ? 'About AstroPulse | Public Astronomy Guides'
            : 'AstroPulse | Tonight’s Sky Guides by Region';
  const description = route.page === 'region' && region
    ? `Latest astronomy content, local events, timezone, and language for ${regionLabel(region)}.`
    : 'Explore public AstroPulse astronomy guides by region, date, event, and social video platform.';
  setSeo(title, description);
  const body = route.page === 'tonight'
    ? tonightPage(data, route.regionId)
    : route.page === 'events'
      ? eventsPage(data)
      : route.page === 'videos'
        ? videosPage(data)
        : route.page === 'region'
          ? regionPage(data, route.regionId)
          : route.page === 'about'
            ? aboutPage()
            : homePage(data);
  return shell(route, body);
}
