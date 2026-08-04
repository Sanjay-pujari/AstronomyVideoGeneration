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

Domain relevance now tokenizes section, role, narrative stage, and learning-objective identities at
space, underscore, hyphen, slash, and period boundaries before normalizing each token. One generic
token-to-domain table is constructed exclusively from `NarrationKnowledgeDomainKey` values through
`NarrationKnowledgeDomains.Id`; no handwritten domain identity can become authority. It governs recognition/identity, appearance/geometry, scientific structure and
evolution, objects/deep-sky, observation/visibility/timing, equipment, astrophotography, cultural
traditions, astrology clarification, safety, history, and closing facts. Unknown tokens grant no
domain authority.

References resolve by exact ordinal Claim ID, exact semantic identity, exact Knowledge Entity ID, or
an exact certified claim-to-knowledge-reference mapping. Blank/whitespace-shaped references are
unsupported; fuzzy, substring, title, and display-text matching are prohibited. Results are
`Resolved`, `Deferred`, `Missing`, `Ambiguous`, `CrossVariantInvalid`, or `Unsupported`. Optional
absence alone defers. Disposition, sources, entity references, provenance, qualifications, and safety
flags remain attached to the immutable certified claim.

The frozen Phase 6 contracts expose no explicit required/optional/primary metadata beyond the authored
ordered `KnowledgeReferenceIds` list. `Phase7SceneReferenceCompatibilityPolicy` is therefore the sole
compatibility authority: it records that order is governing, identifies the first authored reference
as Primary, classifies the authored references as Required, emits
`P7PACKET_REFERENCE_COMPAT_PHASE6_ORDERED_PRIMARY`, and requires no human review. Empty or malformed
collections fail input authority with `P7PACKET_REFERENCE_REQUIREMENTS_UNRESOLVED`; the builder never
reconstructs requirements. Every packet carries typed resolution status and resolved claim IDs for
each governed reference. Primary validation binds requirement ownership, packet variant and ID, and
resolution status rather than treating a nonempty ID collection as proof.

Each variant is built from its own authored frames in scene/frame order. Every frame must have exactly
one matching variant source-scene index row. `SectionKey` is the source row's narrative-stage value and
`SourceSceneChecksum` is the deterministic canonical checksum of the complete source row; neither is
positionally inferred or partially fabricated. The frozen contracts have no distinct section field;
the single section resolver documents the source-scene `NarrativeStage` profile-slot compatibility
mapping and retains NarrativeStage separately as resolver evidence. Reference requirements preserve typed primary/required
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

Both mappings are approved, version-bound P7.1B compatibility decisions—not serialized Phase 6
facts. The reference decision emits `P7PACKET_REFERENCE_COMPAT_PHASE6_ORDERED_PRIMARY`; the section
decision emits `P7PACKET_SECTION_COMPAT_SOURCE_SCENE_NARRATIVE_STAGE`. The compatibility warning is
carried by input authority. Phase 6's governing serialized contracts are
`StoryFrameAuthorityFrame.KnowledgeReferenceIds` and `StoryFrameSceneIndex.NarrativeStage` in
`StoryFrameAuthorityContracts.cs`; neither contract exposes the downstream flags/SectionKey.

## Independence, determinism, and validation

Long and Short builds use separate frame and source-index collections. Packet identity binds execution,
variant, frame identity/checksum, sorted selected Required and Optional claim IDs, and the P7.1B
contract version. Unordered semantic collections are sorted before selection or hashing; authored frame
order and authored reference order remain intact. The packet canonicalizer sorts claim partitions by
ordinal ClaimId; claim source/reference IDs, packet source/visual/protected-term/approximation/warning/
cultural collections, dictionary keys, and per-reference resolved ClaimIds ordinally. Packet identity
and checksum use this complete canonical projection, while visual-planning lineage remains authored.

Required evidence is accepted only when an exact evidence row binds claim and semantic identity, a
claim source ID, Required eligibility, exact claim/entity/approved-field precision, and no human-review
requirement. There is no global Required-claim fallback. Generic neutral placeholder policy is used;
production packet code embeds no event, astronomy-family, country, or time-zone special case.

Optional evidence uses the same exact claim/semantic/source binding and precision set, accepts only
Required-eligible or Optional-eligible sources, and rejects audit-only, rejected, coarse, and
human-review rows. Optional claims participate in source and visual identities, cultural context,
location/date-time dependence, and approximation warnings. `PacketBlockingIssueGate` rejects every
blocking issue, regardless of prefix; builder-generated blockers are deterministic reason codes.
The Phase 6 authority does not expose event-family or event-type fields, so no values are fabricated;
consistency remains indirectly bound through the Phase 4 aggregate, Phase 5 publication, Phase 6
authority/index checksums, canonical profile identity, and P7.1A lineage.

Input evaluation now rejects duplicate frame IDs, duplicate scene/frame identities, absent or
ambiguous source-scene rows, and cross-variant source-scene rows with governed `P7PACKET_INPUT_*`
results before dictionary projection. Required references must resolve and bind at least one exact,
non-review Required-partition claim with Required-eligible evidence; an unrelated section claim cannot
substitute. Exactly one Primary is required per packet, and it must resolve with a nonempty claim set
regardless of its required/optional classification. Both compatibility policies are singleton DI
services and are constructor-injected into their production consumers.

