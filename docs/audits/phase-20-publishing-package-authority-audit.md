# Phase 20 publishing-package and publish-gate authority audit

**Audit date:** 2026-08-11
**Scope:** repository state on the audited branch; documentation only
**Certified premise:** Phase 18 is physical video assembly authority, frozen Phase 19 is final-video technical-QA authority, and Phase 20 is intended to become publishing-package and publish-gate authority. No production implementation or generated media was changed by this audit.

## 1. Executive summary

**Finding: Phase 20 is not currently a publishing-package authority and is not aligned to the certified Phase 19 authority.** Its active method writes legacy scene manifests, performs broad compatibility copies into legacy folders, runs the old final-output validator, and then enforces a three-Boolean gate. It creates no `20-publishing/{language}` root, package, publishing manifest, package checksum, authority diagnostics/report, canonical Phase 20 validation, transaction, or reuse contract.

The gate reads only `validation/phase-19-validation.json.status` and `.validationPassed`; it does **not** read `19-video-qa/{language}/phase19-manifest.json`, diagnostics, or publication report, does not verify the Phase 19 authority checksum, and does not enforce committed/readback/semantic/checksum/manifest/downstream-ready fields. It also consults legacy `review/qa-report.json` and a Phase 19 `recommendation`, so it continues heuristic review discovery rather than treating certified `technicalQaApproved` as the sole technical signal.

Manual publication consent is currently supplied by an unauthenticated marker file or the request Boolean `PublishApproved`. The database `ContentGenerationPlan.ManualValidation` controls plan-selection/workflow eligibility elsewhere but is not consulted by Phase 20. There is no explicit `RequireManualApproval` or equivalent Phase 20 policy. Phase 20 does **not** auto-set `publishApproved` from technical QA; however, it treats mere marker-file existence as approval, fails a normal pending-review state as a technical phase failure, and requires the old automatic Phase 19 review recommendation in addition to manual approval.

Phase 20 performs no external upload. Separate YouTube/Meta publishing services and endpoints do upload, but they currently discover legacy run artifacts and metadata rather than consuming a committed Phase 20 authority. The recommended boundary is therefore Phase 20 = deterministic, portable, publish-ready package authority; post-Phase-20 adapters = explicitly invoked, idempotency-protected external side effects.

**Recommendation: NO-GO for Phase 20 certification.** Replace the active Phase 20 body (without changing frozen Phases 15–19) with a manifest-driven adapter over Phase 19 and requested supporting authorities, an explicit policy-driven approval model, and transactional `20-publishing/{language}` publication. Retain generic copy helpers only where their byte-for-byte behavior is useful inside staging; remove all legacy discovery from the authoritative path.

## 2. Current Phase 20 entry and call graph

### Exact entry

| Item | Current location |
|---|---|
| Registry | `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs:439` |
| Class | `ProductionPipelineExecutionService` |
| Method | `PhaseFinalValidationAsync` |
| Method declaration | same file, line 14513 |
| Registered label | `(20, "Publishing Package", PhaseFinalValidationAsync)` |

Line numbers are audit-time line numbers and may move after implementation.

### Complete active call graph

```text
ProductionPipelineExecutionService phase loop
  -> PhaseDefinitions()[20] = PhaseFinalValidationAsync
  -> PhaseFinalValidationAsync(context, ct)
       -> WriteScenesManifestsAsync(outputRoot, ct)
            -> enumerate scene-*.png under scene-approval-v3/{short,long}
            -> overwrite scenes.json in both profiles
       -> MaterializePlanFolderAsync(request, eventId, outputRoot, [], ct)
            -> BuildEventWorkingRoot
            -> compatibility CopyFile/CopyDirectoryFiles/CopyHeroArtifact calls
               for questions, scenes, hero, thumbnails, narration, TTS,
               legacy final videos, and assembly manifests
            -> WriteScenesManifestsAsync again
       -> IProductionPipelineQualityValidator.ValidateFinalOutputAsync(...)
            -> ProductionPipelineQualityValidator.ValidateFinalOutputAsync
            -> load current-run intelligence/source notes
            -> validate text/leakage and require both legacy scene profiles
            -> conditionally require legacy video paths
            -> conditionally require legacy hero/thumbnail paths
            -> write production-quality-validation-final.json
       -> WriteAndValidatePublishGateAsync(context, ct)
            -> JsonBool/JsonString/TryGetJsonElement
            -> read partial phase-19-validation + legacy qa-report
            -> discover approval marker files/request Boolean
            -> write validation/phase-20-publish-gate-diagnostics.json
            -> throw InvalidOperationException when gate is false
       -> return copied files + phase-manifest.json path + gate diagnostics path
  -> generic phase executor projects Succeeded/Failed and generated-file list
  -> ContentPlanProductionExecutionService.BuildResult
       -> re-reads phase-20-publish-gate-diagnostics.json
       -> projects PublishGateChecked, PublishApproved, Phase19ReviewApproved
```

There is no current package constructor, platform metadata builder, Phase 20 manifest/checksum validator, ZIP builder, transaction publisher, or external publishing call in this graph.

## 3. Current responsibility (observed, not inferred from the name)

