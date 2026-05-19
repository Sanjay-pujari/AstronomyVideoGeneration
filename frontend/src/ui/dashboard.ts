import type { AstroEvent, DashboardData, JsonRecord, MediaItem, PipelineRun, PipelineStage, PublishStatus, Region, SchedulerSchedule, TokenHealthItem } from '../services/api.js';

export type PageKey = 'dashboard' | 'pipeline-runs' | 'regions' | 'events' | 'alerts' | 'analytics' | 'ai-optimization' | 'optimization-insights' | 'content-calendar' | 'settings' | 'tonights-sky' | 'videos' | 'about';

const PAGES: Array<{ key: PageKey; label: string }> = [
  { key: 'dashboard', label: 'Dashboard' },
  { key: 'pipeline-runs', label: 'Pipeline Runs' },
  { key: 'regions', label: 'Regions' },
  { key: 'events', label: 'Events' },
  { key: 'alerts', label: 'Alerts' },
  { key: 'analytics', label: 'Analytics' },
  { key: 'ai-optimization', label: 'AI Optimization' },
  { key: 'optimization-insights', label: 'Optimization Insights' },
  { key: 'content-calendar', label: 'Content Calendar' },
  { key: 'settings', label: 'Settings' },
  { key: 'tonights-sky', label: "Tonight's Sky" },
  { key: 'videos', label: 'Videos' },
  { key: 'about', label: 'About' }
];

