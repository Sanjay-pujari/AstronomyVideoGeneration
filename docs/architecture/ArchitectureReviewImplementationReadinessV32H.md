# V3.2H — Architecture Review & Implementation Readiness

## 1. Purpose

V3.2H is the final architecture review checkpoint for Drashyam Visual Intelligence before V3.3 implementation begins. It freezes the V3.2 Visual Intelligence architecture, verifies consistency across V3.2A–V3.2G, and confirms that the implementation path is ready without changing runtime behavior.

This document is documentation-only. It does not introduce new architecture concepts, implementation code, DTO classes, interface files, prompt replacements, Azure changes, pipeline changes, validation logic, or image generation changes.

## 2. Reviewed Documents

| Document | Summary | Review result |
| --- | --- | --- |
| V3.2A Visual Creative Director Foundation | Defines `VisualCreativeDirector` as the provider-neutral layer that consumes event intelligence and emits structured creative intent for visual assets. | Consistent with V3.2G ownership and V3.3 engine boundaries. |
| V3.2B Brand Design System | Defines Drashyam visual identity, premium documentary tone, typography discipline, color, spacing, observation-card behavior, and platform-aware brand rules. | Consistent as the owner of brand rules and typography rules. |
| V3.2C Planet Rendering Rules & Astronomical Rendering Specification | Defines astronomy rendering truth for planets and other celestial objects independently of providers and renderers. | Consistent as the owner of astronomical rendering constraints. |
| V3.2D Creative Direction Language | Defines CDL as the provider-neutral intermediate language for creative intent between `VisualCreativeDirector` and prompt translation. | Consistent with provider-neutral, renderer-independent data flow. |
| V3.2E PromptComposerV2 | Defines `PromptComposerV2` as the provider-specific translator from CDL / contract inputs to provider-ready prompt packages. | Consistent with translator-only responsibility; no creative ownership drift found. |
| V3.2F Creative Quality Scoring Engine | Defines provider-independent quality evaluation of generated assets against CDL, brand rules, rendering rules, and quality targets. | Consistent with observation-first rollout and publication decision separation. |
| V3.2G Visual Intelligence Contract Specification | Consolidates contracts, ownership, versioning, feature flags, fallback behavior, diagnostics, and V3.3 implementation sequencing. | Accepted as the implementation-facing bridge for V3.3. |

## 3. Architecture Consistency Review

| Area | Verification | Status |
| --- | --- | --- |
| Terminology consistency | `VisualCreativeDirector`, CDL, `CreativeDirectionContract`, `PromptComposerV2`, `PromptPackage`, `CreativeQualityScoringEngine`, `QualityReport`, and Publication Decision are used consistently as stage names and contract boundaries. | Ready |
| Contract naming consistency | Contract names are stable: CDL, `CreativeDirectionContract`, `PromptPackage`, `QualityReport`, BrandRules, PlanetRenderingRules, ProviderProfiles, and PublicationDecision. | Ready |
| Responsibility ownership | Each major artifact has one owner; downstream stages may read or translate but must not redefine upstream semantics. | Ready |
| Data flow consistency | All documents converge on the Event Intelligence → VisualCreativeDirector → CDL / contract → PromptComposerV2 → provider → scoring → publication decision flow. | Ready |
| Versioning consistency | V3.2 documents remain architecture specifications; V3.3 implementation should version serialized contracts and log version boundaries. | Ready |
| Feature flag consistency | V3.3 behavior is gated by explicit feature flags; V3.1 remains the default fallback when disabled or unavailable. | Ready |
| Fallback behavior consistency | Fallback consistently returns to existing V3.1 prompt and publication behavior unless a future release explicitly changes policy. | Ready |

## 4. Responsibility Ownership Matrix

