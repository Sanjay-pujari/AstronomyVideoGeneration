# WeeklySkyForecast v2 Phase 1: Event Intelligence Layer

## Purpose
Transform raw weekly Skyfield output into meaningful weekly astronomy story signals for planning-only use.

## Event Types
- best_overall_night
- best_planet
- best_moon_night
- dark_sky_opportunity
- planetary_grouping
- moon_planet_pairing
- photography_window
- visibility_peak

## Scoring Rules
- **importanceScore**: weighted by visibility, object count, moon inclusion, bright-planet inclusion, and recommended-night alignment.
- **visualScore**: Moon/Jupiter/Venus/Saturn and grouping events are prioritized; dark sky medium; low-visibility ignored.
- **storyScore**: grouping > best night > best planet > isolated object.
- **rarityScore**: moderate default; slightly elevated for grouping-type events.

## Story Extraction Rules
- Build 6–8 primary events maximum.
- Always include best overall night when available.
- Include best planet and best moon when available.
- Add grouping events only when same-window criteria are met.

## Visual Strategy Rules
- Grouping/orientation events => `Hybrid`.
- Best observation night => `Stellarium`.
- Best planet hero => `CelestialAsset`.
- Best moon hero => `CelestialAsset`.
- Dark sky opportunity => `Stellarium` or `CelestialAsset` based on scene purpose.
- If no useful visual context => `None`.

## Deduplication Rules
- De-duplicate by `(eventType, date, objectSet)`.
- Keep highest `storyScore` when duplicates occur.

## Narrative Flow Rules
Weekly story arc should follow:
1. Hook / weekly headline
2. Main sky event
3. Best night recommendation
4. Moon or planet highlight
5. Photography/viewing tip
6. Closing CTA

## Grouping & Relationship Guardrails
- Same-window grouping criteria: visible objects, same date, same/nearby direction bucket, bestViewingTime within 90 minutes.
- Without angular separation data, output **same viewing window grouping** labels only.
- Do **not** claim conjunctions unless explicit angular separation is present in source data.