The validator runs these in-memory gates: InputAuthority, VariantCoverage, StoryFrameCoverage,
SceneOrder, SceneIdentity, StoryFrameChecksum, SourceSceneLineage, Profile, Language, PrimaryReference,
RequiredReferenceResolution, PacketBlockingIssue, ClaimPartition, RequiredClaimEvidence,
OptionalClaimEvidence, RequiredClaimChecksum,
NoContradiction, HumanReviewIsolation, SafetyRule, CulturalQualification, AstrologySeparation,
LocationTimeSafety, Duration, VisualEvidence, SectionAuthority, ViewerQuestionResolution,
ResolutionReportLineage, LongShortIndependence, and Determinism. The current correction environment
does not contain a `dotnet` executable, so compilation and test
totals could not be re-certified here. The previous record was 145/145 for P7.1A and 250/261 for the
broader Phase 7 filter (11 pre-existing semantic/runtime fixture failures); those historical results
are not presented as results for this correction. No dedicated committed-shape packet fixture suite
exists yet, so Long/Short, blocker, failed-gate, byte-equivalence, reordered-input, and mutation-isolation
totals remain unproven rather than fabricated. The available real Orion source artifact is
`Knowledge/Constellations/Orion/Orion.v1.json`; it is not by itself the complete committed P7.1A/Phase 6
artifact set required by the certification fixture contract.

## Files and verification record

This correction adds `Phase7SceneKnowledgePacketCanonicalizer.cs`. It modifies the governance-policy,
packet-builder, validator, input-evaluator, DI-registration, and implementation-report files.

The expected certified fixture cardinality is 12 Long packets and 4 Short packets. A dedicated fixture
test must still confirm these counts and the safety, determinism, and independence gates before
publication work begins.

Remaining P7.1B work is deliberately limited to the later physical-publication batch: transaction and
recovery coordination, stable packet artifacts, inventory/manifest and validation files, publication
evidence, committed-state evaluation, and production routing.

Azure OpenAI calls: **0**. Azure Speech calls: **0**. No narration prose was generated and no provider
call occurred.

## P7.1B-A FINAL CLAIM-IMMUTABILITY AND REAL-FIXTURE CERTIFICATION

The packet canonicalizer now orders the Required, Optional, and Deferred claim partitions by
`ClaimId` while retaining the actual `CertifiedNarrationClaim` instances and their authored nested
orders. It does not rewrite a certified claim. A private checksum-only projection sorts nested claim
source and knowledge-reference identifiers for semantic hashing; that projection is used by the
shared `ComputePacketId` and `ComputeChecksum` methods and never becomes packet JSON. Packet-level
source IDs, visual evidence IDs, protected terms, approximation warnings, warnings, blocking issues,
cultural context, dictionary keys, and resolved claim IDs are unordered and canonicalized. Authored
knowledge-reference order, reference-resolution order, Story Frame order, packet order, and visual
planning lineage remain ordered.

Validation now includes `PacketClaimAuthorityIdentityGate` for all Required, Optional, and Deferred
claims. It compares the complete deterministic representation to the matching frozen authority claim
and emits `P7PACKET_CLAIM_AUTHORITY_IDENTITY_MISMATCH:<ClaimId>`. The Required checksum gate additionally
requires complete authority identity, the frozen checksum, and successful recomputation from the
original representation. Production DI contains one singleton registration each for the reference
compatibility policy, section resolver, reference resolver, packet builder, and packet validator, and
one scoped registration for the packet-input evaluator. This avoids a singleton consuming its scoped
committed-authority evaluators.

Final real-fixture certification is **incomplete**. No sanitized successful Orion committed package is
present. The exact required missing fixture paths are:

- `Astronomy.MediaFactory.Tests/Fixtures/Phase7/P7.1B/OrionCommitted/07-narration/knowledge/knowledge-authority.json`
- `Astronomy.MediaFactory.Tests/Fixtures/Phase7/P7.1B/OrionCommitted/07-narration/knowledge/knowledge-resolution-report.json`
- `Astronomy.MediaFactory.Tests/Fixtures/Phase7/P7.1B/OrionCommitted/07-narration/knowledge/knowledge-diagnostics.json`
- `Astronomy.MediaFactory.Tests/Fixtures/Phase7/P7.1B/OrionCommitted/06-story-frames/story-frame-authority.json`
- `Astronomy.MediaFactory.Tests/Fixtures/Phase7/P7.1B/OrionCommitted/06-story-frames/story-frame-index.json`
- `Astronomy.MediaFactory.Tests/Fixtures/Phase7/P7.1B/OrionCommitted/06-story-frames/story-frame-diagnostics.json`
- `Astronomy.MediaFactory.Tests/Fixtures/Phase7/P7.1B/OrionCommitted/07-narration/constellation-family-narration-profile.json`

Consequently there is no source execution/plan identity available to record, and the required values
(12 Long, 4 Short, 16 total, zero blockers, and zero failed gates) remain unverified. Repeated-build
byte equivalence, reordered-input equivalence, Long mutation isolation, and Short mutation isolation
also remain unverified. The current environment has no `dotnet` executable, so focused P7.1B-A,
unchanged P7.1A regression, and broader Phase 7 totals cannot be truthfully reported. No new observed
test failure is being concealed; the fixture absence and unavailable SDK are certification blockers.
Azure OpenAI calls: **0**. Azure Speech calls: **0**. No narration prose or physical packet publication
was produced. P7.1B-B has not begun.
