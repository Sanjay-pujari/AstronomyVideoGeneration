# Astronomy Documentary Blueprint

## 1. Purpose

This document defines the near-term, practical blueprint for producing high-quality Discovery/Netflix-style astronomy videos using the existing Astronomy V3 pipeline.

The goal is not to create a broad platform architecture. The goal is to turn each supported astronomy event family into a repeatable video package with:

- a clear story arc,
- accurate astronomy facts,
- cinematic but truthful visuals,
- English and Hindi narration paths,
- readable subtitles,
- platform-ready aspect ratios,
- validated publishing assets.

Astronomy V3 should first optimize for consistent execution across the first 5–6 event families before attempting longer-form or fully autonomous documentary production.

## 2. Target output types

| Output type | Practical length | Primary use | Core requirement |
| --- | ---: | --- | --- |
| Short video | 60–180 seconds | YouTube, website, app, event explainers | Fast hook, concise explanation, observation guidance, CTA. |
| Reel/short | 15–60 seconds | YouTube Shorts, Instagram Reels, Facebook Reels | One strong visual idea, one fact, one observation action, burned-in subtitles. |
| 5-minute documentary | 4–6 minutes | Main near-term premium format | Full story arc, cinematic pacing, observation guidance, science explanation, historical/cultural context. |
| 15-minute documentary | 12–18 minutes | Future extended format | Chaptered narrative, deeper science, more assets, stronger validation and editorial review. |

Near-term execution should prioritize short videos, reels/shorts, and 5-minute documentaries. The 15-minute format should remain a future extension until the 5-minute format is stable.

## 3. Documentary story arc

Every documentary should follow the same practical story arc, scaled by duration.

| Story beat | Purpose | Practical guidance |
| --- | --- | --- |
| Hook | Make the viewer care in the first seconds. | Use the most visually dramatic event fact: rarity, brightness, timing, alignment, shadow, speed, or visibility. |
| What is happening | State the event clearly. | Identify the object/event, date window, sky region, and viewer-facing summary. |
| Why it matters | Explain significance. | Mention rarity, scientific relevance, observation value, or cultural/public interest. |
| How to observe | Make it useful. | Include local time window, direction, visibility, equipment, safety notes, and weather/light-pollution caveats. |
| Science explanation | Teach the mechanism. | Explain the physical cause without overloading the viewer. Use analogies and simple diagrams where useful. |
| Historical/cultural context | Add depth and emotion. | Include one short verified historical, mythological, or cultural reference when relevant. Avoid unsupported claims. |
| Closing CTA | Convert interest into action. | Ask viewers to look up, save the date, share, subscribe, or open the sky guide. |

## 4. Scene architecture

| Scene type | Narration role | Visual role | Asset requirement | Subtitle requirement |
| --- | --- | --- | --- | --- |
| Opening hero | Deliver hook and event promise. | Cinematic hero shot or dramatic sky composition. | Hero image/video, event title, date/location metadata. | Large, high-contrast hook line; safe margins for target aspect ratio. |
| Event identification | Explain what the viewer is seeing. | Clean sky map, object labels, simple motion/position view. | Stellarium/Sky Guide capture or validated sky chart. | Short declarative captions, object names, date/time labels. |
| Observation guide | Tell the viewer how to see it. | Directional sky view, horizon cue, time window, equipment iconography. | Stellarium/Sky Guide asset, regional visibility data, safety overlay if needed. | Readable step-by-step text; avoid dense paragraphs. |
| Science explainer | Explain why it happens. | Diagram, animation, or AI cinematic visual constrained by facts. | Validated generated visual or deterministic diagram. | Key terms only; synchronize with narration. |
| Scale/context scene | Build wonder and perspective. | Planetary scale, orbit geometry, object distance, comparative visuals. | Gallery image, AI cinematic visual, or existing celestial asset. | Numbers must be verified and rounded for readability. |
| Historical/cultural scene | Add human context. | Archival-style card, constellation art, old map, cultural visual motif. | Only use verified references; avoid implying fake archival footage. | Clearly identify historical/cultural framing. |
| Recap/CTA | Close and direct action. | Final sky view, hero reprise, platform end card. | Thumbnail/hero-compatible frame and CTA graphic. | Final action line; platform-safe text size. |

## 5. Asset strategy

### Hero

- Use as the primary scroll-stopping image for the event.
- Must communicate the event family immediately: eclipse shadow, comet tail, meteor streak, lunar color, planetary pairing, or occultation geometry.
- Should be cinematic, but not misleading about what observers will actually see.
- Must support title placement, date/location footer, and platform crops.

### Thumbnail

- Produce from the hero or a stronger platform-specific composition.
- Keep text minimal: 2–5 words for English; concise Hindi phrasing for Hindi variants.
- Avoid false exaggeration such as impossible object size, fake colors presented as real, or unsupported “once in a lifetime” claims.

### Gallery

- Provide supporting visuals for longer videos and app/web content.
- Include context assets: sky position, close-up, scale comparison, observation steps, and science diagram.
- Reuse validated assets across short, reel, and 5-minute formats to reduce production cost.

### Stellarium/Sky Guide

- Use for factual sky position, direction, timing, horizon context, and observation steps.
- Prefer deterministic Stellarium/Sky Guide visuals whenever the scene answers “where do I look?” or “when can I see it?”
- Include labels only when readable at the target resolution.

