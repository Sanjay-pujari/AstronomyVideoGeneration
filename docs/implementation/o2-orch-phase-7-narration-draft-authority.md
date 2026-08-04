# P7.1C-A Narration Draft Authority Foundation

## Boundary and architecture

P7.1C-A is an in-memory authority boundary:

`PublishedNarrationPlanningAuthority → Phase7NarrationDraftInputAuthority → deterministic claim realization → scene construction → independent validation → NarrationDraftAuthority`.

The contract version is owned exclusively by `NarrationDraftContract.Version` (`rc2-phase7-narration-draft.v1`). The input evaluator consumes the committed P7.1B-BB evaluator, verifies committed planning identity, checksums, gates, physical readback state, manifest/evidence state, profile, language, lineage identity and runtime evidence, and preserves upstream warnings. It performs no write.

The input also carries the certified language-specific claim objects copied from committed knowledge authority. This is essential because planning scenes intentionally contain claim identities rather than fact text. It is not a second knowledge-resolution path.

## Deterministic realization and permitted transformations

No generated paraphrase is permitted. The realization policy performs exactly these transformations:

1. trims leading/trailing whitespace;
2. inserts the planning-authorized qualification strings before the exact certified claim text, in ordinal order;
3. adds the language policy's terminal punctuation only when terminal punctuation is absent.

Numerals, units, proper names, dates, directions, and locations in certified text are protected and must still occur ordinally in realized text. No synonym, metaphor, inferred fact, comparison, changed confidence, truncation, or translation is allowed.

English and Hindi punctuation, conjunction, sentence estimation, and reading rates are deterministic and culture-invariant. The governed rates are 150 words/minute for English and 130 words/minute for Hindi. A Hindi plan without matching certified Hindi claim text returns `NARRATION_DRAFT_CERTIFIED_LANGUAGE_CLAIM_MISSING`; English claim text is never silently translated.

## Composition governance

Required claims are emitted once in planning order and may never be dropped. Optional claims are considered after Required claims and are omitted before a budget violation. Deferred claims remain typed on the scene for audit but never enter a sentence or factual usage. Any factual Human Review claim blocks construction. Conservative coalescing declines unless a future certified compatibility proof is supplied; it does not recreate P7.1A merge semantics.

Openings come from viewer question (or the learning objective when absent). Closings and transition phrases come only from typed planning transition/goal fields. Generic filler is not created. Long and Short scene lists are independently traversed, and identities bind their variant; neither variant reads or edits the other.

Cultural, mythological, astrology, location, and date/time flags select the corresponding planning qualification requirements for the exact claim sentence. Safety validation evaluates typed usage and text, rejects prohibited strings, missing required qualification, Human Review use, unknown usage, and Deferred use. Sentence and scene checks enforce planning maxima; sentences are never truncated. Reading estimates and optional capacity belong to the timing policy rather than the builder.

## Checksums and validation

`NarrationDraftCanonicalizer` owns sentence, scene, diagnostics, authority, validation checksums and deterministic IDs. Semantic sets are ordinally normalized; authored scene order, sentence order, visual-target order, and authority-local Long/Short order remain intact. Dictionary evidence is ordinally normalized. No time, random value, path, current culture, or provider state contributes.

The validator executes 27 uniquely named gates. It independently reconstructs planning scene coverage and Required/Optional/Deferred use rather than trusting a recomputed checksum alone. It also checks lineage, sentence identity/checksum, qualifications, typed safety, transition variant ownership, timing, diagnostics, authority checksum, and deterministic identity.

## Provider isolation and DI

Draft constructors contain only P7.1C-A policy/evaluator interfaces. There is no Azure OpenAI client, prompt composer, narration generator, Azure Speech client, translation provider, TTS service, or media renderer. Consequently the foundation call counts are:

- Azure OpenAI calls = 0
- Prompt composer calls = 0
- Narration generator calls = 0
- Azure Speech calls = 0
- Translation provider calls = 0

Stateless policies, builder, and validator are singleton registrations. The input evaluator is scoped because the committed P7.1B-BB evaluator is scoped. Each required draft service has one registration and there is no captive scoped dependency.

## Verification status and exclusions

The foundation does **not** perform physical narration-draft publication, manifest integration, transaction/recovery/reuse, editorial approval, TTS, SRT, audio, image, or video output. It does not claim real 12 Long / 4 Short certification before a committed planning-publication package is available.

Focused test total: not certified in this environment. Upstream P7.1A, P7.1B-A, P7.1B-BA, P7.1B-BB regression totals: not certified in this environment. Broader Phase 7 total: not certified in this environment. These operational totals must be recorded after the .NET SDK and committed package are available; they are intentionally not fabricated.
