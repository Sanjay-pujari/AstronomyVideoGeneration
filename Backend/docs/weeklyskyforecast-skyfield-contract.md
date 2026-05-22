# WeeklySkyForecast Skyfield Contract

## Purpose and production-parity guardrails

This document defines the **request/response data contract only** for the `WeeklySkyForecast` category, aligned with the production-parity principles used by DailySkyGuide v2.

Guardrails:
- Do **not** modify `POST /api/pipelines/run`.
- Do **not** modify DailySkyGuide v2 behavior.
- Do **not** implement production pipeline code in this phase.
- Reuse existing output directory conventions and downstream pipeline services.

Category:
- `WeeklySkyForecast`

Category purpose:
- Generate a 7-day astronomy forecast for a given region, including week overview, moon phases, planet visibility, best viewing windows, notable events, recommended nights, and short-form highlight inputs.

---

## 1) Skyfield request contract

### Contract name
`WeeklySkyForecastSkyfieldRequest`

### Fields

| Field | Type | Required | Default | Notes |
|---|---|---:|---|---|
| `regionId` | `string` | Yes | - | Region key used across planning, rendering, and metadata. |
| `locationName` | `string` | Yes | - | Human-readable location label for narration/metadata. |
| `latitude` | `double` | Yes | - | Decimal degrees, WGS84. |
| `longitude` | `double` | Yes | - | Decimal degrees, WGS84. |
| `timezone` | `string` | Yes | - | IANA timezone (e.g., `Asia/Kolkata`). |
| `weekStartDate` | `DateOnly` | Yes | - | Inclusive week start date in local calendar for the selected timezone. |
| `days` | `int` | No | `7` | Must be `7` for v1 parity. |
| `language` | `string` | Yes | - | ISO language code for downstream script/metadata generation (e.g., `en`). |
| `preferredObjectCodes` | `List<string>?` | No | `null` / `[]` | Optional ranking hint for object prioritization. |
| `includeMoonPhases` | `bool` | No | `true` | Include moon phase summaries and moon visibility data. |
| `includePlanets` | `bool` | No | `true` | Include planetary visibility candidates. |
| `includeDeepSkyObjects` | `bool` | No | `true` | Include deep sky objects if visible and relevant. |
| `includeMeteorShowers` | `bool` | No | `true` | Include meteor shower windows/events when applicable. |
| `includeConjunctions` | `bool` | No | `true` | Include conjunction events and scoring. |
| `includeBestViewingWindows` | `bool` | No | `true` | Include computed best observation windows per day. |

### Validation notes (contract-level)
- `weekStartDate` + `days` define the forecast range: `[weekStartDate, weekStartDate + days - 1]`.
- `timezone` should be persisted in response for stable local-time transformations in rendering/narration.
- `days != 7` should be treated as out-of-contract for WeeklySkyForecast v1.

### Example request

```json
{
  "regionId": "IN-RJ-UDAIPUR",
  "locationName": "Udaipur",
  "latitude": 24.5854,
  "longitude": 73.7125,
  "timezone": "Asia/Kolkata",
  "weekStartDate": "2026-05-22",
  "days": 7,
  "language": "en",
  "preferredObjectCodes": [],
  "includeMoonPhases": true,
  "includePlanets": true,
  "includeDeepSkyObjects": true,
  "includeMeteorShowers": true,
  "includeConjunctions": true,
  "includeBestViewingWindows": true
}
```

---

## 2) Skyfield response contract

### Contract name
`WeeklySkyForecastSkyfieldResponse`

### Root fields

| Field | Type | Required | Notes |
|---|---|---:|---|
| `success` | `bool` | Yes | Indicates whether forecast generation succeeded. |
| `regionId` | `string` | Yes | Echo from request for pipeline correlation. |
| `locationName` | `string` | Yes | Echo/normalized value for display. |
| `timezone` | `string` | Yes | IANA timezone used for local transformations. |
| `weekStartDate` | `DateOnly` | Yes | Inclusive start date. |
| `weekEndDate` | `DateOnly` | Yes | Inclusive end date. |
| `days` | `List<DailySkyForecastItem>` | Yes | Daily forecast records for the requested 7-day range. |
| `weeklyHighlights` | `List<WeeklyHighlightItem>` | Yes | Ranked highlights for segmenting, shorts, thumbnails. |
| `recommendedNights` | `List<RecommendedObservationNight>` | Yes | Ranked observation nights with rationale and windows. |
| `warnings` | `List<string>` | Yes | Non-fatal issues/coverage gaps. |
| `errorMessage` | `string?` | No | Present when `success=false`. |

