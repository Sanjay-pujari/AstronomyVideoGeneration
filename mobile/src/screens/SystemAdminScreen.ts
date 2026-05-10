import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import type { MobileHomeData } from '../services/api.js';

export function createSystemAdminScreen(data: MobileHomeData): MobileScreenModel {
  return createScreen('System / Admin Lite', 'Operational health without exposing credentials or token values', [
    createCard('Scheduler status', `Scheduler is ${data.scheduler.state}`, [
      { title: data.scheduler.isEnabled ? 'Enabled' : 'Disabled', detail: [`next ${data.scheduler.nextRunAt ?? 'unknown'}`, `last ${data.scheduler.lastRunAt ?? 'unknown'}`].join(' • '), status: data.scheduler.state }
    ], 'idle', 'system'),
    createCard('Token health', 'Provider health only; token values are never displayed.', data.tokenHealth.map((token) => ({
      title: token.provider,
      detail: token.expiresAt ?? token.message,
      status: token.status
    })), data.tokenHealth.length ? 'idle' : 'empty', 'system'),
    createCard('Latest pipeline runs', data.pipelineRuns.length ? undefined : 'No pipeline run list is available from the dashboard payload.', data.pipelineRuns.map((run) => ({
      title: run.runId,
      detail: [run.stage, run.updatedAt].filter(Boolean).join(' • '),
      status: run.status
    })), data.pipelineRuns.length ? 'idle' : 'empty', 'system'),
    createCard('Platform publish status', data.platformStatuses.length ? undefined : 'No platform publish status is available from the dashboard payload.', data.platformStatuses.map((platform) => ({
      title: platform.platform,
      detail: [platform.itemId, platform.publishedAt].filter(Boolean).join(' • '),
      status: platform.status
    })), data.platformStatuses.length ? 'idle' : 'empty', 'system')
  ]);
}
