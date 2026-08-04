# O2.ORCH.7.1B-A — Scene Knowledge Packet foundation

## Boundary and governing authorities

This batch implements the read-only, in-memory packet boundary. It reviewed the Phase 21 pipeline
specification, the RC2 phase-output contract, the frozen Story Frame authority contracts, and the
certified P7.1A knowledge contracts. P7.1A is a frozen dependency: the packet input evaluator obtains
it exclusively from `IPhase7KnowledgeCommittedStateEvaluator` and obtains Phase 6 exclusively from
`IPhase6CommittedAuthorityEvaluator`.

The committed P7.1A publication projection now carries the already physically read-back resolution
report and diagnostics as typed values; this is an additive downstream projection and does not alter
P7.1A eligibility, disposition, merge, transaction, validation, or publication behavior. This batch does not create packet persistence, a transaction,
manifest/validation artifacts, publication evidence, or a packet committed-state evaluator.

## Contracts and implementation

`Phase7ScenePacketInputAuthority` composes `PublishedPhase7KnowledgeAuthority`,
`PublishedStoryFrameAuthority`, and `FamilyNarrationProfile`. It projects the independent ordered Long
and Short frames and source-scene indexes and records Phase 4–7 and runtime-compatibility evidence.
The evaluator preserves upstream errors and warnings, cancellation, and makes no writes. Its stable
reason-code family is `P7PACKET_INPUT_*`.

The existing `SceneKnowledgePacket` is reused. Its single contract version is
`rc2-phase7-scene-packets.v1`. The additions establish
the committed-input evaluator, a governed reference request, and the in-memory validator. No packet
field contains narration prose, a provider prompt, or a provider response.

## Reference and mapping rules

References resolve by exact ordinal Claim ID, exact semantic identity, exact Knowledge Entity ID, or
an exact certified claim-to-knowledge-reference mapping. Blank/whitespace-shaped references are
unsupported; fuzzy, substring, title, and display-text matching are prohibited. Results are
`Resolved`, `Deferred`, `Missing`, `Ambiguous`, `CrossVariantInvalid`, or `Unsupported`. Optional
absence alone defers. Disposition, sources, entity references, provenance, qualifications, and safety
flags remain attached to the immutable certified claim.

Each variant is built from its own authored frames in scene/frame order. Every frame must have exactly
one matching variant source-scene index row. `SectionKey` is the source row's narrative-stage value and
`SourceSceneChecksum` is the deterministic canonical checksum of the complete source row; neither is
positionally inferred or partially fabricated. Reference requirements preserve typed primary/required
status and source pointers. Required absence blocks, while explicitly optional absence defers. Required,
Optional, Deferred, and HumanReview dispositions are
partitioned without promotion: HumanReview claims are excluded from authoritative partitions and
retained as review warnings. Cultural material is emitted only when qualified. Safety rules and
location/time qualifications travel with the packet. Visual evidence is the intersection of certified
knowledge/object identities and selected claim identities; visual-intent text is lineage only.

Viewer questions preserve the source identity when present. Because the committed Story Frame artifact
does not contain authoritative question prose, the builder uses a deterministic scene-role/section editorial
fallback and labels that result as fallback rather than certified prose. The fallback makes no object-specific factual assertion and carries a reason and
checksum. Objectives use section/role framing and selected certified evidence without treating the
Phase 6 narration brief or visual intent as factual authority.

## Independence, determinism, and validation

Long and Short builds use separate frame and source-index collections. Packet identity binds execution,
variant, frame identity/checksum, sorted selected Required and Optional claim IDs, and the P7.1B
contract version. Unordered semantic collections are sorted before selection or hashing; authored frame
order remains intact. Packet checksum covers the complete serialized semantic record except the
checksum itself.

Required evidence is accepted only when an exact evidence row binds claim and semantic identity, a
claim source ID, Required eligibility, exact claim/entity/approved-field precision, and no human-review
requirement. There is no global Required-claim fallback. Generic neutral placeholder policy is used;
production packet code embeds no event, astronomy-family, country, or time-zone special case.

The validator runs these in-memory gates: InputAuthority, VariantCoverage, StoryFrameCoverage,
SceneOrder, SceneIdentity, StoryFrameChecksum, SourceSceneLineage, Profile, Language, PrimaryReference,
RequiredReferenceResolution, ClaimPartition, RequiredClaimEvidence, RequiredClaimChecksum,
NoContradiction, HumanReviewIsolation, SafetyRule, CulturalQualification, AstrologySeparation,
LocationTimeSafety, Duration, VisualEvidence, SectionAuthority, ViewerQuestionResolution,
ResolutionReportLineage, LongShortIndependence, and Determinism. The net10.0 test project compiles with
zero errors. The existing P7.1A knowledge regression filter passes 145/145 tests. The broader Phase 7
filter passes 250/261 tests (11 pre-existing semantic/runtime fixture failures), so this document does
not claim PASS. No dedicated real-fixture packet suite exists yet; Long/Short and blocking-issue totals
therefore remain unproven rather than fabricated.

## Files and verification record

Added implementation files are the packet-input evaluator and packet validator. Modified implementation
files are the narration-foundation contracts, exact reference resolver, packet builder, and DI module.
This document is the only added documentation file.

The expected certified fixture cardinality is 12 Long packets and 4 Short packets. A dedicated fixture
test must still confirm these counts and the safety, determinism, and independence gates before
publication work begins.

Remaining P7.1B work is deliberately limited to the later physical-publication batch: transaction and
recovery coordination, stable packet artifacts, inventory/manifest and validation files, publication
evidence, committed-state evaluation, and production routing.

Azure OpenAI calls: **0**. Azure Speech calls: **0**. No narration prose was generated and no provider
call occurred.