| Capability | Current Phase 20 behavior |
|---|---|
| Final validation | **Yes, legacy.** Broad text/scene checks plus requested legacy video, hero, and thumbnail existence checks. |
| Publish approval | **Yes, weak gate.** Marker existence or request Boolean. |
| Manual-review check | **Yes, implicit.** Four marker locations; no content/authenticity/status validation. |
| Package generation | **No.** |
| Media copying | **Yes, compatibility materialization**, including legacy video/TTS/hero/thumbnail copies; not a publishing package. |
| Metadata generation | **No publishing metadata.** It rewrites scene manifests and a gate diagnostic only. |
| Social/YouTube/short-form metadata | **No.** |
| Thumbnail selection | **No semantic selection.** Compatibility copies use fixed legacy filenames. |
| Hero selection | **No authority selection.** Multiple legacy locations are copied; landscape may be copied to `hero-final.png`. |
| Gallery selection/package | **No.** |
| Caption packaging | **No.** Neither canonical SRT nor ASS is selected/copied. |
| Publishing manifest | **No.** The returned generic `phase-manifest.json` is not built by this method and is not a package manifest. |
| Checksums | **No Phase 20/package checksum.** |
| ZIP/export archive | **No.** |
| External publishing | **No.** |
| Database status update | **Not in Phase 20.** The outer execution service projects results; external publishers persist their own results. |
| Re-encoding/rendering | **No direct transcode**, although it needlessly rematerializes upstream compatibility assets. |

## 4. Legacy behaviors

The active materializer discovers and copies from `question-engine`, `scene-approval-v3`, `hero-assets`, `hero`, `thumbnail-assets`, `thumbnails`, and `video-assembly`. Its video names are `final-video-short.mp4` and `final-video-long.mp4`, not the certified Phase 18 `18-video-assembly/{language}/{short,long}/final.mp4`. It does not read manifest-declared captions. The final validator likewise checks legacy `video-assembly`, `hero/hero.png`, and `thumbnails/{landscape,square,portrait}.png`.

`publishing/` is registered by `Phase1Authority` as the Phase 20 cleanup target, but Phase 20 does not write a package there. It is therefore a **legacy/placeholder ownership entry**, not demonstrated canonical authority or a functioning external-export root. It must not be deleted until all uses are migrated and a compatibility decision is documented.

Phase 20 also writes into shared `scene-approval-v3`, `hero`, `thumbnails`, `narration`, `tts`, and `video-assembly` roots. Those mutations violate the target rule that Phase 20 own only its package root and never rewrite upstream or compatibility media as a prerequisite to governance.

## 5. Phase 19 consumption

Current consumption is limited to two fields in `validation/phase-19-validation.json`. Phase 20 does **not** load:

* `19-video-qa/{language}/phase19-manifest.json`;
* `phase19-authority-diagnostics.json`;
* `phase19-publication-report.json`;
* the certified `authorityChecksum` (`9a74451820539ea1cf490efa84c192c4fefba69bc7725b4b1319c5c788c772e5` for the stated certified run);
* manifest-declared Phase 18 videos/captions or their hashes.

Consequently there is no current `SourcePhase19AuthorityChecksum`. Phase 20 cannot distinguish the Phase 19 authority checksum from the source Phase 18 checksum (`5bdafaea5d5e9086f6afead307354a441c023f82a892ab751835b56ef385c3da`) or an individual asset hash because it reads none of them.

**Target:** resolve exactly one source Phase 19 checksum by reconciling the manifest, publication report, diagnostics, and validation record. Package video/caption paths must then follow Phase 19 -> Phase 18 manifest lineage; folder search is forbidden.

## 6. Phase 19 governance gate

Before any package staging, require all of the following from mutually consistent committed artifacts:

```text
status == Succeeded
publicationCommitted == true
committedReadbackPassed == true
committedStateValidationPassed == true
semanticValidationPassed == true
checksumValidationPassed == true
manifestValidationPassed == true
validationStatus == Valid
manifestValidationStatus == Valid
technicalQaApproved == true
downstreamReady == true
authorityChecksum is a nonempty, valid SHA-256 and agrees across artifacts
```

Failure is `P20_UPSTREAM_PHASE19_INVALID`, before any media discovery or package mutation. Phase 20 should reference `technicalQaApproved=true` and the Phase 19 authority checksum rather than copying detailed motion/audio metrics.

## 7. Technical QA versus publish approval

Current gate meanings are:

* `phase19QaPassed`: only Phase 19 validation `validationPassed && status=Succeeded`;
* `phase19ReviewApproved`: Phase 19 `recommendation=Approved` or legacy QA report approval;
* `publishApproved`: any approval marker exists or request `PublishApproved=true`;
* gate: all three must be true.

Thus technical QA is **not directly auto-promoted** into `publishApproved`, which is good. However, the separate automatic `phase19ReviewApproved` requirement is obsolete under frozen Phase 19, where publish gate and editorial approval deliberately remain false. Continuing it would reject a certified Phase 19 authority. A marker file is also not a governed human decision.

Target states must be distinct:

| State | Meaning |
|---|---|
| `technicalQaApproved` | Read-only Phase 19 technical decision. |
| `manualReviewRequired` | Versioned publishing-policy decision. |
| `manualReviewCompleted` | A governed decision exists. |
| `manualReviewApproved` | Governed human/editorial decision is Approved. |
| `publishGateChecked` | Phase 20 evaluated all applicable policy inputs. |
| `publishApproved` | Technical approval, package validation, and configured review policy all pass. |
| `publicationPackageReady` | Package itself is complete/valid; may be true while approval is pending. |
| `downstreamReady` | Package is committed and `publishApproved=true`. |

## 8. Manual approval model and actual product policy

Repository search found:

* request-level `PublishApproved` (default false), forwarded into the pipeline;
* four approval marker paths checked by Phase 20;
* database `ContentGenerationPlan.ManualValidation` (`manual_validation`, default false), used in plan-selection/manual-run logic, not Phase 20 approval;
* astronomy-event `AutoGenerateAllowed`, controlling generation eligibility, not publication consent;
* a separate legacy `PublishingOptions.Enabled` flow that can auto-invoke external publishing after another pipeline; it is not a Phase 20 gate policy.

