# CG-A3 A3.8 completion report

## Scope and files

The closure added the real-adapter fixture and split full-flow, compatibility, mapper, determinism, and non-mutation tests. It modified `VariantCompositionAdapter.cs` only to normalize focused-inspector exceptions while preserving caller cancellation and to include the already-sanitized stderr hash in success diagnostics. CG-A2 contracts, storage, publishing, production enablement, and A3.9 were not modified.

## Execution record

- SDK: .NET SDK 10.0.302 (installed in the isolated `/tmp/dotnet` tool directory).
- Restore: succeeded; existing NU1510 and NU1903 warnings were reported.
- Build: succeeded with 0 errors; repository-existing warnings remain.
- Focused command: `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-build --filter "FullyQualifiedName~ProductionAdapters.DocumentaryVariant|FullyQualifiedName~ProductionAdapters.ExistingFFmpegDocumentaryVariant|FullyQualifiedName~ProductionAdapters.ExistingDocumentaryVariant"`.
- Focused result: total 28; passed 28; failed 0; skipped 0; duration 769 ms.
- Broad command: `dotnet test Backend/Astronomy.MediaFactory.slnx --no-build`.
- Broad result: cancelled after approximately 2 minutes 54 seconds after numerous unrelated pre-existing failures; therefore no truthful final broad total, pass, fail, or skip count is available.
- Unrelated broad failures included astronomy context expectations, semantic characterization and DI validation, thumbnail generation, language validation, and missing local `ffprobe` execution.
- Real FFmpeg smoke status: not executed.

## Certification results

The executed focused suite passed the real adapter full flow, upstream independence, one-variant behavior, shuffled-request scene ordering, landscape and portrait profiles, required-audio and video-only behavior, provider ownership, provider and inspector cancellation, inspector exception normalization, output validation, atomic finalization, SHA-256/content identity, descriptor validation, registry registration and replay, sanitized diagnostics, result mapping, deterministic final identity, non-mutation, stable provider identity, and disabled-by-default architecture boundary. The compatibility validator directly passed MP4, dimension, frame-rate tolerance, and audio-policy cases.

Known limitations remain: no real FFmpeg smoke was performed; the broad repository suite has unrelated failures and was cancelled before totals; finalization precedes descriptor validation; codecs, pixel format, and time base are not represented by the A3.8 descriptor. Generalized A3.9 verification was not added.

## Decision

The focused A3.8 tests created in this closure pass, but the entire exhaustive matrix requested for resolver, provider-binding, focused-inspector, and DI behavior has not been completed and the broad suite did not finish cleanly.

**NOT READY FOR A3.9**