| Responsibility / Artifact | Primary owner | Inputs | Outputs / Notes |
| --- | --- | --- | --- |
| Event intelligence consumption | `VisualCreativeDirector` | Event family, objects, timing, region, visibility, platform intent | Interpreted visual strategy inputs; does not own event facts. |
| Creative direction | `VisualCreativeDirector` | Event intelligence, brand rules, rendering rules, platform context | Provider-neutral visual intent. |
| Brand rules | Brand Design System | Drashyam identity requirements | BrandRules, typography rules, spacing, tone, observation-card style. |
| Astronomical rendering rules | Planet Rendering Rules module | Astronomy truth and object-specific constraints | PlanetRenderingRules and stable rendering constraints. |
| CDL generation | `VisualCreativeDirector` / CDL module | Creative direction, brand rules, rendering rules | CDL artifact. |
| `CreativeDirectionContract` | `VisualCreativeDirector` | CDL, brand rules, rendering rules, quality targets, target platform | Frozen implementation-facing bundle. |
| Prompt translation | `PromptComposerV2` | `CreativeDirectionContract`, CDL, provider profile | Provider-ready prompt content without changing creative intent. |
| Provider profiles | Provider profile layer / `PromptComposerV2` | Provider capabilities and constraints | Translation hints, parameter preferences, unsupported-capability diagnostics. |
| `PromptPackage` | `PromptComposerV2` | Contract, provider profile | Prompt text, negative constraints, provider metadata, diagnostics. |
| Quality scoring | `CreativeQualityScoringEngine` | Generated asset, contract, prompt package, brand and rendering rules | Provider-independent quality assessment. |
| `QualityReport` | `CreativeQualityScoringEngine` | Quality scoring dimensions and thresholds | Scores, findings, diagnostics, recommended decision. |
| Publication decision | Publication policy / pipeline decision layer | QualityReport, feature flags, existing pipeline policy | Accept, warn, block, regenerate, or fallback recommendation. |

## 5. Dependency Matrix

| Document | Depends on | Feeds |
| --- | --- | --- |
| V3.2A Visual Creative Director Foundation | Event Intelligence, V3.2B, V3.2C | V3.2D, V3.2G, V3.3B |
| V3.2B Brand Design System | Drashyam product identity and platform needs | V3.2A, V3.2D, V3.2E, V3.2F, V3.2G |
| V3.2C Planet Rendering Rules | Astronomy truth and rendering credibility requirements | V3.2A, V3.2D, V3.2E, V3.2F, V3.2G |
| V3.2D Creative Direction Language | V3.2A, V3.2B, V3.2C | V3.2E, V3.2F, V3.2G, V3.3A |
| V3.2E PromptComposerV2 | V3.2D, V3.2B, V3.2C, provider profiles | Image Provider, V3.2F, V3.2G, V3.3C |
| V3.2F Creative Quality Scoring Engine | V3.2D, V3.2E, V3.2B, V3.2C | QualityReport, Publication Decision, V3.2G, V3.3D |
| V3.2G Visual Intelligence Contract Specification | V3.2A–V3.2F | V3.2H, all V3.3 implementation work |
| V3.2H Architecture Review & Implementation Readiness | V3.2A–V3.2G | Architecture freeze recommendation and V3.3 readiness checklist |

## 6. End-to-End Data Flow

Final frozen V3.2 Visual Intelligence flow:

```text
Event Intelligence
→ VisualCreativeDirector
→ CDL
→ CreativeDirectionContract
→ PromptComposerV2
→ PromptPackage
→ Image Provider
→ Generated Asset
→ CreativeQualityScoringEngine
→ QualityReport
→ Publication Decision
```

Flow constraints:

- Event Intelligence owns astronomy facts and context.
- `VisualCreativeDirector` owns creative interpretation, not prompt text or provider calls.
- CDL and `CreativeDirectionContract` preserve provider-neutral intent.
- `PromptComposerV2` translates, but does not invent creative direction.
- Image Provider remains provider-specific and external to creative logic.
- `CreativeQualityScoringEngine` evaluates generated output and emits findings.
- Publication Decision remains separate from scoring and controlled by feature flags.