No explicit `RequireManualApproval`, `EditorialApproval`, versioned `PublishGate`, or Phase 20 `AutoPublish` policy governs this execution. Therefore current effective Phase 20 policy is “manual approval always required,” implicitly, because `publishApproved` defaults false and the gate always requires it. This is not explicit enough to certify.

Recommendation: add a versioned policy with `manualReviewRequired`. When required, store a governed decision record with decision ID, state (`Pending|Approved|Rejected`), policy version, decision time, and stable reviewer/role identifier or pseudonymous subject reference. Validate its contents and provenance; do not trust existence. `manual_validation` may signal workflow routing but must not itself mean Approved. If policy explicitly disables manual review, auto-approval after technical/package checks is permitted and must record that policy decision.

Pending review is a normal governed state: generate/validate the package if desired, set `publicationPackageReady=true`, `publishGateChecked=true`, `publishApproved=false`, `downstreamReady=false`, and return a repository-supported nontechnical outcome. Existing `ProductionPhaseStatus` has no `PendingApproval`; prefer adding a typed result/gate status. If compatibility prevents that, a successfully committed package with `reasonCode=P20_PUBLISH_GATE_PENDING` is less misleading than throwing “technical failure.” Rejection similarly preserves `technicalQaApproved=true` while returning `publishApproved=false` and `P20_PUBLISH_GATE_REJECTED`.

## 9. Requested-output handling

The request mapper can resolve `ShortVideo`, `LongVideo`, `Thumbnail`, `HeroAsset`, and `Gallery`. Phase gating already skips 11/12/13 and relevant video phases when their outputs are unrequested, and the legacy final validator conditionally checks videos/hero/thumbnails. Gaps:

* Phase 20 itself always runs;
* it performs compatibility copies regardless of requested output;
* it always requires both short and long scene folders;
* Gallery is neither checked nor packaged;
* captions are not considered;
* Phase 19 applicability currently tracks only `LongVideo`, while Phase 18 can assemble either video (an upstream orchestration concern to adapt around, not modify here).

Target Phase 20 must derive a sorted role set from requested outputs and require only corresponding authorities. `ShortVideo`/`LongVideo` require certified Phase 19 declarations; Thumbnail/Hero/Gallery independently require their Phase 12/11/13 validation, committed report, manifest, checksum, and `downstreamReady=true`. Unrequested supporting authorities must not block publication and must not enter identity.

## 10. Video authority

Canonical inputs are Phase 19 manifest declarations tracing to Phase 18, expected for the certified run at:

* `18-video-assembly/en/short/final.mp4`;
* `18-video-assembly/en/long/final.mp4`.

Phase 20 currently uses neither. It copies/checks legacy `video-assembly/{short,long}/final-video-{short,long}.mp4`. Target resolution must begin only after Phase 19 governance passes, select requested profiles from the Phase 19/18 manifests, enforce safe relative paths, and verify size/hash. No ffprobe replay is necessary beyond lightweight readback; Phase 19 owns technical QA.

The current top-level `ShortVideoGenerated`, `LongVideoGenerated`, `FinalShortVideoPath`, and `FinalLongVideoPath` are calculated from still older `video/{short,long}/final-{short,long}.mp4` paths in the orchestration result. Phase 20 must not claim it generated videos. Future projection should use `shortVideoIncluded`/`longVideoIncluded` or clearly documented inherited canonical references; retain generated flags only as compatibility fields sourced from their owning phase.

## 11. Caption authority and policy

Current Phase 20 neither discovers nor packages captions. The canonical Phase 18 lineage supplied through Phase 19 is:

```text
short/captions/en.srt
short/captions/en.ass
long/captions/en.srt
long/captions/en.ass
```

Never revive flat `final.srt`. Package SRT as the upload sidecar for supporting platforms; preserve ASS as a canonical archival/rendering sidecar, not a universal platform upload. Record `burnIn=true` and `sidecarAvailable=true` for the current production policy and never burn again. Platform adapters choose supported sidecars from typed roles.

## 12. Thumbnail authority and mapping

Canonical Phase 12 authority is:

```text
12-thumbnails/
  thumbnail-asset-manifest.json
  phase12-authority-diagnostics.json
  phase12-publication-report.json
  thumbnail-landscape.png
  thumbnail-portrait.png
  thumbnail-square.png
validation/phase-12-validation.json
```

The manifest declares variant roles and physical metadata/checksums. Current Phase 20 ignores it and copies legacy `thumbnail-assets`/`thumbnails` paths. Target mapping is semantic: landscape -> YouTube long / 16:9 web card where policy declares it; portrait -> Shorts/Reels/mobile social; square -> explicitly square consumers. Never select the first file. Any adapter-specific conversion/compression (the YouTube service currently may compress oversized thumbnails) occurs after Phase 20 and must not mutate canonical package artifacts.

## 13. Hero authority and usage

Canonical Phase 11 authority is:

```text
11-hero/
  hero-asset-manifest.json
  phase11-authority-diagnostics.json
  phase11-publication-report.json
  hero-landscape.png
  hero-portrait.png
  hero-square.png
validation/phase-11-validation.json
```

The responsive hero service publishes manifest-declared variants transactionally. Current Phase 20 ignores this authority and copies several legacy hero locations, even projecting landscape to `hero-final.png`. Target role is website/article cover or explicitly declared social banner. Hero must not silently substitute for a video thumbnail.

