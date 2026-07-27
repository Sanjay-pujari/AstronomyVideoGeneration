# CG-A3 A3.7 completion report

## Delivery

### Files created

- `Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentarySceneCompositionFullAdapterTests.cs`

### Files modified

- `Backend/src/Astronomy.MediaFactory.ProductionAdapters/SceneCompositionAdapter.cs`
- `Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentarySceneCompositionTestFixtures.cs`
- `docs/cg-a3-existing-ffmpeg-scene-composition-adapter.md`
- `docs/cg-a3-a3.7-completion-report.md`

The reusable fixture owns an isolated workspace and uses the real workspace, checksum, ContentIdentity, artifact inspection, descriptor validation, registry, dependency resolution, diagnostics, and failure-normalization services. Only the scene provider binding and focused video inspector are fakes. No CG-A2 contract, variant composition, generalized A3.9 verification, storage, publishing, or production enablement changed.

## Executed verification

- SDK: .NET SDK 10.0.302.
- Restore: `dotnet restore Backend/Astronomy.MediaFactory.slnx` — succeeded with existing NU1510 and NU1903 warnings.
- Build: `dotnet build Backend/Astronomy.MediaFactory.slnx --no-restore` — the final build succeeded after correcting the newly added test source; the test-project build reported 0 errors and 136 existing warnings.
- Focused command: `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-build --filter "FullyQualifiedName~ProductionAdapters.DocumentaryScene|FullyQualifiedName~ProductionAdapters.ExistingFFmpegDocumentaryScene|FullyQualifiedName~ProductionAdapters.ExistingDocumentaryScene"`
  - Total: **65**
  - Passed: **65**
  - Failed: **0**
  - Skipped: **0**
  - Duration: **646 ms**
- Broad command: `dotnet test Backend/Astronomy.MediaFactory.slnx --no-build`
  - Total: **4,351**
  - Passed: **3,871**
  - Failed: **480**
  - Skipped: **0**
  - Duration: **2 min 10 sec**
  - The broad repository suite is not the A3.7 acceptance gate. Its failures are unrelated pre-existing failures in semantic characterization, thumbnails, analytics, publishing, legacy FFmpeg rendering, path-sensitive architecture tests, and environment-dependent tests.

## Certification results

- **Production fixes driven by tests:** null/malformed response handling, empty-prompt guard, inspector exception normalization, finalization/registry/diagnostic exception normalization, and cancellation preservation.
- **Upstream independence / single scene:** passed; no visual, narration, or subtitle adapter is injected or invoked, one provider call produces one `SceneVideo`, and no `VariantVideo` is produced.
- **Narrated / silent / subtitle burn-in:** passed through the real adapter. Silent composition supplies no artificial audio; burn-in consumes the finalized SRT without regeneration.
- **Cancellation:** provider and inspector cancellation propagate as `OperationCanceledException`, without registration or success diagnostics.
- **Finalization and identity:** atomic finalization, lowercase SHA-256, `sha256:` ContentIdentity, descriptor validation, and physical registry registration passed.
- **Registry:** same-content replay is idempotent; conflicting registration is normalized by the adapter's infrastructure failure boundary.
- **Diagnostics:** the real JSON writer creates the success diagnostic containing logical/requested/measured fields and sanitized stderr hash, without raw command, stderr, authorization, or subscription-key values.
- **Mapper:** successful full-flow output maps to generated O2.18 `SceneVideo`; focused failure mapping preserves safe failure, attempt, and correlation.
- **Architecture / determinism / non-mutation:** all focused tests passed. No blocking async call, upstream adapter dependency, shell invocation, storage/publishing dependency, or request/context mutation was introduced.
- **Real FFmpeg smoke status:** **not executed**. Recording/fake process boundaries were used; no claim of machine FFmpeg execution is made.
- **Known limitations:** MP4 single-scene composition and subtitle burn-in only. Descriptor validation occurs after finalization, so a validation failure can retain an unregistered artifact for quarantine. Variant concatenation, A3.9 verification, storage, and publishing remain out of scope.

## Readiness decision

All 65 focused A3.7 tests passed, including direct real-adapter execution and the mandatory full-flow gates. CG-A2 was unchanged.

**READY FOR A3.8**
