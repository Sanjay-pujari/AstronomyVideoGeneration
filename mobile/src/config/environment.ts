const DEFAULT_LOCAL_API_BASE_URL = 'http://localhost:8080';
const DEFAULT_PRODUCTION_API_BASE_URL = 'https://api.astropulse.example';

declare const process: { env?: Record<string, string | undefined> };

export function getApiBaseUrl() {
  const configured = process.env?.EXPO_PUBLIC_API_BASE_URL;
  const fallback = process.env?.NODE_ENV === 'production' ? DEFAULT_PRODUCTION_API_BASE_URL : DEFAULT_LOCAL_API_BASE_URL;
  return (configured || fallback).replace(/\/$/, '');
}

export const API_BASE_URL = getApiBaseUrl();