## 14. Gallery authority and usage

Canonical Phase 13 authority is:

```text
13-gallery/
  gallery-manifest.json
  phase13-authority-diagnostics.json
  phase13-publication-report.json
  gallery-01.png ... gallery-06.png
  observation-guide.json   (supporting projection)
validation/phase-13-validation.json
```

The manifest defines six ordered 16:9 pages and their semantic/physical lineage. Phase 20 currently ignores Gallery. Gallery is best treated as website/CMS/carousel media-library content, included only when `Gallery` is requested and only in a generic/CMS package section. It must not be attached to YouTube or a video-platform request merely because files exist. `observation-guide.json` may remain internal supporting metadata unless an explicit public CMS policy selects safe fields.

## 15. Metadata authority

Existing plan/intelligence inputs already provide title, short title, event type, language, scheduled UTC, recommended publish window, requested content types, source external event ID, warnings/source notes, and timezone. Separate legacy publishing flows generate SEO/platform metadata (`PublishRequest` contains title, description, tags, privacy, thumbnail/video paths; Meta uses caption/short title).

Phase 20 currently creates none of this and no prior canonical **publishing-copy authority** was found in the active Phase 20 lineage. Therefore Phase 20 should only project governed existing copy and identity/schedule fields. Missing description/tags/hashtags/CTA authority is a gap, not permission to call AI or rewrite copy. Define a read-only upstream publishing-copy contract before making those fields required.

Recommended generic metadata fields: PlanId, title/shortTitle (with source artifact/pointer), language, eventType, requested roles, schedule metadata, visibility policy, typed asset/caption references, `burnIn`, `sidecarAvailable`, supporting authority checksums, and source/reference attribution classification. Platform-specific formatting belongs to adapters or deterministic projections whose version enters policy identity.

## 16. Current publishing root

There is no functional current Phase 20 package root. `publishing/` appears in cleanup ownership but is not written by `PhaseFinalValidationAsync`; classify it as **compatibility/placeholder, not canonical**. Existing external publishers operate from pipeline output directories and write publish-result diagnostics there, which is a separate legacy deployment flow.

## 17. Target publishing root

Use the numbered, language-scoped authority:

```text
20-publishing/{language}/
  publishing-manifest.json
  publishing-package.json
  phase20-authority-diagnostics.json
  phase20-publication-report.json
  media/{short,long}/final.mp4
  captions/{short,long}/{language}.{srt,ass}
  thumbnails/...
  hero/...
  gallery/...
  metadata/...
validation/phase-20-validation.json
```

Phase 20 owns only `20-publishing/{language}` plus its validation record. Cleanup must protect every upstream numbered authority, specifically `11-hero`, `12-thumbnails`, `13-gallery`, `18-video-assembly`, and `19-video-qa`. The cleanup catalog should replace the generic `publishing` ownership only after compatibility consumers are identified/migrated.

## 18. Copy/reference strategy

Choose a hybrid of **B and C**:

1. The canonical Phase 20 authority manifest references immutable upstream canonical files and hashes, minimizing duplicate storage.
2. When a portable/staging export is requested, materialize byte-for-byte copies under the same transactional Phase 20 candidate and include `packageRelativePath`.
3. Do not create a portable copy for every internal run unless policy requests it.

This preserves lineage and minimizes duplication while providing a self-contained handoff for external adapters. Copy via streams/files only: no transcode, resize, recompression, subtitle burn, or remix. Every copied SHA-256 and byte length must equal its source. Platform-specific conversions are non-authoritative adapter outputs.

## 19. Package manifest

Use a versioned schema and typed role enum:

```text
ShortVideo, LongVideo,
ShortCaptionSrt, ShortCaptionAss, LongCaptionSrt, LongCaptionAss,
ThumbnailLandscape, ThumbnailPortrait, ThumbnailSquare,
HeroLandscape, HeroPortrait, HeroSquare,
GalleryImage, Metadata
```

Each ordered artifact entry must contain `role`, `format`, `language`, `sourcePhase`, `sourceAuthorityChecksum`, `sourceRelativePath`, nullable `packageRelativePath`, `sha256`, `byteLength`, and `contentType`. Reject unsafe/absolute/traversal paths, missing/empty files, duplicate roles where cardinality is one, duplicate destinations, hash/length mismatch, invalid metadata references, and missing requested roles.

`publishing-package.json` should record package ID, plan/language, requested outputs/roles, generic metadata, policy version, gate state, source Phase 19 checksum, supporting checksums, portability mode, and manifest path. Diagnostics/report remain evidence, not semantic input authority.

## 20. Checksums and package identity

Define `PublishingPackageId` deterministically from canonical serialization of:

```text
PlanId + language + sorted requested roles
+ SourcePhase19AuthorityChecksum
+ applicable Phase11/12/13 authority checksums
+ PublishingPolicyVersion
```

Compute a distinct Phase 20 `authorityChecksum` from schema/serializer version, source authority checksums, ordered artifact identities (role/path/hash/length/content type), metadata identity, publish-gate policy, and governing approval decision identity/state. Never copy the Phase 19 checksum.

If approval changes publishability, include a stable approval decision ID/status and policy version; include decision time only if policy makes it semantically relevant. Do not hash/display names, emails, tokens, or raw user profiles. A Pending -> Approved transition invalidates/rebuilds Phase 20 only.

## 21. Transaction model

Build beneath `20-publishing/.staging/{transactionId}/{language}` (or a sibling staging directory where same-volume atomic replacement requires it), then:

