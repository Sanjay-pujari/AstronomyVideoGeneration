# O2.ORCH.4 Task 1B Story Frame audit

| Concern | Existing type/file | Previous behavior | Final reuse decision | Change made |
|---|---|---|---|---|
| Authority entry point | `CreativeStoryboardBuilder.BuildCertifiedFramesAsync` | Built fixed-value frames independently | Retain the in-memory entry point but make it a projection over shared production rules | Authority projection now calls the same visual, composition, camera, background, subject and motion rule methods as the legacy writer |
| Legacy generation | `BuildAndWriteDiagnosticsAsync` / `WriteStoryFramesAsync` | Builds documentary contracts, long/short beats and writes legacy artifacts | Preserve for compatibility; never call file-writing code from authority integration | Pure planning primitives are shared with the authority projection |
| Long/short planning | `BuildLongBeats` / `BuildShortBeats` | Expands long beats and compresses short beats | Preserve as production legacy contract planning | Format-sensitive frame rules are centralized and shared; certified Phase 5 scene order remains authoritative for RC2 authority |
| Production intent | `BuildVisualGoal`, `BuildComposition`, `BuildCameraPlan`, `BuildSubjectFocus`, `BuildBackground`, `BuildMotionHint` | Used only by file-writing frames | Reuse substantively | Both authority and legacy overloads delegate to the same pure rules |
| Core visual planners | `LongStoryFramePlanner`, `ShortStoryFramePlanner`, `NarrativeCompositionEngine` | Separate downstream visual-intelligence domain with incompatible inputs | Do not adapt or duplicate them in Phase 6 | Existing pipeline boundaries remain unchanged |
| Integration | `StoryFrameIntegrationService` | Concrete-builder dependency, builds three artifacts | Keep thin and make invocation mockable | Depends on `ICertifiedStoryFrameBuilder`; builds authority, index and unambiguous variant-scene diagnostics |
| Checksums | `StoryFrameAuthorityChecksum` | Alphabetically sorted requested variants and incidentally ordered frames | Preserve declared variant and canonical frame semantics; sort only unordered sets | `GeneratedUtc` remains excluded; semantic lineage, builder, planning and sequence fields remain included |
| Validation | `StoryFrameArtifactValidator` | Basic identity/checksum/count checks | One validator remains shared by memory, staged reread and resume | Added metadata, exact variant order, scene/frame sequence, timing, relationship, lineage, index and diagnostic checks |
| Resume manifest | `Phase6ManifestIsValid` | Used normalized string prefix containment | Preserve three-role manifest contract and harden paths | Canonical containment, exact directory/filenames, and staging/backup rejection |
| Tests/fixtures | Existing Story Frame planner and RC2 tests | No focused authority checksum suite | Preserve all tests and add focused checksum regression tests | Added timestamp, unordered-set, semantic mutation, variant-order and index tests |

The authority path remains in-memory and has one adapter call. No API, phase numbering, Phase 1–5 behavior, Phase 7 implementation, provider, or application setting is changed.
