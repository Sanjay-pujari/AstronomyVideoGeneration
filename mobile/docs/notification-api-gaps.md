# Notification API gaps

Phase 10D adds mobile alert and preference screen models only. There is no push-token registration and no production subscription flow.

Required backend contracts:

- `POST /api/alerts/subscribe` — create a public subscription after explicit user consent. Request should include region, language, event types, preferred local alert time, and selected channels only.
- `GET /api/alerts/preferences/{userId}` — return saved preferences for a privacy-safe user reference.
- `PUT /api/alerts/preferences/{userId}` — update preferences without exposing push tokens, provider secrets, or internal queue data.
- `GET /api/alerts/upcoming` — return notification-ready alert candidates for visible planets, full moon/supermoon, meteor showers, eclipses, special event videos, and daily sky guide reminders.
- `POST /api/alerts/test` — admin-only test alert endpoint with authorization and audit logging.

Until these exist, the mobile app exposes local notification-ready toggles and local placeholders only.