1. validate governance inputs before staging;
2. resolve safe manifest paths;
3. construct/copy candidate and manifest;
4. validate semantics, hashes, cardinality, metadata references, and candidate readback;
5. atomically swap the complete language authority with recoverable backup;
6. read back committed manifest and all package files;
7. write/confirm publication report and canonical validation state;
8. remove staging/backup only after success, restoring the prior authority on failure.

No partial package may be observable. Publication evidence must include `publicationCommitted`, `committedReadbackPassed`, and `committedStateValidationPassed`.

## 22. Reuse and overwrite

Reuse only when the committed Phase 20 authority is fully valid and the Phase 19 checksum, applicable supporting checksums, policy/schema/serializer version, approval decision state/ID, language, requested roles, metadata identity, and copy/reference strategy are identical. Revalidate committed readback without retriggering external publishing.

`overwriteExisting=true` rebuilds only Phase 20 package/authority. It must never regenerate, clean, or rewrite upstream assets. Approval Pending -> Approved similarly invalidates only Phase 20. External adapters need their own idempotency key (`PublishingPackageId + authorityChecksum + platform + channel/account + adapter policy version`) so package reuse cannot duplicate a post.

## 23. Publish-gate states

| Case | Technical | Package | Review | Result |
|---|---:|---:|---|---|
| Approved | true | ready | not required, or completed+approved | `publishGateChecked=true`, `publishApproved=true`, `downstreamReady=true` |
| Pending | true | ready | required, incomplete | `P20_PUBLISH_GATE_PENDING`; approved/ready-for-external=false without technical invalidation |
| Rejected | true | ready | completed, rejected | `P20_PUBLISH_GATE_REJECTED`; `publishApproved=false`; preserve Phase 19 validity |
| Technical invalid | false/unknown | not built | any | `P20_UPSTREAM_PHASE19_INVALID`; fail before discovery |
| Package invalid | true | false | any | package reason code; no commit/downstream readiness |

The package may be committed while awaiting approval if policy explicitly supports that state. In that model distinguish `publicationPackageReady` from `downstreamReady`. A rejected decision can remain auditable without making the package externally consumable.

## 24. External platform boundary

Current Phase 20 does **not** call YouTube, Facebook, Instagram, X/Twitter, TikTok, Reels, Shorts, website, or CMS APIs. Separate `ContentPublishService`, `YouTubePublishService`, `MetaPublishService`, Facebook services, Instagram service, API endpoints, retry operations, and a legacy auto-publish orchestrator do perform side effects.

Those publishers currently consume pipeline-run output and legacy naming/metadata, not a Phase 20 authority. Target every downstream external publisher to consume only a committed Phase 20 manifest/package with `publishApproved=true` and `downstreamReady=true`. Retain upload services, but split their artifact resolution behind a Phase 20 adapter and remove heuristic discovery after migration. X/Twitter and TikTok were not found as active Phase 20 integrations.

## 25. Scheduling

`ScheduledUtc`, `recommendedPublishWindow`, and timezone are available in planning/intelligence models, while publishing entities/options have their own scheduling fields. Phase 20 currently neither packages nor schedules them. Record normalized UTC plus original timezone/window provenance as metadata; do not execute external scheduling in Phase 20. Platform adapters translate the governed schedule under a versioned policy and record external IDs/results separately.

## 26. Security, secrets, rights, and attribution

No Phase 20 method reads API keys, access tokens, channel IDs, or social credentials. External publisher configuration does. Keep all credentials/account identifiers outside authority checksum, manifest, diagnostics, and public package; include only a non-secret target policy alias when necessary.

Phase 18 has music-selection/source mechanics, but this audit found no complete canonical publication-rights contract for music source, asset ID, license, attribution, and checksum in Phase 20. This is a release risk: preserve any certified source/hash metadata available through the video manifest, but never invent a license or claim. Add a governed rights/attribution authority or explicit “rights metadata unavailable/review required” gate before public release.

Astronomy source notes exist in intelligence metadata and provenance authorities. Keep internal research notes traceable by source artifact/pointer/checksum, but do not expose them publicly by default. A publishing-copy/rights policy must explicitly select public citations/attribution.

## 27. Result projection

Current outer result exposes generic phase status/errors/generated files and re-reads only `PublishGateChecked`, `PublishApproved`, and `Phase19ReviewApproved`. It lacks the required Phase 20 authority fields.

Target projection:

```text
status, reason, reasonCode
generated, reused, regenerated
authorityChecksum, sourcePhase19AuthorityChecksum
publishingPackageId, publishingPackagePath, publishingManifestPath
publicationCommitted, committedReadbackPassed, committedStateValidationPassed
validationStatus, manifestValidationStatus
semanticValidationPassed, checksumValidationPassed, manifestValidationPassed
technicalQaApproved
manualReviewRequired, manualReviewCompleted, manualReviewApproved
publishGateChecked, publishApproved, publicationPackageReady, downstreamReady
```

Paths should be repository-relative or explicitly rooted contract paths, never secret-bearing URLs. Platform package/export paths may be added only for actually materialized adapters. Existing video-generated flags must remain owning-phase compatibility projections, not Phase 20 generation claims.

## 28. Failure/reason codes

Adopt:

* `P20_UPSTREAM_PHASE19_INVALID`
* `P20_SUPPORTING_AUTHORITY_INVALID`
* `P20_PUBLISH_GATE_PENDING`
* `P20_PUBLISH_GATE_REJECTED`
* `P20_PACKAGE_ARTIFACT_MISSING`
* `P20_PACKAGE_CHECKSUM_MISMATCH`
* `P20_PACKAGE_METADATA_INVALID`
* `P20_CANDIDATE_VALIDATION_FAILED`
* `P20_PUBLICATION_FAILED`
* `P20_COMMITTED_READBACK_FAILED`
* `P20_PUBLISHING_AUTHORITY_ACCEPTED`

