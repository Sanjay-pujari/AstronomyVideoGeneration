# O2.ORCH.7.1 — Phase 7 Narration Authority Foundation

## 1. Governing documents reviewed
The implementation follows the frozen RC2 implementation guide, Phase 1–6 committed-authority conventions, artifact-authority guidance, the Phase 6 final-certification report, and the supplied Drashyam Phase 7 narration strategy and Orion audit requirements.

## 2. Frozen Phase 1–6 boundary
P7.1 does not write Phase 1–6 directories, validations, manifest inventories, phase history, checksums, recovery behavior, or reuse codes. Its structural input is the typed `PublishedStoryFrameAuthority` returned by `IPhase6CommittedAuthorityEvaluator`.

## 3. Phase 7 responsibility
The milestone resolves committed Story Frames, certified knowledge, family and language policy into deterministic knowledge packets and structured narration plans. It deliberately produces neither narration prose nor audio.

## 4. Phase 7 input authority
`Phase7InputAuthorityEvaluator` verifies committed Phase 6 identity, index lineage, variants, language, safe paths, certified intelligence, reviewed sources, family profile, and knowledge domains. Every rejection has a non-null deterministic reason code.

## 5. Family profile architecture
Profiles are immutable registrations. Aliases explicitly share profiles; production logic contains no subject-specific narration branch and no event facts.

## 6. Supported family profiles
Registrations cover CONSTELLATION, PLANET, MOON, LUNAR_PHASE, STAR, DEEP_SKY_OBJECT, GALAXY, NEBULA, STAR_FORMING_REGION, CLUSTER, METEOR_SHOWER, CONJUNCTION, GROUPING, ECLIPSE, OCCULTATION, TRANSIT, OPPOSITION, ELONGATION, CLOSE_APPROACH, COMET, SATELLITE, and ISS_PASS.

## 7. Certified knowledge resolution
`Phase7CertifiedKnowledgeSource` adapts existing `AstronomyEventIntelligence`, raw/metadata JSON, verification state, and reference-source records. `Phase7KnowledgeResolver` parses local certified JSON only; it has no network or AI dependency, reports missing domains, and never fabricates claims.

## 8. Claim contract
`CertifiedNarrationClaim` carries stable payload/domain/ordinal identity, approved text, reviewed source IDs, references, confidence, qualification flags, language, and checksum. Blank sources are rejected at the authority and validation boundaries.

## 9. Scene Knowledge Packet contract
There is one immutable packet per Story Frame. Packets carry lineage, selected certified claims, localization, visual evidence, safety, duration, dependency flags, warnings, blocking issues, and deterministic checksum—but no narration prose.

## 10. Narration Planning contract
Variant plans independently cover packets in canonical order. Scene plans contain purposes, claim IDs, progression and transition intent, duration-driven word ranges, language and safety rules, and review requirements.

## 11. Orion mapping
The constellation Long order is opening-recognition, identity, recognition-geometry, key-stars, deep-sky-star-formation, line-of-sight-geometry, history, culture-and-mythology, astronomy-astrology-clarification, observation, astrophotography, closing. Short order is hook, central-discovery, viewing-action, memorable-close. Facts remain sourced from certified Orion data rather than profiles.

## 12. Localization strategy
English and Hindi are supported as separate executions. Vocabulary, protected terms, and pronunciation hints are resolved from the selected certified language payload; Hindi is not produced by post-hoc packet translation.

## 13. Location/time safety
Claims record location, date/time, and approximation dependencies. Universal “Global's sky” and unqualified exact India Standard Time patterns are blocked, and profiles prohibit manufactured local viewing times.

## 14. Existing Azure OpenAI reuse plan
P7.2 will consume the existing configured provider and `NarrationGeneratorV5` only after accepted foundation authority. P7.1 introduces no SDK, endpoint, credential, or model call.

## 15. Existing Azure Speech reuse plan
P7.2 duration/release work will use the existing documentary narration speech adapter, voice resolver, and configured Azure Speech provider. P7.1 does not resolve a speech client or synthesize audio.

## 16. P7.1 artifact inventory
The transaction writes input authority, family profile, knowledge report, Long and Short packets/plans, diagnostics under `07-narration/`, plus `validation/phase-07-foundation-validation.json`. Final narration, acceptance, release-candidate, and certification artifacts are forbidden.

## 17. Validation gates
Gates cover committed input, profile, knowledge, sources, variants, coverage/order, claim grounding/sources, placeholders, location/time safety, localization, planning, Long/Short independence, checksums, and paths.

## 18. Transaction flow
The service evaluates and builds in memory, validates, writes an isolated `.07-narration-foundation-staging-<id>`, physically rereads and revalidates, swaps only `07-narration`, writes milestone validation, cleans temporary state, and restores the previous directory if swap fails.

## 19. Files added
Phase 7 contracts were added to Core; evaluators, resolvers, builders, validator, and transaction service were added to Infrastructure; a certified EF knowledge adapter and focused tests were added.

## 20. Files modified
Dependency injection registers the typed Phase 6/7 boundaries. API and Worker settings add only non-secret Phase 7 policy and estimator options.

## 21. Tests added
Focused family resolver tests certify constellation requirements, representative families and aliases, unsupported-family handling, Hindi registration, and deterministic checksums.

## 22. Exact test totals
The broad Phase7 name filter ran 68 tests: 54 passed, 14 failed, 0 skipped in 7.1898 seconds. The newly added focused profile suite ran 10 tests: 10 passed, 0 failed, 0 skipped in 107 ms.

## 23. Phase 1–6 regression totals
The Phase4/Phase5/Phase6 name-filter regression ran 271 tests: 262 passed, 9 failed, 0 skipped in 6 seconds. The complete project ran 5,079 tests: 4,637 passed, 431 failed, and 11 skipped in 2 minutes 43 seconds. These pre-existing/broad-suite failures remain certification blockers.

## 24. Remaining gaps
The failing broad Phase 7, Phase 4–6 regression, and complete-suite tests must be resolved. The production Orion fixture must then prove 12 Long and 4 Short packets and all expected certified claims.

## 25. P7.2 readiness
Contracts isolate future prose generation behind accepted structured authority and existing provider abstractions. P7.2 must not begin until the missing build/regression evidence and Orion fixture certification pass.

## 26. Final verdict
PHASE7_FOUNDATION_STILL_INCOMPLETE
