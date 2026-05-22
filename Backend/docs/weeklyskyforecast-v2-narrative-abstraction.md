# WeeklySkyForecast v2 Phase 1D — Narrative Abstraction Layer

## Philosophy
Narrative abstraction converts event-centric output into story-centric output. The system must optimize for viewer experience, emotional pacing, and cinematic clarity rather than repeating raw event metadata.

**Think in:** story concepts, emotional progression, watchability, and audience promise.

**Do not think in:** per-day event duplication, repeated object labels, repeated dates as separate stories.

## Story-Centric Architecture
Pipeline order:

Astronomy Intelligence → Event Intelligence → Editorial Intelligence → Cinematic Editorial Refinement → **Narrative Abstraction**.

Narrative Abstraction is the final storytelling layer before narration planning.

## Event-to-Story Conversion Rules
1. Treat repeated object groups across multiple nights as one human narrative concept.
2. Keep source event IDs for traceability, but rewrite text in human spoken language.
3. Convert mechanical labels into cinematic titles and spoken hooks.
4. Preserve safety wording when angular separation data is unavailable.

## Repetition Collapse Rules
- Collapse repeated grouping events into one hero narrative concept.
- Do not create one narrative beat per repeated date.
- Do not create repeated grouping shorts.
- Do not create repeated grouping visual moments.

## Cinematic Pacing Rules
Narrative flow must follow 6–7 progressive beats:
1. Hook
2. Hero sky story
3. Why this week matters
4. Best observation night
5. Emotional moon/planet highlight
6. Photography/viewing recommendation
7. Closing CTA

## Beat Uniqueness Rules
- Beat purpose must be unique.
- Beat emotional intent must progress.
- Beat visual intent must not duplicate prior beat intent.

## Visual Uniqueness Rules
- Maximum 4–5 visual concepts.
- Every visual concept requires unique `visualUniquenessKey`.
- Grouping visuals must not repeat with only date changes.

## Emotional Storytelling Rules
- Voice should sound like human narration, not a report.
- Emphasize curiosity, payoff, and confidence.
- Maintain one coherent weekly story, not disconnected event bullets.

## Hook Writing Rules
- Spoken-language opening sentence.
- Curiosity-driven and cinematic.
- Avoid template/report tone.

Forbidden metadata phrases in headline/hook:
- same viewing window grouping
- grouping event
- visibility momentum
- backup opportunities
- observation event
- visibility priority

## Thumbnail Emotional Direction Rules
Thumbnail direction must describe an emotional composition narrative, including:
- hero object hierarchy,
- depth layering,
- light contrast,
- viewer emotional reaction.

Example style:
“A glowing Moon above twilight, with bright planets stepping toward the horizon to create cinematic depth.”

## Wording Safety
Without angular separation data, use only safe phrasing such as:
- share the same evening sky
- visible together after sunset
- dominate the western sky
- appear in the same viewing window

Avoid unsafe claims unless angular separation supports them:
- conjunction
- exact alignment
- rare alignment
- nearly touching
- extremely close