Pending/rejected are gate outcomes, not proof of invalid technical media. Preserve machine-readable reason code independently of explanatory text.

## 29. Current test inventory

No direct unit/integration test was found for `PhaseFinalValidationAsync` or `WriteAndValidatePublishGateAsync`. Relevant adjacent inventory:

| File/test group | Existing behavior |
|---|---|
| `ProductionPipelineExecutionServiceTests.PhaseGating_ThumbnailOnly_RunsSceneAudioSyncButSkipsVideoPhasesNotRequested` | Requested-output phase gating; Phase 20 still always applicable. |
| `ProductionPipelineExecutionServiceTests.RequestedOutputCompletion_ReportsSkippedForUnrequestedLongVideo` and nearby completion tests | Per-output completion projection. |
| `ProductionPipelineExecutionServiceTests.OverwriteCleanup_Phase13Only_PreservesEarlierValidationAndOtherOutputRoots` | Upstream cleanup protection for a partial phase. |
| `ProductionPipelineExecutionServiceTests.Phase12Owns12Thumbnails`, `Phase12DoesNotTreatLegacyThumbnailsAsSemanticAuthority`, `Phase12OverwriteTargets12ThumbnailsOnly` | Canonical Phase 12 ownership/no legacy authority. |
| `ProductionPipelineExecutionServiceTests.Phase18CleanupDoesNotOwnLegacyVideoAssemblyRoot` | Legacy video-assembly cleanup boundary. |
| `ProductionPipelineExecutionServiceTests.Phase19LegacyVideoQaRootIsCompatibilityOnly` | Phase 19 numbered-root ownership. |
| `Phase11ExecutionGateTests`, `ResponsiveHeroPositiveExecutionTests` | Hero requested-output applicability and canonical authority behavior. |
| `Phase13ExecutionGateTests` | Gallery applicability for requested outputs. |
| `ResponsiveHeroAuthorityServiceTests` | Phase 11 transaction/manifest/readback behavior. |
| `ResponsiveThumbnailAuthorityServiceTests` | Phase 12 authority, variants, lineage, transaction, cleanup. |
| `Phase13PhysicalMetadataTests` and Phase 13 authority tests | Gallery physical metadata/checksum constraints. |
| `YouTubePublishingIntegrationTests.DryRun_WritesPayload_AndDoesNotCallYouTube` | External dry-run boundary. |
| `...ValidationFailure_BlocksPublish` | Legacy pre-publish validation blocks upload. |
| `...LongVideoAsset_IsDetected`, `...ShortVideoAsset_IsDetected_FromShortsShortVideo`, `...ShortVideoAsset_DoesNotRequireShortsFinalVideo` | Heuristic/legacy media selection. |
| `...YouTubeShort_UploadsGeneratedShortThumbnail...`, long-thumbnail/missing/compression tests | Platform thumbnail resolution/fallback and adapter mutation. |
| `...ManualAssetSelector...`, `...ManualEndpoint_PublishesExistingRun` | Explicit external side-effect selection/API. |
| `...AutoPublish_UsesContentPublishService_WhenPipelinePublishingEnabled` | Separate legacy pipeline auto-publish configuration. |

Missing tests must cover every Phase 19 governance field, checksum reconciliation, requested role subsets, exact 11/12/13 manifest selection, canonical captions, safe paths, hash preservation, pending/rejected/auto-approved policies, transaction rollback/readback, deterministic reuse, overwrite isolation, result projection, secret exclusion, and external idempotency.

## 30. Obsolete tests/expectations

Flag for replacement (not necessarily deletion until compatibility removal):

* YouTube tests whose success depends on heuristic legacy video naming instead of Phase 20 manifest roles;
* thumbnail fallback/compression expectations that treat legacy images as canonical input (adapter conversion behavior may remain after manifest resolution);
* any final-validator expectation for `video-assembly/.../final-video-*.mp4`, `hero/hero.png`, or legacy `thumbnails/*.png`;
* any expectation that flat `final.srt`, old outro media, or arbitrary folder search is valid (no direct Phase 20 caption test currently exists);
* any test assuming `publishing/` is canonical solely because cleanup registers it;
* any gate test that equates automatic Phase 19 recommendation with editorial approval, trusts marker existence, fails pending approval technically, auto-approves without explicit policy, or packages all outputs regardless of request.

## 31. Current-versus-target matrix

| Concern | Current | Target |
|---|---|---|
| Primary authority | Legacy folders + partial validation JSON | Committed Phase 19 authority |
| Technical QA source | Two validation fields + legacy recommendation/report | Full Phase 19 governance contract + checksum |
| Publish gate | Three implicit Booleans, throws | Typed policy/gate state |
| Manual approval | Marker existence/request flag | Governed versioned decision or explicit no-review policy |
| Video resolution | Legacy fixed paths/copies | Phase 19 -> Phase 18 manifest declarations |
| Caption resolution | None | Manifest-declared language SRT/ASS |
| Thumbnail authority | Legacy fixed paths | Phase 12 manifest semantics |
| Hero authority | Multiple legacy searches/copies | Phase 11 manifest semantics |
| Gallery authority | Ignored | Phase 13 manifest; requested CMS/carousel role only |
| Metadata source | None in Phase 20 | Governed upstream copy + deterministic policy projection |
| Package root | None; `publishing/` placeholder | `20-publishing/{language}` |
| Copy/reference | Compatibility copying | Reference by default, portable copy on request |
| Manifest | None | Versioned typed artifact manifest |
| Checksums | None | Source hashes + distinct deterministic P20 checksum |
| Transaction | None | Stage, validate, atomic commit, committed readback |
| Reuse | None | Complete authority identity match |
| External publishing | Separate but legacy-input | Separate adapter consuming approved Phase 20 |
| Result projection | Three gate flags + generic status | Full authority/validation/gate/package projection |

