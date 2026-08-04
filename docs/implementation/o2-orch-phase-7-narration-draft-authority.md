# P7.1C-A Narration Draft Authority Foundation

## Boundary and architecture

P7.1C-A is an in-memory authority boundary:

`PublishedNarrationPlanningAuthority → Phase7NarrationDraftInputAuthority → deterministic claim realization → scene construction → independent validation → NarrationDraftAuthority`.

The contract version is owned exclusively by `NarrationDraftContract.Version` (`rc2-phase7-narration-draft.v1`). The input evaluator accepts only the P7.1B-BB `NARRATION_PLANNING_REUSE_VALID` result. It independently canonicalizes the embedded and published diagnostics and verifies report, physical-validation, manifest, and publication-evidence checksums and their authority/validation linkages. It performs no write.

The request carries only a typed `Phase7KnowledgeCommittedStateRequest`, never a caller-authored claim list. Only the governed `P7KNOWLEDGE_VALID` committed-state result is accepted. P7.1C-A reconciles execution/plan/event, Phase 4 aggregate, Phase 5 publication, Phase 6 authority, P7.1A authority identities/checksums, normalized language, compatible knowledge runtime evidence, claim checksum, unique claim identity and planning partition before copying claims. It is not a second knowledge-resolution path.

## Deterministic realization and permitted transformations

No generated paraphrase is permitted. The realization policy performs exactly these transformations:

1. trims leading/trailing whitespace;
2. inserts the planning-authorized qualification strings before the exact certified claim text, in ordinal order;
3. adds the language policy's terminal punctuation only when terminal punctuation is absent.

Realized factual text must equal the deterministic composition of qualification text plus the trimmed certified body plus permitted terminal punctuation. The former protected-token substring test is not the authority decision. No synonym, metaphor, inferred fact, comparison, changed confidence, truncation, or translation is allowed.

English and Hindi punctuation, conjunction, sentence estimation, and reading rates are deterministic and culture-invariant. The governed rates are 150 words/minute for English and 130 words/minute for Hindi. A Hindi plan without matching certified Hindi claim text returns `NARRATION_DRAFT_CERTIFIED_LANGUAGE_CLAIM_MISSING`; English claim text is never silently translated.

## Composition governance

Required claims are emitted once in planning order and may never be dropped. Optional claims are considered after Required claims and are omitted before a budget violation; Optional Human Review and incompletely qualified claims are also deterministically omitted. Deferred claims remain typed on the scene for audit but never enter a sentence or factual usage. A Required Human Review claim blocks construction. Conservative coalescing declines unless a future certified compatibility proof is supplied; it does not recreate P7.1A merge semantics.

Spoken order is incoming transition, opening, Required claims, Optional claims, closing, outgoing transition. Openings come from viewer question (or the learning objective when absent). Closing is separately authorized by the typed learning objective; it never falls back to either transition side. Incoming ownership selects only `DestinationTransitionIn`, while outgoing ownership selects only `SourceTransitionOut`. Every spoken component is represented by an identified, checksummed sentence and participates in word, sentence, timing, diagnostics, and safety authority. Generic filler is not created.

Cultural, mythological, astrology, location, and date/time flags select the corresponding planning requirement strings. In the v1 upstream contract these strings are both the canonical qualification identity and approved spoken text; no display-text-derived identifier is invented. Safety validation evaluates all sentence text and typed usage. Exact mandatory structural capacity is reserved before Optional selection. Complete scenes enforce minimum and maximum sentence, word, and reading-time bounds without padding or truncation.

## Checksums and validation

`NarrationDraftCanonicalizer` owns sentence, scene, diagnostics, authority, validation checksums and deterministic IDs. Semantic sets are ordinally normalized; authored scene order, sentence order, visual-target order, and authority-local Long/Short order remain intact. Dictionary evidence is ordinally normalized. No time, random value, path, current culture, or provider state contributes.

The validator executes 27 uniquely named gates. Its DeterminismGate is a structural identity check, not by itself a full determinism proof. Byte determinism and Long/Short mutation isolation require focused serializer and mutation tests; the documentation does not substitute for that evidence.

## Provider isolation and DI

Draft constructors contain only P7.1C-A policy/evaluator interfaces. There is no Azure OpenAI client, prompt composer, narration generator, Azure Speech client, translation provider, TTS service, or media renderer. Consequently the foundation call counts are:

- Azure OpenAI calls = 0
- Prompt composer calls = 0
- Narration generator calls = 0
- Azure Speech calls = 0
- Translation provider calls = 0

Stateless policies, builder, and validator are singleton registrations. The input evaluator and `IPhase7NarrationDraftAuthorityService` are scoped because both committed-state evaluators are scoped. The application service evaluates input, builds, independently validates, and returns governed errors/blockers without writing or resolving a provider. Each required draft service has one registration and there is no captive scoped dependency.

## Verification status and exclusions

The foundation does **not** perform physical narration-draft publication, manifest integration, transaction/recovery/reuse, editorial approval, TTS, SRT, audio, image, or video output. It does not claim real 12 Long / 4 Short certification before a committed planning-publication package is available.

Focused test total: not certified in this environment. Upstream P7.1A, P7.1B-A, P7.1B-BA, P7.1B-BB regression totals: not certified in this environment. Broader Phase 7 total: not certified in this environment. These operational totals must be recorded after the .NET SDK and committed package are available; they are intentionally not fabricated.
