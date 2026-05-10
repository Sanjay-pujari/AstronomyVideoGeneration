const DEFAULT_LOCAL_API_BASE_URL = 'http://localhost:8080';
const DEFAULT_PRODUCTION_API_BASE_URL = 'https://api.astropulse.example';
const DEFAULT_TIMEOUT_MS = 10000;

const TRUE_VALUES = new Set(['1', 'true', 'yes', 'on']);

declare const process: { env?: Record<string, string | undefined> };

export function getNodeEnv() {
  return process.env?.NODE_ENV ?? 'development';
}

export function getApiBaseUrl() {
  const configured = process.env?.EXPO_PUBLIC_API_BASE_URL;
  const fallback = getNodeEnv() === 'production' ? DEFAULT_PRODUCTION_API_BASE_URL : DEFAULT_LOCAL_API_BASE_URL;
  return (configured || fallback).replace(/\/$/, '');
}

export function getApiTimeoutMs() {
  const configured = Number(process.env?.EXPO_PUBLIC_API_TIMEOUT_MS);
  return Number.isFinite(configured) && configured > 0 ? configured : DEFAULT_TIMEOUT_MS;
}

export function isMockModeEnabled() {
  if (getNodeEnv() === 'production') return false;
  return TRUE_VALUES.has((process.env?.EXPO_PUBLIC_MOCK_MODE ?? '').toLowerCase());
}

export const API_BASE_URL = getApiBaseUrl();
export const API_TIMEOUT_MS = getApiTimeoutMs();
export const MOCK_MODE_ENABLED = isMockModeEnabled();
