declare global {
  interface Window { ASTROPULSE_API_BASE_URL?: string }
}

const DEFAULT_LOCAL_API_BASE_URL = 'http://localhost:8080';
const DEFAULT_PRODUCTION_API_BASE_URL = 'https://api.astropulse.example';

export function getApiBaseUrl() {
  const configured = typeof window !== 'undefined' ? window.ASTROPULSE_API_BASE_URL : undefined;
  const fallback = typeof location !== 'undefined' && location.hostname !== 'localhost' ? DEFAULT_PRODUCTION_API_BASE_URL : DEFAULT_LOCAL_API_BASE_URL;
  return (configured || fallback).replace(/\/$/, '');
}

export const API_BASE_URL = getApiBaseUrl();