## 32. Code reuse classification

| Code | Classification | Reason |
|---|---|---|
| Phase registry/phase executor | **REUSE AS-IS** | Phase 20 dispatch and generic orchestration remain useful. |
| `PhaseFinalValidationAsync` signature | **REUSE WITH PHASE19 ADAPTER** | Entry is correct; body is not. |
| `CopyFile` low-level byte copy | **REUSE PACKAGING UTILITY** | Only inside staging with required hash/length checks; never discovery. |
| Generic JSON/path/hash helpers | **REUSE PACKAGING UTILITY** | Prefer typed deserialization and safe-path helper. |
| `ProductionPipelineQualityValidator.ValidateFinalOutputAsync` in Phase 20 | **REMOVE FROM ACTIVE PATH** | Repeats legacy final QA/discovery; Phase 19 owns technical QA. |
| `WriteScenesManifestsAsync` from Phase 20 | **REMOVE FROM ACTIVE PATH** | Mutates upstream compatibility state. |
| `MaterializePlanFolderAsync` from Phase 20 | **COMPATIBILITY ONLY** | Broad legacy copies; not authority-driven packaging. |
| `WriteAndValidatePublishGateAsync` | **REUSE WITH PHASE19 ADAPTER** concept only | Replace implicit inputs/throw semantics with typed policy evaluator. |
| marker-file discovery and QA recommendation fallback | **OBSOLETE** | Ungoverned and conflicts with frozen Phase 19 model. |
| `publishing/` cleanup registration | **COMPATIBILITY ONLY** | No current package writer; migrate to numbered root. |
| external platform publishers | **REUSE WITH PHASE19 ADAPTER** (actually Phase 20 adapter) | Keep side-effect clients; change their input authority and idempotency. |

## 33. Minimal implementation plan

1. Add typed read-only Phase 19 loader; reconcile manifest, diagnostics, report, validation, and exactly one source checksum.
2. Derive sorted requested package roles and language; do not inspect unrequested authorities.
3. Add typed Phase 11/12/13 authority loaders for requested supporting roles and enforce committed/valid/downstream-ready states.
4. Resolve only manifest-declared files and canonical captions; validate safe paths, size, and hashes.
5. Introduce `PublishingPolicyVersion` and governed manual approval evaluator with Pending/Approved/Rejected/NotRequired.
6. Define typed artifact roles, deterministic metadata projection, package schema, and `PublishingPackageId`.
7. Stage reference manifest and optional portable byte copies without media transformation.
8. Validate candidate cardinality, references, hashes, metadata, empty files, duplicate destinations, and readback.
9. Compute distinct canonical Phase 20 authority checksum including policy/approval identity.
10. Atomically publish `20-publishing/{language}`, validate committed readback, and write validation/report.
11. Implement exact reuse/overwrite behavior and cleanup ownership confined to Phase 20.
12. Project complete API result including package/gate readiness without relabeling inherited video as generated.
13. Adapt external publishers to require approved/downstream-ready Phase 20 authority plus idempotency key; retain explicit dry-run/manual endpoints.
14. Add certification tests, then retire legacy discovery only after compatibility consumers are migrated.

## 34. Files/classes expected to change

Likely production changes (none made in this audit):

* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` — replace Phase 20 body/gate wiring and Phase 20 root ownership integration; do not alter Phase 15–19 implementations.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase1Authority.cs` — narrowly change Phase 20 cleanup target from legacy `publishing` to numbered root while preserving compatibility safely.
* new `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase20PublishingAuthorityPublisher.cs` (or equivalent) — loaders, package builder, checksum, transaction, reuse/readback.
* new or existing Core contracts, likely `Backend/src/Astronomy.MediaFactory.Core/ContentPlanBatchGeneration.cs` and/or a dedicated `Phase20PublishingAuthority.cs` — typed policy, roles, approval/result projection.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ContentPlanProductionExecutionService.cs` — project the canonical Phase 20 validation/result rather than three diagnostic flags.
* `Backend/src/Astronomy.MediaFactory.Publishing/ContentPublishService.cs`, `MetaPublishService.cs`, and platform resolver services — consume Phase 20 package rather than legacy discovery.
* API composition/endpoints only if necessary to pass an approval decision or package ID; secrets remain in existing option stores.
* Phase 20/new publisher tests plus targeted updates to `ProductionPipelineExecutionServiceTests.cs` and `YouTubePublishingIntegrationTests.cs`.

Do **not** modify Phase 15, 16, 17, 18, or 19 production code. Read their frozen contracts through adapters. Phase 11/12/13 implementations likewise need no regeneration change; only safe reader contracts may be shared.

## 35. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Certified Phase 19 ignored | Critical | Hard gate before discovery, checksum reconciliation. |
| Ungoverned marker approval | Critical | Authenticated/versioned decision record. |
| External publishers bypass Phase 20 | Critical | Require committed approved package and idempotency key. |
| Phase 20 mutates upstream compatibility roots | High | Single owned numbered root and transaction. |
| Legacy path selects stale/wrong media | High | Manifest-only resolution. |
| Pending approval reported as technical failure | High | Typed gate state separate from package/technical state. |
| Missing publishing-copy authority | High | Report gap; no AI regeneration in Phase 20. |
| Missing music/content rights contract | High | Rights gate/authority before public release. |
| Duplicate storage | Medium | Reference-first, portable export only on request. |
| Personal/secret data in checksum/package | High | Allowlisted canonical fields; stable non-personal decision ID. |
| Schedule timezone drift | Medium | Preserve UTC and source timezone with policy version. |
| Compatibility consumers depend on `publishing/`/legacy paths | Medium | Inventory, adapter, measured deprecation. |

## 36. Certification criteria / definition of done

Phase 20 is certifiable only when tests and committed evidence prove:

* Phase 19 is the primary direct authority and all governance fields/checksum agree;
* `technicalQaApproved=true` is mandatory and never conflated with publication consent;
* requested roles alone drive Phase 11/12/13/video authority requirements;
* no legacy folder/image/final-media discovery occurs on the authority path;
* exact manifest-declared video, captions, thumbnails, hero, and gallery are used;
* copied bytes preserve upstream SHA-256/length; no transcode, resize, burn, or regeneration occurs;
* publishing metadata is governed/provenanced and no AI copy is silently created;
* approval policy is explicit; Pending/Rejected preserve technical validity; auto-approval is policy-driven only;
* package identity, manifest ordering, serialization, and checksum are deterministic/idempotent;
* `20-publishing/{language}` publication is staged, atomically committed, rolled back on failure, and read back;
* overwrite/review changes touch Phase 20 only and cleanup protects all upstream numbered roots;
* validation/result projection is internally consistent and includes package paths/checksums/gate states;
* `downstreamReady` is true only for a committed, validated, publish-approved authority;
* external adapters consume that exact authority, exclude secrets, and cannot double-publish on reuse/retry;
* caption metadata records current burn-in plus sidecar availability and uploads SRT only where supported;
* rights/attribution gaps are resolved or explicitly gate public publication.

## 37. Remaining uncertainties

1. No explicit product decision states whether every Phase 20 run requires manual editorial approval or which content categories may auto-publish. This must be decided and versioned.
2. No canonical publishing-copy authority was found; ownership of descriptions, tags, hashtags, CTA, category, and visibility needs a governing contract.
3. The desired portable-export default (reference-only versus materialized copy) and external deployment transport are not explicitly specified.
4. Gallery’s exact CMS/carousel consumers and whether `observation-guide.json` is publicly exposed require product confirmation.
5. Hero’s target website/CMS consumers and platform mapping require an explicit policy.
6. Music licensing and public source-attribution requirements are incomplete.
7. Existing external publishers’ production reliance on legacy run folders and `publishing/` has not been established by telemetry; migration must precede deletion.
8. Repository status semantics lack a native `PendingApproval`; contract evolution versus compatibility projection must be chosen.

## 38. Final recommendation and direct answers

**Final recommendation: implement a new Phase 20 authority publisher behind the existing registry entry; do not extend the current legacy validator/materializer.** Gate on frozen Phase 19 first, resolve all assets through manifests, separate package readiness from approval, publish transactionally under `20-publishing/{language}`, and make external adapters consume only that approved authority.

Direct answers required for audit completion:

* **What does Phase 20 currently do?** Rewrites legacy scene manifests, compatibility-copies old artifacts, runs legacy final validation, writes a weak publish-gate diagnostic, and fails unless technical/recommendation/manual Booleans all pass.
* **Does it consume committed Phase 19 authority?** No; it reads only two fields from Phase 19 validation and ignores the canonical authority package/checksum.
* **Does it still use legacy final-media discovery?** Yes, through materialization/final validation; downstream external publishers also use legacy discovery.
* **Who owns manual approval?** Today, Phase 20 implicitly does via marker files/request Boolean. `manual_validation` is workflow selection, not approval. There is no governed policy/decision owner.
* **Is `publishApproved` auto-set incorrectly?** Not from technical QA. It is incorrectly trusted from mere marker existence or an ungoverned request Boolean; automatic Phase 19 recommendation is separately and incorrectly required.
* **What assets currently enter a publishing package?** None—no publishing package exists. Compatibility copies may include questions, scenes, hero, thumbnails, narration, TTS, legacy videos, and assembly manifests; captions/gallery do not enter.
* **Which Phase 11/12/13 artifacts are canonical?** Their numbered manifests, publication reports, diagnostics/evidence, canonical validations, and manifest-declared responsive hero/thumbnail variants or ordered six Gallery pages, as enumerated above.
* **Does Phase 20 copy media or reference it?** It currently copies selected legacy media into compatibility roots. Target is reference-first with optional verified portable copies.
* **What is the canonical Phase 20 root?** None exists today. Adopt `20-publishing/{language}`; `publishing/` is placeholder/compatibility until proven otherwise.
* **Is publishing external or package-only?** Current Phase 20 is neither a package authority nor an uploader. External publishing exists in separate services/endpoints. Target Phase 20 is package-only authority.
* **How does pending approval behave?** Today it throws and fails Phase 20. Target commits/validates the technical package if policy permits, reports Pending, and keeps `publishApproved/downstreamReady=false` without invalidating Phase 19.
* **What authority does a downstream external publisher consume?** Today, no canonical authority—it consumes legacy pipeline-run folders/metadata. Target consumers must accept the committed `20-publishing/{language}/publishing-manifest.json` identified by the distinct Phase 20 checksum and package ID, only when publish-approved and downstream-ready.