### Child contracts

#### `DailySkyForecastItem`
- `DateOnly date`
- `DateTime sunsetUtc`
- `DateTime sunriseUtc`
- `string moonPhase`
- `double moonIlluminationPercent`
- `DateTime? moonRiseUtc`
- `DateTime? moonSetUtc`
- `List<VisibleObjectForecastItem> visibleObjects`
- `List<AstronomyEventForecastItem> events`
- `DateTime bestViewingStartUtc`
- `DateTime bestViewingEndUtc`
- `double overallViewingScore`
- `string viewingSummary`

#### `VisibleObjectForecastItem`
- `string objectCode`
- `string objectName`
- `string objectType`
- `bool visible`
- `DateTime? riseUtc`
- `DateTime? setUtc`
- `DateTime? transitUtc`
- `double? maxAltitudeDegrees`
- `DateTime? bestViewingTimeUtc`
- `double visibilityScore`
- `double photographyScore`
- `string viewingDirection`
- `string reason`

#### `AstronomyEventForecastItem`
- `string eventType`
- `string title`
- `string description`
- `DateTime eventTimeUtc`
- `double importanceScore`
- `double viralityScore`
- `string? primaryObjectCode`
- `string viewingDirection`
- `string viewingTip`

#### `WeeklyHighlightItem`
- `int order`
- `string highlightType`
- `string title`
- `string description`
- `DateOnly date`
- `DateTime? bestTimeUtc`
- `string? objectCode`
- `double score`
- `string suggestedSceneType`

#### `RecommendedObservationNight`
- `DateOnly date`
- `double score`
- `string reason`
- `List<string> bestObjects`
- `DateTime bestStartUtc`
- `DateTime bestEndUtc`

### Example response skeleton

```json
{
  "success": true,
  "regionId": "IN-RJ-UDAIPUR",
  "locationName": "Udaipur",
  "timezone": "Asia/Kolkata",
  "weekStartDate": "2026-05-22",
  "weekEndDate": "2026-05-28",
  "days": [
    {
      "date": "2026-05-22",
      "sunsetUtc": "2026-05-22T13:40:00Z",
      "sunriseUtc": "2026-05-23T00:05:00Z",
      "moonPhase": "Waxing Crescent",
      "moonIlluminationPercent": 27.4,
      "moonRiseUtc": "2026-05-22T08:42:00Z",
      "moonSetUtc": "2026-05-22T22:15:00Z",
      "visibleObjects": [],
      "events": [],
      "bestViewingStartUtc": "2026-05-22T15:15:00Z",
      "bestViewingEndUtc": "2026-05-22T19:30:00Z",
      "overallViewingScore": 78.2,
      "viewingSummary": "Clear post-evening window with strong planetary visibility."
    }
  ],
  "weeklyHighlights": [
    {
      "order": 1,
      "highlightType": "BestNight",
      "title": "Best all-round sky quality",
      "description": "Peak darkness with multiple bright objects visible.",
      "date": "2026-05-24",
      "bestTimeUtc": "2026-05-24T17:45:00Z",
      "objectCode": "JUP",
      "score": 92.1,
      "suggestedSceneType": "BestObservationNight"
    }
  ],
  "recommendedNights": [
    {
      "date": "2026-05-24",
      "score": 92.1,
      "reason": "Low moonlight and high object diversity.",
      "bestObjects": ["JUP", "SAT", "M13"],
      "bestStartUtc": "2026-05-24T16:50:00Z",
      "bestEndUtc": "2026-05-24T20:10:00Z"
    }
  ],
  "warnings": [],
  "errorMessage": null
}
```

---

## 3) Segment mapping (response -> video planning)

## Long video segment plan (6-8 segments)
1. Weekly intro
   - Sources: `weeklyHighlights` (top narrative anchor), week range, location.
