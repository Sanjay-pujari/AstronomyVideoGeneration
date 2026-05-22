# WeeklySkyForecast v2 Phase 1B: Editorial Intelligence Layer

## Editorial Philosophy
Events > Objects. WeeklySkyForecast v2 answers: "what is the most exciting sky story this week?" It does not enumerate isolated object visibility.

## Events Over Objects Rule
- Good: "Moon, Venus, and Jupiter share the western sky this week."
- Bad: "Moon is visible. Venus is visible. Jupiter is visible."

## Hero Event Rules
- Select one hero event using weighted score:
  - 0.35 storyScore + 0.30 visualScore + 0.20 importanceScore + 0.15 rarityScore.
- Priority order: planetary_grouping > moon_planet_pairing > best_overall_night > best_planet > best_moon_night > photography_window.
- Boosts: Moon involvement, bright planets (Jupiter/Venus/Saturn), recommended-night overlap, Hybrid strategy.

## Story Deduplication Rules
- Collapse repeated events by eventType + normalized object set.
- Produce one editorial event with:
  - peakDate (highest score)
  - supportingDates (all appearances)
  - bestTimeUtc (from peak date)
  - editorialized title and merged description.

## Headline Generation Rules
- Must include hero objects when possible.
- Must sound cinematic/human, not template-like.
- Must avoid generic placeholders.

## Emotional Pacing Rules
- Start with curiosity and urgency.
- Elevate to hero reveal.
- Ground with practical timing/viewing guidance.
- Close with clear recommendation.

## Narrative Arc Rules
- 5-7 beats required.
- Mandatory beat intent: Hook, Hero event, Best night, Moon/planet focus, Viewing or photography tip, Closing recommendation.

## Cinematic Moment Rules
- Max 5 unique moments.
- Avoid duplicate visuals.
- Allow reuse when one visual supports multiple beats.
- Strategy defaults: Hybrid for grouping, CelestialAsset for planet/moon hero, Stellarium for wide-sky context.

## Thumbnail Direction Rules
- Derived from hero event.
- Prefer 1-2 primary visual objects plus optional secondary support.
- Avoid clutter and overpopulation.
- Usually Hybrid strategy.

## Visual Strategy Philosophy
- Use strategy as editorial intent, not just rendering mechanics.
- Hero grouping => Hybrid emotional composition.
- Planet/moon identity beat => CelestialAsset focus.
- Observation-night context beat => Stellarium wide framing.

## Wording Safety
If source is `same_window_grouping_only_no_angular_separation`, use language such as:
- "share the same evening viewing window"
- "appear in the same part of the sky"
- "form the week's strongest visual grouping"

Avoid conjunction/alignment claims unless angular separation evidence exists.
