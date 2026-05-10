# Notification API gaps

Phase 10D adds frontend-only sky alert signup, alert card actions, and admin preview UX. The UI does **not** create production subscriptions because alert-specific backend APIs are not present.

Required backend contracts:

- `POST /api/alerts/subscribe` — create a public subscription after explicit user consent. Request should include region, language, event types, preferred local alert time, and selected channels only.
- `GET /api/alerts/preferences/{userId}` — return saved preferences for an authenticated or otherwise privacy-safe user reference.
- `PUT /api/alerts/preferences/{userId}` — update preferences without exposing tokens, provider secrets, or internal queue data.
- `GET /api/alerts/upcoming` — return upcoming alert candidates suitable for public/admin preview: visible planets, full moon/supermoon, meteor showers, eclipses, special event videos, and daily sky guide reminders.
- `POST /api/alerts/test` — send a safe test alert for admins only, with authorization and audit logging.

Until these exist, `/alerts` validates preferences locally and `/admin/alerts` shows a friendly unavailable warning plus mock queue placeholders.