## 7. Feature Flag Readiness

All V3.3 behavior remains opt-in. Default state for V3.3 implementation is **off** unless explicitly enabled in a controlled environment.

| Feature flag | Default state | Behavior when off | Behavior when on | Fallback behavior |
| --- | --- | --- | --- | --- |
| `UseVisualCreativeDirector` | Off | V3.1 prompt planning remains the visual-intent source. | `VisualCreativeDirector` may produce creative direction candidates. | Return to V3.1 visual intent path on disable, error, missing inputs, or unsupported event family. |
| `UseCDL` | Off | No CDL artifact is required. | CDL is emitted as provider-neutral creative intent. | Ignore CDL and continue V3.1 prompt flow if CDL is absent, invalid, or disabled. |
| `UseCreativeDirectionContract` | Off | No `CreativeDirectionContract` is required downstream. | Contract is emitted and passed to prompt composition and scoring. | Use V3.1 prompt and image-generation behavior if contract emission or validation fails. |
| `UsePromptComposerV2` | Off | Existing prompt composer / Azure Image2 prompt path remains unchanged. | `PromptComposerV2` creates a `PromptPackage`. | Use existing prompt generation path if translation fails, provider profile is missing, or output is invalid. |
| `UseProviderProfiles` | Off | Existing provider settings remain in effect. | Provider profiles influence translation and provider parameters. | Use stable existing provider defaults when a profile is unavailable or unsupported. |
| `UseQualityScoring` | Off | No new quality report is required; publication continues as V3.1. | `CreativeQualityScoringEngine` emits `QualityReport` in observation or blocking mode. | Continue V3.1 publication behavior if scoring is disabled, unavailable, or inconclusive. |
| `UseQualityScoringBlocking` | Off | Quality scoring, if enabled, is observation-only and cannot block publication. | Quality scoring may influence block, regenerate, warn, or accept decisions. | Revert to observation-only scoring or V3.1 publication behavior on errors or misconfiguration. |
| `UseExperimentalRenderingRules` | Off | Only stable rendering rules are used. | Explicitly allowed experimental rendering constraints may be included. | Drop experimental rules and continue with stable rendering constraints. |

## 8. V3.3 Implementation Checklist

### V3.3A Contracts and DTOs

- [ ] Define DTOs for CDL, `CreativeDirectionContract`, `PromptPackage`, `QualityReport`, provider profiles, diagnostics, and version metadata.
- [ ] Preserve serialized contract names from V3.2G.
- [ ] Add validation diagnostics without changing pipeline behavior.
- [ ] Confirm all DTOs are additive and feature-flag-safe.

### V3.3B VisualCreativeDirector Engine

- [ ] Implement `VisualCreativeDirector` behind `UseVisualCreativeDirector`.
- [ ] Consume event intelligence, brand rules, rendering rules, platform context, and asset type.
- [ ] Emit CDL and optional `CreativeDirectionContract` only when corresponding flags are enabled.
- [ ] Preserve V3.1 fallback on all disabled, unsupported, or failed paths.

### V3.3C PromptComposerV2

- [ ] Implement translation from `CreativeDirectionContract` / CDL to `PromptPackage`.
- [ ] Keep `PromptComposerV2` business-logic-free, astronomy-logic-free, and brand-logic-free.
- [ ] Add provider profile support behind `UseProviderProfiles`.
- [ ] Preserve existing prompt generation and Azure Image2 path as fallback.

### V3.3D Quality Scoring Observation Mode

- [ ] Implement `CreativeQualityScoringEngine` behind `UseQualityScoring`.
- [ ] Emit `QualityReport` diagnostics without blocking publication by default.
- [ ] Keep `UseQualityScoringBlocking` off for initial rollout.
- [ ] Log false positives, false negatives, scoring latency, and fallback reasons.

