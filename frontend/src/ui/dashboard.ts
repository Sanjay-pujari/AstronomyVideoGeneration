import type { AstroEvent, DashboardData, MediaItem, PipelineRun, Region } from '../services/api.js';

function escapeHtml(value: unknown) {
  return String(value ?? '').replace(/[&<>'"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[char]!);
}

function formatDate(value?: string) {
  if (!value) return 'Not scheduled';
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
}

function statusBadge(status?: string) {
  const normalized = (status ?? 'unknown').toLowerCase();
  const tone = ['healthy', 'published', 'running', 'completed', 'ready', 'enabled', 'live'].includes(normalized)
    ? 'good'
    : ['warning', 'queued', 'rendering', 'processing', 'scheduled'].includes(normalized)
      ? 'warn'
      : ['error', 'failed', 'disabled', 'expired', 'blocked'].includes(normalized)
        ? 'bad'
        : 'neutral';
  return `<span class="status-badge status-badge--${tone}">${escapeHtml(status ?? 'Unknown')}</span>`;
}

function card(title: string, subtitle: string, body: string, extraClass = '') {
  return `<section class="card ${extraClass}"><div class="card__header"><div><h2>${escapeHtml(title)}</h2><p>${escapeHtml(subtitle)}</p></div></div>${body}</section>`;
}

function mediaList(items: MediaItem[]) {
  if (!items.length) return '<div class="state state--empty">No generated media yet. Completed runs will appear here.</div>';
  return `<div class="stack-list">${items.map((item) => `<article class="list-row"><div><h3>${escapeHtml(item.title)}</h3><p>${escapeHtml([item.regionName, item.platform, item.durationSeconds ? `${item.durationSeconds}s` : undefined].filter(Boolean).join(' • '))}</p></div>${statusBadge(item.status)}</article>`).join('')}</div>`;
}

function eventList(events: AstroEvent[]) {
  if (!events.length) return '<div class="state state--empty">No upcoming events returned by the API.</div>';
  return `<div class="stack-list">${events.map((event) => `<article class="list-row"><div><h3>${escapeHtml(event.title)}</h3><p>${escapeHtml([event.eventType, event.regionName, formatDate(event.startsAt)].filter(Boolean).join(' • '))}</p></div>${event.priority ? `<strong>${event.priority}</strong>` : ''}</article>`).join('')}</div>`;
}

function regionList(regions: Region[]) {
  return `<div class="stack-list">${regions.map((region) => `<div class="list-row"><div><h3>${escapeHtml(region.name)}</h3><p>${escapeHtml(region.timezone ?? 'Timezone not configured')}</p></div><button class="secondary-button" data-region-run="${escapeHtml(region.id)}">Manual run</button></div>`).join('')}</div>`;
}

function pipelineViewer(run?: PipelineRun) {
  return `<div class="pipeline-viewer"><label for="run-id">Run ID</label><input id="run-id" value="${escapeHtml(run?.runId ?? '')}" placeholder="run-id" /><button class="secondary-button" id="load-run">Load run</button><div id="pipeline-result">${run ? `<div class="status-panel">${statusBadge(run.status)}<p>Stage: ${escapeHtml(run.stage ?? 'Not reported')}</p><p>Region: ${escapeHtml(run.regionName ?? run.regionId ?? 'Not reported')}</p><p>${escapeHtml(run.message ?? 'No pipeline message.')}</p></div>` : '<div class="state state--empty">Enter a run ID to view pipeline status.</div>'}</div></div>`;
}

export function renderDashboardHtml(data: DashboardData, options: { error?: string } = {}) {
  const allEvents = [...data.events, ...data.upcomingEvents, ...data.topEvents];
  return `
    <main class="app-shell">
      <header class="hero"><div><span class="eyebrow">AstroPulse Mission Control</span><h1>Tonight’s astronomy content pipeline at a glance.</h1><p>Track generated videos, shorts, publishing, events, regions, scheduler health, and active pipeline runs from one portal.</p></div><button class="primary-button" id="refresh-dashboard">Refresh dashboard</button></header>
      ${options.error ? `<div class="state state--error" role="alert"><strong>Signal lost.</strong><span>${escapeHtml(options.error)} Showing safe mock telemetry.</span></div>` : ''}
      <section class="metric-grid" aria-label="Analytics summary">
        ${card('Views', 'Recent total', `<strong>${(data.analytics.views ?? 0).toLocaleString()}</strong>`)}
        ${card('Watch time', 'Minutes', `<strong>${(data.analytics.watchTimeMinutes ?? 0).toLocaleString()}</strong>`)}
        ${card('Subscribers', 'Net gained', `<strong>${(data.analytics.subscribersGained ?? 0).toLocaleString()}</strong>`)}
        ${card('Engagement', data.analytics.topPlatform ?? 'All platforms', `<strong>${data.analytics.engagementRate ?? '—'}%</strong>`)}
      </section>
      <section class="dashboard-grid">
        ${card('Latest generated videos', 'Long-form astronomy episodes', mediaList(data.latestVideos))}
        ${card('Latest shorts/reels', 'Short-form previews', mediaList(data.latestShorts))}
        ${card('Platform publish status', 'Sanitized provider status only', `<div class="stack-list">${data.publishStatuses.map((status) => `<div class="list-row"><div><h3>${escapeHtml(status.platform)}</h3><p>${escapeHtml(status.lastPublishedAt ? `Last publish ${formatDate(status.lastPublishedAt)}` : status.message ?? 'Waiting for activity')}</p></div>${statusBadge(status.status)}</div>`).join('')}</div>`)}
        ${card('Scheduler status', 'Automation heartbeat', `<div class="status-panel">${statusBadge(data.scheduler.state)}<p>Next run: ${formatDate(data.scheduler.nextRunAt)}</p><p>Last run: ${formatDate(data.scheduler.lastRunAt)}</p><p>${data.scheduler.isEnabled ? 'Scheduler enabled' : 'Scheduler disabled'}</p></div>`)}
        ${card('Token health summary', 'No secrets displayed', `<div class="stack-list">${data.tokenHealth.map((token) => `<div class="list-row"><div><h3>${escapeHtml(token.provider)}</h3><p>${escapeHtml(token.expiresAt ? `Expires ${formatDate(token.expiresAt)}` : token.message ?? 'No expiry reported')}</p></div>${statusBadge(token.status)}</div>`).join('')}</div>`)}
        ${card('Regions', 'Manual pipeline controls', regionList(data.regions))}
        ${card('Event list', 'Upcoming and top-ranked sky events', eventList(allEvents))}
        ${card('Pipeline status viewer', 'Inspect a known run', pipelineViewer(data.pipelineRuns[0]))}
      </section>
    </main>`;
}
