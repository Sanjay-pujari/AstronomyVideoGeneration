# O2.ORCH.7.1.H1 — Phase 7 Foundation Production Hardening

## 1. Governing documents reviewed
The hardening pass reviewed the frozen RC2 architecture and integration material, artifact/committed-authority conventions, frozen Phase 1–6 contracts and certification material, the Phase 7 narration strategy/audit supplied with this work, evergreen astronomy contracts, and existing semantic-runtime infrastructure.

## 2. Frozen Phase 1–6 declaration
No Phase 1–6 artifact, validation, inventory, history, checksum, Story Frame, count, reuse rule, or transaction was changed. P7.1 consumes only `PublishedStoryFrameAuthority` through `IPhase6CommittedAuthorityEvaluator`.

## 3. Original P7.1 gaps
The prior implementation omitted evergreen loading, merged only one payload, inferred family from title text, assigned all source IDs to every claim, used ordinal claim IDs and confidence 1.0, recursively promoted arbitrary strings, used inconsistent domains and aliased distinct geometry families, generated generic scene semantics/free-form evidence IDs, assumed diagnostic success, incompletely reread artifacts, and did not restore both authorities after post-swap failure.

## 4. Existing knowledge services reused
* Event record and event reference sources: EF `AstronomyEventIntelligence` repository through `MediaFactoryDbContext`.
* Evergreen payload and safe relative-path resolution: existing `IEvergreenAstronomyKnowledgeLoader` / `EvergreenAstronomyKnowledgeLoader`.
* Family authority: explicit certified event type followed by existing canonical `EventFamilyResolver`; title scanning was removed.
* Checksums and JSON: `Phase7Determinism` and repository `JsonSerializerDefaults.Web` conventions.
* Upstream authority: `IPhase6CommittedAuthorityEvaluator`.

## 5. Evergreen loading
`MetadataJson.relativePath` is parsed and passed unchanged to the approved loader. The loader rejects absolute/traversal paths, validates the reviewed package, and returns its physical SHA-256. P7.1 verifies localized language and event/evergreen family agreement and stores relative path, payload ID, and checksum.

## 6. Certified family resolution
The certified event's explicit type is preferred. Unsupported direct values use the existing canonical resolver with event type/category only. Arbitrary title substring matching is prohibited. The evergreen family must match the resolved authority.

## 7. Knowledge merge policy
The schema-aware resolver reads approved raw-event and evergreen sections independently. Evergreen supplies stable identity/science/history/culture/observation principles; certified event claims are applied later and therefore own event-specific timing, visibility, location, and geometry. Same-origin semantic conflicts produce a blocking deterministic issue rather than silent overwrite.

## 8. Canonical domain model
`NarrationKnowledgeDomainKey` is the single domain catalog used by profiles, resolver, claims, packet selection, validator, diagnostics, and tests. Profile construction rejects unknown domain names.

## 9. Claim identity
Claim IDs hash the stable knowledge entity, canonical schema path, language, and certified payload version. Array values use a content-derived semantic suffix, so JSON reordering does not alter identity. Ordinal position is not part of new claim identity.

## 10. Claim provenance
`CertifiedNarrationSource` carries authority, reference, review/certification, supported knowledge/claim/domain mappings, language, confidence, and checksum. Explicit payload `sourceIds` create exact mappings; declared knowledge/domain coverage is coarse and forces review. Required claims with no exact certified source fail validation.

## 11. Knowledge-reference reconciliation
`IPhase7KnowledgeReferenceResolver` returns `Resolved`, `Deferred`, `Missing`, `Ambiguous`, `CrossVariantInvalid`, or `Unsupported`. Exact claim, semantic identity, and knowledge entity references resolve deterministically. Missing primary references become blocking packet issues; optional requests can be deferred.

## 12. Scene semantic enrichment
Packets derive objectives and documentary questions from family section, scene role, and selected certified evidence. Claims record selection reason. Phase 6 visual instructions remain lineage only; certified object IDs provide `VisualEvidenceIds`.