### V3.3E End-to-End Feature Flag Integration

- [ ] Wire all Visual Intelligence feature flags end to end.
- [ ] Confirm disabled flags preserve V3.1 behavior.
- [ ] Add diagnostics at each boundary: direction, CDL, contract, prompt package, provider request, quality report, and publication decision.
- [ ] Add operational visibility for fallback counts, errors, and version metadata.

## 9. Risk Register

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Contract drift | DTOs or serialized fields diverge from V3.2G, causing integration ambiguity. | Treat V3.2G names as normative; snapshot serialized payloads in V3.3. |
| Prompt provider lock-in | Prompt logic becomes too Azure-specific and hard to reuse. | Keep CDL provider-neutral and isolate provider syntax in provider profiles and `PromptComposerV2`. |
| Overly complex CDL | Implementation becomes slow, hard to test, or hard to inspect. | Start with minimal required fields; add optional sections only with diagnostics and fallbacks. |
| Quality scoring false positives | Good assets may be warned or blocked incorrectly. | Run scoring in observation-only mode first; keep blocking off until confidence is proven. |
| Inconsistent terminology | Developers may implement duplicate concepts with different names. | Use V3.2G and this review as naming references during V3.3A DTO work. |
| Feature flag misconfiguration | Partial enablement may create broken or surprising flows. | Define safe dependency checks and default all V3.3 behavior to off. |
| Performance overhead | Contract generation, translation, or scoring may add latency. | Measure stage timing; allow each stage to be disabled independently. |
| Backward compatibility regression | Existing V3.1 RC prompt and publication behavior could change unintentionally. | Preserve V3.1 as default fallback; require disabled-flag regression checks before merge. |

## 10. Decision Log

| Decision | Rationale |
| --- | --- |
| Document-first architecture | V3.2 freezes design before V3.3 implementation to reduce ambiguity and rework. |
| Provider-neutral CDL | Creative intent must survive Azure Image2 and future provider changes. |
| `PromptComposerV2` as translator, not creative engine | Creative ownership stays upstream; prompt composition only adapts intent to provider syntax. |
| Quality scoring initially observation-only | Scoring needs real-world calibration before it can block publication. |
| V3.1 fallback remains default | Existing release-candidate behavior must remain stable until V3.3 is explicitly enabled. |
| Feature flags gate all V3.3 behavior | Safe rollout requires independent enablement, diagnostics, and rollback. |
| No Azure dependency inside creative logic | Visual Intelligence must stay reusable across providers, renderers, and future platforms. |

## 11. Architecture Freeze Recommendation

V3.2 Visual Intelligence is ready to freeze for V3.3 implementation.

Recommended architecture freeze tag:

```text
v3.2.0-architecture-freeze
```

Freeze recommendation conditions:

- V3.2A–V3.2G are accepted as the source architecture set.
- V3.2G is treated as the normative implementation-facing contract reference.
- V3.3 begins with contracts and feature-flag-safe DTOs before runtime behavior changes.
- V3.1 RC remains unchanged by default.

## 12. Non-Goals

V3.2H explicitly does not include:

- No implementation code.
- No DTO classes.
- No interface files.
- No prompt replacement.
- No Azure changes.
- No pipeline phase changes.
- No validation implementation.
- No image generation changes.
- No narration, SRT, TTS, validation, rendering, or publication logic changes.

## 13. Acceptance Criteria

- [x] Documentation-only PR.
- [x] V3.2A–V3.2G reviewed.
- [x] No new architecture concepts introduced.
- [x] Responsibility matrix complete.
- [x] Dependency matrix complete.
- [x] Feature flag behavior reviewed.
- [x] V3.3 implementation checklist complete.
- [x] Risk register complete.
- [x] Decision log complete.
- [x] Existing V3.1 RC remains unchanged.
- [x] No code files modified.