### AI cinematic visuals

- Use for atmosphere, scale, scene transitions, and science explanation.
- Treat AI visuals as illustrative unless validated against event facts.
- Never allow AI visuals to invent impossible geometry, wrong object ordering, unsafe eclipse viewing behavior, or false visibility.

## 6. Language strategy

### English

- Default production path.
- Use clear documentary narration: concise, calm, vivid, and scientifically grounded.
- Avoid jargon unless immediately explained.
- Optimize thumbnails and subtitles for fast comprehension.

### Hindi

- Produce as a first-class localized version, not a literal translation afterthought.
- Use natural Hindi narration with astronomy terms explained in viewer-friendly language.
- Ensure Devanagari subtitles use validated fonts, line breaks, and safe margins.
- Keep title and thumbnail copy short enough for mobile readability.

## 7. Quality bar

| Area | Minimum bar |
| --- | --- |
| Cinematic visuals | Strong composition, smooth pacing, consistent color, no low-resolution or stretched assets. |
| Factual accuracy | Dates, times, direction, visibility, safety guidance, object names, and science claims must come from validated event context. |
| No AI hallucination | AI may phrase or illustrate, but cannot invent facts, visibility, images, cultural claims, or scientific explanations. |
| Readable subtitles | High contrast, mobile-safe size, clean line breaks, synchronized timing, English/Hindi font validation. |
| Clean voice | Natural TTS voice, stable loudness, no clipping, no awkward pauses, language-appropriate pronunciation. |
| Platform-specific aspect ratios | Produce or crop intentionally for 16:9, 9:16, and 1:1 where needed; never rely on accidental center crops. |

## 8. Current pipeline mapping

| Pipeline phase | Documentary role |
| --- | --- |
| Phase 1: Event intake | Select event family, date window, region, language, and target output type. |
| Phase 2: Astronomy data/context | Gather event facts, visibility, object metadata, and source-backed constraints. |
| Phase 3: Skyfield visibility planning | Determine night-plan, time windows, direction, altitude, and observability. |
| Phase 4: Observation context | Convert raw visibility into viewer-facing guidance. |
| Phase 5: Story/question planning | Choose hook, viewer questions, scene order, and duration. |
| Phase 6: Prompt generation | Generate narration draft, scene prompts, title ideas, and localized copy. |
| Phase 7: Validation pass | Check factual claims, forbidden terms, safety guidance, and event-family requirements. |
| Phase 8: Hero planning | Select or generate the primary cinematic event visual. |
| Phase 9: Thumbnail planning | Create platform-specific thumbnail candidates and text. |
| Phase 10: Gallery planning | Build supporting educational and cinematic asset list. |
| Phase 11: Stellarium/Sky Guide visuals | Render sky-position and observation-guide scenes. |
| Phase 12: AI cinematic visuals | Generate illustrative cinematic scenes under factual constraints. |
| Phase 13: Narration/TTS | Produce English or Hindi voice track and SSML package. |
| Phase 14: Subtitles | Generate readable, synchronized subtitles for the selected language and format. |
| Phase 15: Timeline composition | Assemble scenes, narration, transitions, subtitles, and music/ambience if available. |
| Phase 16: FFmpeg render | Render final video variants and diagnostics. |
| Phase 17: Pre-publish validation | Validate media, assets, metadata, aspect ratio, audio, subtitles, and safety/factual gates. |
| Phase 18: Publishing package | Export final video, thumbnail, title, description, tags, diagnostics, and release notes. |

## 9. Minimum release checklist for first 5–6 families

Apply this checklist before claiming the documentary pipeline is ready for a family such as meteor shower, lunar eclipse, solar eclipse, comet, planetary conjunction/grouping, or occultation.

- Event family has a documented story template and required facts.
- Observation guidance includes time window, direction, visibility caveats, and equipment/safety notes.
- Hero visual pattern exists and passes validation.
- Thumbnail pattern exists for English and Hindi.
- Stellarium/Sky Guide scene can be generated or a safe fallback exists.
- AI cinematic prompts include factual constraints and forbidden visual errors.
- English narration and subtitles pass readability review.
- Hindi narration and subtitles pass font, layout, and pronunciation review.
- 16:9 and 9:16 outputs are explicitly supported.
- Pre-publish validation blocks unsupported claims and missing required assets.
- Final package includes video, thumbnail, title, description, tags, subtitles, manifest, and diagnostics.

## 10. Future improvements

### Longer documentaries

- Expand 5-minute templates into 15-minute chaptered documentaries.
- Add stronger pacing rules, recurring motifs, expert-style explanation scenes, and chapter cards.
- Require deeper editorial review before publishing.

### Knowledge graph

- Connect event families, celestial objects, observation rules, cultural references, and scientific explanations.
- Use the graph to prevent contradictory claims and improve reuse across videos.

### AI director

- Add an AI director layer that chooses pacing, scene emphasis, music mood, camera language, and asset priority.
- Keep deterministic validation in control of facts, safety, timing, and final publishing gates.

### Learning feedback

- Feed viewer retention, click-through rate, subtitle readability issues, and publishing analytics back into templates.
- Improve hooks, thumbnail wording, scene length, and language-specific narration based on measured performance.