## 13. Family profile distinctions
Constellation bounds are 8/12/16 with unchanged 480/600/900 seconds and four-scene Short. Grouping, occultation, transit, opposition, elongation, and close approach now have independent profiles and mandatory canonical domains rather than inheriting the complete conjunction profile.

## 14. Orion packet mapping
The family order supports recognition/Belt, official identity, Belt/Sword geometry, key stars, M42/star formation, line-of-sight distance, history, separately attributed traditions, qualified Indian/zodiac context, qualified observation, equipment/astrophotography, and grounded closure. Short retains recognition, discovery, qualified action, and memorable close. Production contains no Orion frame-ID branch.

## 15. Safety policy
Claims preserve approximation, location, date/time, weather, moon, uncertainty, cultural, mythology, astrology, qualification, and human-review metadata. Universal global time and `Global's sky` patterns are rejected. Cultural and astrology claims require explicit qualification.

## 16. Atomic transaction
The candidate complete set is written under an owned staging name and all eight narration artifacts are deserialized before swap. Existing narration and validation authorities are separately snapshotted. Any failure after directory swap removes the candidate and restores both prior authorities. Residue is deleted only after committed readback.

## 17. Recovery
`IPhase7FoundationRecoveryService` recognizes only the four exact owned path families. It clears pre-swap staging, restores missing narration/validation authorities from the newest owned backups, cleans completed residue, and never enumerates unrelated names.

## 18. Committed foundation evaluator
`IPhase7FoundationCommittedStateEvaluator` physically requires and deserializes all nine P7.1 files, checks validation, lineage, and provenance, computes physical SHA-256 values, and returns `PublishedPhase7FoundationAuthority`. This is the typed P7.2 boundary.

## 19. Artifact inventory
The complete set is input authority, family profile, knowledge report, diagnostics, Long packets/plan, Short packets/plan, and foundation validation. Exact membership and safe relative paths are validator gates.

## 20. Files added
`Phase7KnowledgeReferenceResolver.cs`, `Phase7FoundationPublicationInfrastructure.cs`, and `Phase7KnowledgeReferenceResolverTests.cs` were added.

## 21. Files modified
Phase 7 contracts, certified knowledge source, knowledge resolver, family profiles, input evaluator, packet builder, validator, foundation service, DI registration, profile tests, and this document were modified. No frozen Phase 1–6 file was modified.

## 22. Tests added
Tests cover constellation bounds, independent geometry-event profiles, canonical domains, exact reference resolution, missing primary resolution, and optional deferral. The requested broader transaction, recovery, committed evaluator, adapter, provenance, and real Orion fixture matrix remains incomplete.

## 23. Exact focused totals
The targeted new profile/reference suite ran 19 tests: 19 passed, 0 failed, 0 skipped in 120 ms. The broad Phase 7 name filter ran 77 tests: 63 passed, 14 failed, 0 skipped in 10.5706 seconds. Its failures are recorded release blockers.

## 24. Upstream regression totals
The combined Phase 4/5/6, Story Frame, and production-pipeline regression filter ran 473 tests: 463 passed, 10 failed, 0 skipped in 12 seconds. No frozen Phase 1–6 source or artifact was edited, but these failures prevent regression certification. A complete-suite attempt exposed additional pre-existing failures and was stopped after more than four minutes without reaching a test summary, so exact complete totals are unavailable.

## 25. Real Orion result
The repository Orion package was inspected and includes the required constellation, Belt/key-star, and M42 object identities plus reviewed IAU/NASA/reference sources. A real P7.1 publication was not executable without the .NET runtime, so 12 Long + 4 Short packet/readback results are not certified in this run.

## 26. Remaining gaps
The solution builds with 0 errors (248 existing warnings), but the full requested test matrix, Phase 7 failures, Phase 4–6 regression failures, complete-suite totals, fault-injected recovery/rollback tests, and real Orion committed publication must pass. Diagnostics intentionally do not claim physical readback success before publication evidence.

## 27. P7.2 readiness
P7.2 must not begin. P7.1 invokes neither Azure OpenAI nor Azure Speech, but missing runtime certification evidence remains a release blocker.

## 28. Final verdict
PHASE7_FOUNDATION_STILL_INCOMPLETE
