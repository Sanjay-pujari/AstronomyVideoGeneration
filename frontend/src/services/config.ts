declare global {
  interface Window {
    ASTROPULSE_API_BASE_URL?: string;
    ASTROPULSE_API_TIMEOUT_MS?: number;
  }
}

const DEFAULT_LOCAL_API_BASE_URL = 'https://localhost:59235';
const DEFAULT_PRODUCTION_API_BASE_URL = 'https://api.astropulse.example';
const DEFAULT_TIMEOUT_MS = 12_000;

export function getApiBaseUrl() {
  const configured = typeof window !== 'undefined' ? window.ASTROPULSE_API_BASE_URL : undefined;
  const fallback = typeof location !== 'undefined' && location.hostname !== 'localhost' && location.hostname !== '127.0.0.1'
    ? DEFAULT_PRODUCTION_API_BASE_URL
    : DEFAULT_LOCAL_API_BASE_URL;
  return (configured || fallback).replace(/\/$/, '');
}

export function getApiTimeoutMs(): number {
  const configured = typeof window !== 'undefined' ? window.ASTROPULSE_API_TIMEOUT_MS : undefined;
  return typeof configured === 'number' && Number.isFinite(configured) && configured > 0 ? configured : DEFAULT_TIMEOUT_MS;
}

export const API_BASE_URL = getApiBaseUrl();
export const API_TIMEOUT_MS = getApiTimeoutMs();