export function escapeHtml(value: unknown) {
  return String(value ?? '').replace(/[&<>'"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[char]!);
}

function formatDate(value?: string) {
  if (!value) return 'Not scheduled';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

function formatNumber(value?: number) {
  return Number(value ?? 0).toLocaleString();
}

function formatOptionalNumber(value?: number) {
  return value === undefined || Number.isNaN(value) ? '—' : Number(value).toLocaleString();
}

function seconds(value?: number) {
  if (!value) return '—';
  if (value < 60) return `${Math.round(value)}s`;
  return `${Math.round(value / 60)}m`;
}

function statusBadge(status?: string | boolean | null) {
  const label = typeof status === 'boolean' ? (status ? 'enabled' : 'disabled') : (status ?? 'unknown');
  const normalized = String(label).toLowerCase();
  const tone = ['healthy', 'published', 'running', 'completed', 'ready', 'enabled', 'live', 'success', 'accepted', 'valid'].includes(normalized)
    ? 'good'
    : ['warning', 'queued', 'rendering', 'processing', 'scheduled', 'pending', 'unknown'].includes(normalized)
      ? 'warn'
      : ['error', 'failed', 'disabled', 'expired', 'blocked', 'invalid', 'notfound'].includes(normalized)
        ? 'bad'
        : 'neutral';
  return `<span class="status-badge status-badge--${tone}">${escapeHtml(label)}</span>`;
}

function card(title: string, subtitle: string, body: string, extraClass = '') {
  return `<section class="card ${extraClass}"><div class="card__header"><div><h2>${escapeHtml(title)}</h2><p>${escapeHtml(subtitle)}</p></div></div>${body}</section>`;
}

function emptyState(message: string) {
  return `<div class="state state--empty">${escapeHtml(message)}</div>`;
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

function externalLink(url?: string) {
  const safe = safeExternalUrl(url);
  if (!safe) return '';
  return `<a class="safe-link" href="${escapeHtml(safe)}" target="_blank" rel="noopener noreferrer">Open published URL</a>`;
}

function nav(activePage: PageKey) {
  return `<nav class="top-nav" aria-label="AstroPulse pages">${PAGES.map((page) => `<a class="nav-link ${page.key === activePage ? 'nav-link--active' : ''}" href="#${page.key}">${page.label}</a>`).join('')}</nav>`;
}

function hero(activePage: PageKey) {
  const label = PAGES.find((page) => page.key === activePage)?.label ?? 'Dashboard';
  return `<header class="hero"><div><span class="eyebrow">AstroPulse Mission Control</span><h1>${escapeHtml(label)}</h1><p>Production operations dashboard for astronomy video generation, publishing, scheduling, event ranking, and platform analytics.</p></div><button class="primary-button" id="refresh-dashboard">Refresh telemetry</button></header>`;
}

function shell(activePage: PageKey, body: string, error?: string) {
  const message = error || '';
  return `<main class="app-shell">${nav(activePage)}${hero(activePage)}${message ? `<div class="state state--error" role="alert"><strong>Analytics service temporarily unavailable.</strong><span>${escapeHtml(message)}</span></div>` : ''}${body}</main>`;
}

function metric(label: string, value: string, hint: string) {
  return card(label, hint, `<strong>${escapeHtml(value)}</strong>`, 'metric-card');
}

function systemHealth(data: DashboardData) {
  const health = data.systemHealth;
  const checks = [
    ['FFmpeg', health.ffmpegConfigured],
    ['Stellarium', health.stellariumConfigured],
    ['Skyfield sidecar', health.skyfieldSidecarReachable],
    ['Blob storage', health.azureBlobStorageConfigured]
  ];
  const warnings = Array.isArray(health.warnings) ? health.warnings as string[] : [];
  return `<div class="health-grid">${checks.map(([label, value]) => `<div class="health-check"><span>${escapeHtml(label)}</span>${statusBadge(value === true ? 'healthy' : value === false ? 'warning' : 'unknown')}</div>`).join('')}</div>${warnings.length ? `<ul class="warning-list">${warnings.map((warning) => `<li>${escapeHtml(warning)}</li>`).join('')}</ul>` : ''}`;
}

function mediaList(items: MediaItem[]) {
  if (!items.length) return emptyState('Waiting for analytics data. Publish content and collect analytics to populate this card.');
  return `<div class="stack-list">${items.map((item) => {
    const publishDate = item.publishedAt ?? item.publishedUtc ?? item.createdAt;
    const details = [
      item.regionName ?? item.locationName,
      item.platform,
      publishDate ? `Published ${formatDate(publishDate)}` : undefined,
      item.durationSeconds ? `${item.durationSeconds}s` : undefined,
      item.views !== undefined ? `${formatOptionalNumber(item.views)} views` : undefined,
      item.engagement !== undefined ? `${formatOptionalNumber(item.engagement)} engagement` : undefined
    ];
    return `<article class="list-row"><div><h3>${escapeHtml(item.title)}</h3><p>${escapeHtml(details.filter(Boolean).join(' • '))}</p>${externalLink(item.url ?? item.previewUrl)}</div>${statusBadge(item.status ?? 'published')}</article>`;
  }).join('')}</div>`;
}

function publishStatusList(items: PublishStatus[]) {
  if (!items.length) return emptyState('No platform publish status returned yet.');
  return `<div class="stack-list">${items.map((item) => `<article class="list-row"><div><h3>${escapeHtml(item.platform)}</h3><p>${escapeHtml(item.lastPublishedAt ? `Last publish ${formatDate(item.lastPublishedAt)}` : item.message ?? 'Waiting for activity')}</p>${externalLink(item.url)}</div>${statusBadge(item.status)}</article>`).join('')}</div>`;
}

function schedulerPanel(data: DashboardData) {
  const scheduler = data.scheduler;
  const schedules = scheduler.schedules ?? [];
  return `<div class="status-panel">${statusBadge(scheduler.enabled ?? scheduler.isEnabled ?? scheduler.state ?? 'unknown')}<p>Queued: ${formatNumber(scheduler.queuedRuns)} • Active: ${formatNumber(scheduler.activeRuns)} • Max concurrent: ${formatNumber(scheduler.maxConcurrentRuns)}</p><p>Next run: ${formatDate(scheduler.nextRunAt ?? scheduler.nextPlannedRun ?? schedules[0]?.nextPlannedRunUtc)}</p><p>Last run: ${formatDate(scheduler.lastRunAt ?? scheduler.recentRuns?.[0]?.actualRunUtc)}</p>${schedules.length ? `<div class="mini-table">${schedules.slice(0, 5).map((schedule) => `<div><span>${escapeHtml(schedule.name)}</span><span class="row-actions">${statusBadge(schedule.enabled)}<button class="secondary-button" data-schedule-run="${escapeHtml(schedule.name)}">Run schedule</button></span></div>`).join('')}</div>` : emptyState('No scheduler schedules returned.')}</div>`;
}

function tokenPanel(items: TokenHealthItem[]) {
  if (!items.length) return emptyState('No token-health providers returned.');
  return `<div class="stack-list">${items.map((item) => {
    const status = item.status ?? (item.isValid === true ? 'healthy' : item.isValid === false ? 'error' : 'unknown');
    return `<article class="list-row"><div><h3>${escapeHtml(item.provider ?? item.platform ?? 'Provider')}</h3><p>${escapeHtml(item.expiresAt ? `Expires ${formatDate(item.expiresAt)}` : item.message ?? item.error ?? item.accountName ?? 'No expiry reported')}</p></div>${statusBadge(status)}</article>`;
  }).join('')}</div>`;
}

function runId(run: PipelineRun) {
  return run.runId ?? run.pipelineRunId ?? run.id ?? '';
}

function runStatus(run: PipelineRun) {
  return run.status ?? run.runStatus ?? 'unknown';
}

function runRows(runs: PipelineRun[]) {
  if (!runs.length) return `<tr><td colspan="6">No recent pipeline runs returned.</td></tr>`;
  return runs.map((run) => `<tr><td><code>${escapeHtml(runId(run))}</code></td><td>${escapeHtml(run.regionName ?? run.locationName ?? run.regionId ?? '—')}</td><td>${escapeHtml(run.contentType ?? '—')}</td><td>${statusBadge(runStatus(run))}</td><td>${formatDate(run.startedAt ?? run.startedUtc)}</td><td><button class="secondary-button" data-load-run="${escapeHtml(runId(run))}">Details</button></td></tr>`).join('');
}

function stageTimeline(stages: PipelineStage[] = []) {
  if (!stages.length) return emptyState('Stage timeline is not loaded. Select a run details button or enter a run id.');
  return `<ol class="timeline">${stages.map((stage) => `<li><div>${statusBadge(stage.status)}<h3>${escapeHtml(stage.stageName)}</h3><p>${escapeHtml([stage.startedUtc ? `Started ${formatDate(stage.startedUtc)}` : undefined, stage.completedUtc ? `Completed ${formatDate(stage.completedUtc)}` : undefined, stage.attemptCount ? `Attempt ${stage.attemptCount}/${stage.maxAttempts ?? '?'}` : undefined].filter(Boolean).join(' • '))}</p>${stage.lastError ? `<p class="error-text">${escapeHtml(stage.lastError)}</p>` : ''}</div></li>`).join('')}</ol>`;
}

function publishedUrls(urls: string[] = []) {
  if (!urls.length) return emptyState('No published URLs reported for this run.');
  return `<div class="stack-list">${urls.map((url) => `<div class="list-row"><span>${escapeHtml(safeExternalUrl(url) ?? 'Unsafe or unsupported URL hidden')}</span>${externalLink(url)}</div>`).join('')}</div>`;
}

function eventList(events: AstroEvent[]) {
  if (!events.length) return emptyState('No events returned by the API.');
  return `<div class="stack-list">${events.map((event) => `<article class="list-row"><div><h3>${escapeHtml(event.title)}</h3><p>${escapeHtml([event.eventType, event.regionName ?? event.regionId, formatDate(event.startsAt ?? event.startUtc), event.visibility].filter(Boolean).join(' • '))}</p></div><div class="badge-stack">${event.status ? statusBadge(event.status) : ''}<strong class="score-pill">${escapeHtml(event.score ?? event.priority ?? '—')}</strong></div></article>`).join('')}</div>`;
}

function regionList(regions: Region[], schedules: SchedulerSchedule[]) {
  if (!regions.length) return emptyState('No regions returned by the API.');
  return `<div class="stack-list">${regions.map((region) => {
    const id = region.id ?? region.displayName ?? region.name;
    const schedule = schedules.find((item) => item.regionId === id || item.locationName === region.name || item.locationName === region.displayName);
    return `<article class="list-row"><div><h3>${escapeHtml(region.displayName ?? region.name)}</h3><p>${escapeHtml([region.timezone, region.language, region.localRunTime ? `Run ${region.localRunTime}` : undefined, schedule?.nextPlannedRunUtc ? `Next ${formatDate(schedule.nextPlannedRunUtc)}` : undefined].filter(Boolean).join(' • ') || 'Region configuration available')}</p></div><div class="row-actions">${statusBadge(region.enabled ?? schedule?.enabled ?? 'unknown')}<button class="secondary-button" data-region-run="${escapeHtml(id)}">Run now</button></div></article>`;
  }).join('')}</div>`;
}

function analyticsPage(data: DashboardData) {
  const overall = data.analyticsDashboard.overallSummary ?? {};
  const platformBreakdown = data.analyticsDashboard.platformBreakdown ?? [];
  const regionBreakdown = data.analyticsDashboard.regionBreakdown ?? [];
  const topContent = data.analytics.topContent ?? [];
  const hasAnalytics = platformBreakdown.length > 0 || topContent.length > 0 || Number(overall.totalContentPublished ?? 0) > 0;
  return `<section class="metric-grid">${metric('Total views', hasAnalytics ? formatOptionalNumber(data.analytics.totalViews ?? data.analytics.views) : '—', 'Views')}${metric('CTR', formatOptionalNumber(Number(data.analyticsSummaryCards.ctr ?? 0)), 'Click-through rate')}${metric('Engagement', hasAnalytics ? formatOptionalNumber(data.analytics.totalEngagement) : '—', 'Across platforms')}${metric('Best platform', data.analytics.bestPlatform || data.analytics.topPlatform || '—', 'Top performer')}</section><section class="dashboard-grid">${card('Platform comparison', 'Views, CTR, engagement by platform', platformBreakdown.length ? `<div class="stack-list">${platformBreakdown.map((item) => `<div class="list-row"><div><h3>${escapeHtml(item.platform)}</h3><p>${escapeHtml(`${formatOptionalNumber(Number(item.totalViews))} views • CTR ${formatOptionalNumber(Number(item.ctr ?? 0))}% • Engagement ${formatOptionalNumber(Number(item.averageEngagement ?? item.averageEngagementRate ?? 0))}%`)}</p></div></div>`).join('')}</div>` : emptyState('Waiting for analytics data.'))}${card('Video analytics', 'Per-run video performance', data.analyticsVideoBreakdown.length ? `<pre class="json-preview">${escapeHtml(JSON.stringify(data.analyticsVideoBreakdown, null, 2))}</pre>` : emptyState('No video analytics for current run yet.'))}${card('Hook performance', 'Top hook outcomes', trendPanel((data.analyticsSummaryCards.hookPerformance as JsonRecord | undefined) ?? {}))}${card('Thumbnail performance', 'Top thumbnail outcomes', trendPanel((data.analyticsSummaryCards.thumbnailPerformance as JsonRecord | undefined) ?? {}))}${card('Top content', 'Recent published content from analytics', topContent.length ? mediaList(topContent) : emptyState('Waiting for analytics data.'))}${card('Best region/platform', 'Operational winner summary', regionBreakdown.length ? `<div class="stack-list">${regionBreakdown.map((item) => `<div class="list-row"><div><h3>${escapeHtml(item.locationName ?? item.regionId ?? 'Region')}</h3><p>${escapeHtml(`${formatOptionalNumber(Number(item.views))} views • ${formatOptionalNumber(Number(item.runs))} runs`)}</p></div></div>`).join('')}</div>` : emptyState('Waiting for analytics data.'))}</section>`;
}

function trendPanel(value?: JsonRecord) {
  if (!value || !Object.keys(value).length) return emptyState('No engagement trend chart data returned.');
  return `<pre class="json-preview">${escapeHtml(JSON.stringify(value, null, 2))}</pre>`;
}

function dashboardPage(data: DashboardData) {
  const schedulerState = data.scheduler.enabled ?? data.scheduler.isEnabled ?? data.scheduler.state ?? 'unknown';
  const failures = data.ops.systemHealthSummary && typeof data.ops.systemHealthSummary === 'object' ? ((data.ops.systemHealthSummary as JsonRecord).warnings as unknown[] | undefined)?.length ?? 0 : 0;
  const publishedToday = [...data.latestVideos, ...data.latestShorts].filter((item) => item.status === 'published').length;
  const failedStages = data.pipelineRuns.filter((run) => (run.status ?? '').toLowerCase() === 'failed' || Boolean(run.failedStage || run.lastError));
  return `<section class="metric-grid">${metric('Generation status', String(schedulerState), 'Scheduler heartbeat')}${metric('Latest runs', formatNumber(data.pipelineRuns.length), 'Most recent pipeline activity')}${metric('Published today', formatNumber(publishedToday), 'Videos/shorts published today')}${metric('Warnings', formatNumber(data.warnings.length + failures), 'Needs operator review')}</section><section class="dashboard-grid">${card('Quick links', 'Open core admin dashboards', '<p><a class="safe-link" href="#analytics">Analytics Dashboard</a></p><p><a class="safe-link" href="#ai-optimization">AI Optimization</a></p><p><a class="safe-link" href="#optimization-insights">Optimization Insights</a></p><p><a class="safe-link" href="#pipeline-runs">Pipeline Runs</a></p>')}${card('Overall system health', 'Infrastructure readiness', systemHealth(data))}${card('Latest pipeline runs', 'Most recent scheduler and pipeline activity', `<div class="table-wrap"><table><thead><tr><th>Run</th><th>Region</th><th>Type</th><th>Status</th><th>Started</th><th></th></tr></thead><tbody>${runRows(data.pipelineRuns.slice(0, 5))}</tbody></table></div>`)}${card('Analytics summary cards', 'Views, CTR, engagement summary', trendPanel(data.analyticsSummaryCards))}${card('AI recommendations summary', 'Hook and publishing recommendations', `<p>Hook recommendations: ${escapeHtml(String(data.aiOptimizationByRun.length))}</p><p>Publishing recommendations: ${escapeHtml(String(data.publishingRecommendationsByRun.length))}</p>`)}${card('Failed stages', 'Any runs with failed stages or errors', failedStages.length ? `<div class="stack-list">${failedStages.map((run) => `<article class="list-row"><div><h3>${escapeHtml(runId(run))}</h3><p>${escapeHtml(run.failedStage ?? run.lastError ?? 'Failed status reported')}</p></div>${statusBadge(runStatus(run))}</article>`).join('')}</div>` : emptyState('No failed stages in current dataset.'))}${card('Platform publish status', 'Sanitized provider status only', publishStatusList(data.publishStatuses))}</section>`;
}

function runsPage(data: DashboardData) {
  const selected = data.pipelineRuns[0];
  return `<section class="dashboard-grid dashboard-grid--wide"><section class="card card--full"><div class="card__header"><div><h2>Recent pipeline runs</h2><p>Loaded from operations dashboard and scheduler history.</p></div></div><div class="table-wrap"><table><thead><tr><th>Run</th><th>Region</th><th>Type</th><th>Status</th><th>Started</th><th></th></tr></thead><tbody>${runRows(data.pipelineRuns)}</tbody></table></div></section>${card('Run details', 'Load a run to inspect status, stages, and publications', pipelineViewer(selected), 'card--full')}</section>`;
}

function aiOptimizationPage(data: DashboardData) {
  return `<section class="dashboard-grid">${card('Hook scores and recommendations', 'From AI optimization hooks endpoint', data.aiOptimizationByRun.length ? `<pre class="json-preview">${escapeHtml(JSON.stringify(data.aiOptimizationByRun, null, 2))}</pre>` : emptyState('No AI hook recommendations yet.'))}${card('Publishing recommendations', 'Recommended title/hooks, publish time, hashtags', data.publishingRecommendationsByRun.length ? `<pre class="json-preview">${escapeHtml(JSON.stringify(data.publishingRecommendationsByRun, null, 2))}</pre>` : emptyState('No publishing recommendations yet.'))}</section>`;
}

function optimizationInsightsPage(data: DashboardData) {
  return `<section class="dashboard-grid">${card('Optimization insights', 'Combined analytics and recommendation trends', trendPanel(data.analyticsSummaryCards), 'card--full')}${card('AI optimization payloads', 'Current optimization output by run', data.aiOptimizationByRun.length ? `<pre class="json-preview">${escapeHtml(JSON.stringify(data.aiOptimizationByRun, null, 2))}</pre>` : emptyState('No optimization insights yet.'))}${card('Publishing recommendation payloads', 'Current recommendation output by run', data.publishingRecommendationsByRun.length ? `<pre class="json-preview">${escapeHtml(JSON.stringify(data.publishingRecommendationsByRun, null, 2))}</pre>` : emptyState('No recommendation insights yet.'))}</section>`;
}

function contentCalendarPage(data: DashboardData) {
  return `<section class="dashboard-grid"><section class="card card--full"><div class="card__header"><div><h2>Daily planned videos</h2><p>Run plan grouped by region, language, status, and platform targets.</p></div></div><div class="table-wrap"><table><thead><tr><th>Run</th><th>Region</th><th>Language</th><th>Status</th><th>Platform targets</th></tr></thead><tbody>${data.pipelineRuns.length ? data.pipelineRuns.map((run) => `<tr><td><code>${escapeHtml(runId(run))}</code></td><td>${escapeHtml(run.regionName ?? run.regionId ?? '—')}</td><td>${escapeHtml(data.regions.find((region) => region.id === run.regionId)?.language ?? 'en')}</td><td>${statusBadge(runStatus(run))}</td><td>${escapeHtml('YouTube, Facebook, Instagram')}</td></tr>`).join('') : '<tr><td colspan=\"5\">No planned videos yet.</td></tr>'}</tbody></table></div></section></section>`;
}

function pipelineViewer(run?: PipelineRun) {
  return `<div class="pipeline-viewer"><label for="run-id">Run ID</label><div class="input-row"><input id="run-id" value="${escapeHtml(run ? runId(run) : '')}" placeholder="00000000-0000-0000-0000-000000000000" /><button class="secondary-button" id="load-run">Load run</button></div><div id="pipeline-result">${runDetails(run)}</div></div>`;
}

export function runDetails(run?: PipelineRun) {
  if (!run) return emptyState('Enter a run ID to view pipeline status.');
  return `<div class="run-detail-grid"><section><h3>Run summary</h3><div class="status-panel">${statusBadge(runStatus(run))}<p>Run: <code>${escapeHtml(runId(run))}</code></p><p>Region: ${escapeHtml(run.regionName ?? run.locationName ?? run.regionId ?? 'Not reported')}</p><p>Failed stage: ${escapeHtml(run.failedStage ?? 'None reported')}</p><p>${escapeHtml(run.lastError ?? run.message ?? 'No pipeline message.')}</p><div class="row-actions"><button class="secondary-button" data-ai-opt-run="${escapeHtml(runId(run))}">Run AI Optimization</button><button class="secondary-button" data-analytics-init="${escapeHtml(runId(run))}">Initialize Analytics</button><button class="secondary-button" data-intel-backfill="${escapeHtml(runId(run))}">Backfill Intelligence</button></div></div></section><section><h3>Published URLs</h3>${publishedUrls(run.publishedUrls)}</section><section class="detail-wide"><h3>Stage timeline</h3>${stageTimeline(run.stages)}</section></div>`;
}

function regionsPage(data: DashboardData) {
  return `<section class="dashboard-grid"><section class="card card--full"><div class="card__header"><div><h2>Regions</h2><p>Enabled status, local schedule, and safe run-now control.</p></div></div>${regionList(data.regions, data.scheduler.schedules ?? [])}</section></section>`;
}

function eventsPage(data: DashboardData) {
  return `<section class="dashboard-grid">${card('Upcoming events', 'Planned astronomical opportunities', eventList(data.upcomingEvents))}${card('Top events', 'Ranked by event score/status', eventList(data.topEvents))}${card('All dashboard events', 'Operations event context', eventList(data.events), 'card--full')}</section>`;
}


function alertCandidateList(events: AstroEvent[]) {
  if (!events.length) return emptyState('No upcoming alert candidates returned by /api/events/upcoming or /api/events/top yet.');
  return `<div class="stack-list">${events.map((event) => `<article class="list-row"><div><h3>${escapeHtml(event.title)}</h3><p>${escapeHtml([event.eventType, event.regionName ?? event.regionId, formatDate(event.startsAt ?? event.startUtc), event.visibility].filter(Boolean).join(' • '))}</p></div><div class="badge-stack">${statusBadge('candidate')}<strong class="score-pill">${escapeHtml(event.score ?? event.priority ?? '—')}</strong></div></article>`).join('')}</div>`;
}

function alertsAdminPage(data: DashboardData) {
  const candidates = [...data.upcomingEvents, ...data.topEvents].filter((event, index, all) => all.findIndex((item) => item.id === event.id) === index);
  return `<section class="dashboard-grid"><section class="card card--full"><div class="card__header"><div><h2>Sky alert subscription API</h2><p>Create, update, test, and unsubscribe email sky alerts without exposing diagnostics or SMTP secrets.</p></div>${statusBadge('api-ready')}</div><form class="alert-form" data-alert-preferences><input type="hidden" name="subscriberId" value=""><label>Email<input name="email" type="email" placeholder="viewer@example.com" required></label><label>Region<select name="regionId" required><option value="">Select a region</option>${data.regions.map((region) => `<option value="${escapeHtml(region.id)}">${escapeHtml(region.displayName ?? region.name ?? region.id)}</option>`).join('')}</select></label><label>Language<select name="language" required><option value="en">English</option><option value="hi">Hindi</option></select></label><fieldset><legend>Event types</legend>${['MeteorShower', 'FullMoon', 'Eclipse', 'SpecialEvent', 'DailySkyGuide'].map((type) => `<label class="check-row"><input type="checkbox" name="eventTypes" value="${type}" ${type === 'MeteorShower' ? 'checked' : ''}>${type}</label>`).join('')}</fieldset><label>Preferred alert time<input name="preferredAlertTimeLocal" type="time" value="18:00" required></label><label>Minimum event score<input name="minimumEventScore" type="number" min="0" max="1" step="0.01" value="0.65" required></label><div class="state state--empty" data-alert-form-state>POST /api/alerts/subscribe is ready. Save first, then test or unsubscribe the created subscriber.</div><div class="alert-actions"><button class="primary-button" type="submit">Save alert subscription</button><button class="secondary-button" type="button" data-alert-test>Send test alert</button><button class="secondary-button" type="button" data-alert-unsubscribe>Unsubscribe</button></div></form></section>${card('Upcoming alert candidates', 'Loaded from /api/alerts/upcoming with event API fallback', alertCandidateList(candidates), 'card--full')}</section>`;
}



function tonightsSkyPage(data: DashboardData) {
  return `<section class="dashboard-grid">${card("Tonight's Sky", 'Regional sky visibility and latest guide links', '<p>Use this page to review tonight sky guide coverage by region and jump to public-facing tonight pages.</p><p><a class="safe-link" href="/tonights-sky" data-router-link>Open public Tonight&amp;#39;s Sky page</a></p>')}${card('Regional readiness', 'Configured regions for tonight', regionList(data.regions, data.scheduler.schedules ?? []), 'card--full')}</section>`;
}

function videosAdminPage(data: DashboardData) {
  return `<section class="dashboard-grid">${card('Video library', 'Published long videos, shorts, and reels', mediaList([...data.latestVideos, ...data.latestShorts, ...(data.analytics.topContent ?? [])]), 'card--full')}</section>`;
}

function aboutAdminPage() {
  return `<section class="dashboard-grid">${card('About AstroPulse', 'Operations and public experience', '<p>AstroPulse produces regional astronomy videos, manages scheduling, and publishes cross-platform content.</p><p><a class="safe-link" href="/about" data-router-link>Open public About page</a></p>', 'card--full')}</section>`;
}

function settingsPage(data: DashboardData) {
  const settings = data.settingsSummary;
  const rows = [
    ['API base URL', settings.apiBaseUrl],
    ['Request timeout', `${settings.timeoutMs}ms`],
    ['Environment', settings.environment],
    ['Production API configured', settings.productionApiConfigured ? 'yes' : 'no'],
    ['Secret policy', settings.secretPolicy],
    ['AIOptimization settings', 'Read-only'],
    ['Analytics settings', 'Read-only'],
    ['Publishing targets', 'Read-only'],
    ['VideoEncoding settings', 'Read-only']
  ];
  return `<section class="dashboard-grid"><section class="card card--full"><div class="card__header"><div><h2>Settings Readonly</h2><p>Safe configuration summary only. Secrets and SAS query strings are intentionally hidden.</p></div></div><div class="settings-list">${rows.map(([label, value]) => `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`).join('')}</div><p><a class="safe-link" href="frontend-api-health.json" data-api-health-download>Download frontend-api-health.json</a></p></section></section>`;
}

export function renderDashboardHtml(data: DashboardData, options: { error?: string; page?: PageKey; warning?: string } = {}) {
  const page = options.page ?? 'dashboard';
  const body = page === 'pipeline-runs'
    ? runsPage(data)
    : page === 'regions'
      ? regionsPage(data)
      : page === 'events'
        ? eventsPage(data)
        : page === 'alerts'
          ? alertsAdminPage(data)
          : page === 'analytics'
          ? analyticsPage(data)
          : page === 'ai-optimization'
            ? aiOptimizationPage(data)
            : page === 'optimization-insights'
              ? optimizationInsightsPage(data)
            : page === 'content-calendar'
              ? contentCalendarPage(data)
          : page === 'settings'
            ? settingsPage(data)
            : page === 'tonights-sky'
              ? tonightsSkyPage(data)
              : page === 'videos'
                ? videosAdminPage(data)
                : page === 'about'
                  ? aboutAdminPage()
                  : dashboardPage(data);
  return shell(page, body, options.error) + (options.warning ? `<aside class="state state--warn" role="status">${escapeHtml(options.warning)}</aside>` : '');
}
