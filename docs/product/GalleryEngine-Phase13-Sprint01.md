# Phase 13 Gallery Refinement — RC3 Sprint 01

## Review of current Gallery implementation

- **Rendering pipeline:** `AstroPulseGalleryService` keeps the existing Gallery V3 architecture: six carousel topics, one Azure Image2 background per topic, deterministic ImageSharp grading, and a minimal overlay. The refinement preserves that pipeline rather than replacing it.
- **AI prompt flow:** prompts are built from production event intelligence, resolved object names, event family forbidden terms, aspect constraints, and a per-slide educational role. Azure is still required for generated backgrounds; local fallback is intentionally not introduced.
- **Localization:** Gallery now reads the production language from event intelligence and localizes date/time labels, educational overlay labels, and observation-guide tips for English and Hindi.
- **Overlay:** the lower-third remains minimal and sky-dominant. Refinement adds a small educational sequence badge and a shared Drashyam footer while keeping text area capped and bottom padding diagnostics intact.
- **Validation:** phase validation now records a parity checklist for Hindi localization, shared footer, prompt refinement, diagnostics, educational overlay, story sequencing, and aspect support.
- **Diagnostics:** manifest, review, generation diagnostics, visual prompt diagnostics, and phase validation now include educational role, language, aspect, shared-footer flags, and story-sequencing flags.

## Hero capabilities reused naturally

- Shared typography resolver for Gallery text roles instead of ad-hoc font choice.
- Localization resolver for Hindi/English selection.
- Shared footer concept from Hero production composition, adapted as a subtle Gallery footer rather than a Hero-style information bar.
- Event content guard and event-family profile diagnostics already used by Gallery remain the validation backbone.

## Gallery-specific behavior preserved

- Six-image carousel contract.
- Unique Azure Image2 background per slide.
- Deterministic ImageSharp overlay after AI background generation.
- Minimal lower-third design rather than a dense infographic redesign.
- No thumbnail regeneration and no phase-12 coupling.
- Event-object-context prompt grounding and forbidden-term guardrails.

## Gap checklist implemented

- [x] Hindi localization.
- [x] Shared footer.
- [x] Validation parity checklist.
- [x] Prompt refinement with one educational role per slide.
- [x] Diagnostics expansion.
- [x] Educational overlay badge.
- [x] Story sequencing.
- [x] Landscape, portrait, and square aspect coverage at service contract level.

## Remaining TODO items

- Add end-to-end Azure-backed golden image fixtures when CI has configured Azure Image2 credentials.
- Add pixel-level overlay bounds validation for generated PNGs after a deterministic image-generation test seam exists.
- Expand family-specific Hindi copy beyond generic educational labels for more regional phrasing.