2. Moon phase forecast
   - Sources: `days[].moonPhase`, `days[].moonIlluminationPercent`, moon rise/set trends.
3. Best planets of the week
   - Sources: `days[].visibleObjects` filtered by `objectType=Planet`, ranked by `visibilityScore`/`photographyScore`.
4. Best observation nights
   - Sources: `recommendedNights` + `days[].bestViewingStartUtc/bestViewingEndUtc`.
5. Notable events
   - Sources: `days[].events` ranked by `importanceScore` and `viralityScore`.
6. Astrophotography tip
   - Sources: object `photographyScore`, `viewingDirection`, event timing.
7. Weekly summary/outro
   - Sources: top `weeklyHighlights`, summary of best night + top object + CTA.

## Short video segment plan (3-5 segments)
1. Biggest weekly highlight
   - Source: `weeklyHighlights[0]`.
2. Best night to watch
   - Source: `recommendedNights[0]`.
3. Top planet/object
   - Source: highest-ranked item from `visibleObjects` over the week.
4. Quick reminder/CTA
   - Source: cautionary notes from `warnings` + best viewing window reminder.

---

## 4) SSC scene mapping strategy

WeeklySkyForecast should produce **5-8 SSC scripts**.

Minimum scene set:
1. Weekly wide sky intro
2. Moon phase highlight night
3. Best planet night
4. Best observation night
5. Notable event scene
6. Weekly summary sky map
7. Thumbnail candidate scene

Each scene contract record should include:
- `captureTimeUtc`
- `targetObjectCode` (nullable)
- `fov`
- `sceneType`
- `outputRole` (e.g., `LongSegment`, `ShortSegment`, `ThumbnailCandidate`)
- `dateLabel` metadata (week day/date label for overlays)

Suggested planner mapping:
- Derive `captureTimeUtc` from highlight `bestTimeUtc` or daily best viewing midpoint.
- Use `suggestedSceneType` in `WeeklyHighlightItem` to hint scene template selection.
- Ensure at least one scene is short-safe and one scene is thumbnail-safe.

---

## 5) Thumbnail strategy

### Long thumbnail
- Composition: weekly sky collage.
- Content anchors: moon phase state + best planet + week date range label.
- Style preset: `CinematicWeeklySkyCollage`.

### Short thumbnail
- Composition: strongest weekly highlight (single dominant subject/event).
- Text: minimal, high-contrast.
- Framing: vertical-safe composition.

### Category-aware source assets
- Thumbnail module should accept category-aware candidate scene assets from WeeklySkyForecast SSC outputs.
- Prefer `ThumbnailCandidate` scenes with highest `score` + clear subject isolation.

---

## 6) Metadata strategy

Metadata payload should include:
- `title`
- `description`
- `tags`
- `region`
- `weekDateRange`
- `visibleObjects`
- `events`
- `language`
- `category = WeeklySkyForecast`

Suggested derivation rules:
- Title uses region + strongest `weeklyHighlights` item + week range.
- Description summarizes recommended nights and top events.
- Tags aggregate object codes/types + event types + regional keyword.

---

## 7) Output path convention

Reuse Rendering:WorkingDirectory convention:

`/media-output/{categoryName}/{date}/{regionId}/{pipelineRunId}`

For WeeklySkyForecast:

`Rendering:WorkingDirectory/WeeklySkyForecast/{weekStartDate}/{regionId}/{pipelineRunId}`

Example:

`D:/AstronomyWorkspace/Astronomy/media-output/WeeklySkyForecast/2026-05-22/in-rj-udaipur/{pipelineRunId}`

Normalization notes:
- `categoryName` must be `WeeklySkyForecast`.
- `date` token maps to `weekStartDate` (ISO `yyyy-MM-dd`).
- `regionId` path segment should be slug/lowercase normalized for filesystem safety.

---

## 8) Out of scope (this phase)

- No API/controller changes.
- No pipeline orchestration implementation.
- No rendering/audio/thumbnail engine implementation.
- No DailySkyGuide v2 modifications.

This document is intentionally limited to defining the data contract and category-to-output mapping needed before implementation.
