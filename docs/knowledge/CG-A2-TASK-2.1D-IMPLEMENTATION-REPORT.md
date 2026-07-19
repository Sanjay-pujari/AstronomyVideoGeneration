# CG-A2 Task 2.1D — Implementation Report

## Executive summary
Task 2.1D completes the Astronomy Knowledge Foundation integration layer with System.Text.Json serialization, explicit payload discriminator registration, immutable statement round-tripping through the public constructor, minimal dependency injection for the Task 2.1C validator, serialization/DI/architecture tests, and RC1-oriented compatibility verification.

## Architecture status
- Task 2.1A: frozen primitive value objects, enum guards, references, validity, audit, and tags are preserved.
- Task 2.1B: frozen payload marker, localization reference, statement interfaces, and immutable statement envelope are preserved.
- Task 2.1C: frozen validator contract, implementation, and validation codes are preserved.
- Task 2.1D: added non-breaking serialization, payload discriminator seam, DI registration, and integration tests.

## Repository inspection
- Reused System.Text.Json and JsonSerializerDefaults.Web conventions already used by Task 1 tests.
- Reused Task 1 camel-case/string-enum serializer expectations.
- Reused Task 1 extension naming style with AddCgA2...Foundation methods.
- Reused singleton validator lifetime convention from Task 1 domain registration.
- Reused source-level architecture guard tests already present in Task 1 tests.

## Requirement mapping
```text
Stable primitive JSON
→ Existing System.Text.Json convention reused
→ JsonSerializerOptions extension extended for Task 2.1
→ Primitive scalar converters introduced

Statement round-trip
→ Existing immutable constructor reused
→ Serializer configuration extended
→ Dedicated generic statement converter introduced

Payload polymorphism seam
→ Existing System.Text.Json converter model reused
→ Serializer options extension extended with explicit registrations
→ Generic discriminator converter introduced

Dependency injection
→ Existing IServiceCollection extension pattern reused
→ No Task 1 extension changed
→ Task 2.1 foundation DI extension introduced

Architecture verification
→ Existing source-boundary test style reused
→ KnowledgeFoundation-specific guard added
→ No new framework introduced
```

## Serialization contract
- `KnowledgeId`: JSON string, e.g. `"knowledge.synthetic.identity"`.
- `KnowledgeVersion`: JSON integer, e.g. `1`.
- `KnowledgeStatementKind`: JSON string enum, e.g. `"Scientific"`.
- `KnowledgeFoundationStatus`: JSON string enum, e.g. `"Draft"`.
- `KnowledgeLanguageTag`: JSON string normalized by constructor, e.g. `"en-US"`.
- `KnowledgeTag`: JSON string normalized by constructor, e.g. `"moon"`.
- `KnowledgeValidityRange`: camel-case object with UTC `effectiveFromUtc` and `effectiveToUtc` values.
- `KnowledgeAuditMetadata`: camel-case object with UTC creation/update metadata.
- `KnowledgeLocalizationReference`: camel-case object containing `languageTag`, `resourceKey`, `isOriginalTerm`, and `isCanonicalLabel`.
- `AstronomyEntityReference` and `AstronomyFamilyReference`: existing camel-case record object shapes with string-enum taxonomy values.
- `AstronomyKnowledgeStatement<TPayload>`: camel-case object exposing `id`, `version`, `kind`, `status`, `primarySubject`, `familyContext`, `payload`, `localizationReferences`, `tags`, `validity`, and `audit`.
- Payload discriminator: explicit stable `payloadKind` property inside the payload object.

## Payload polymorphism
Payload support is explicit and closed per serializer options instance. Callers register `TPayload` with `AddAstronomyKnowledgePayload<TPayload>("stable.discriminator")`. The converter emits `payloadKind`, rejects missing/unknown/mismatched discriminators, does not serialize CLR type names, does not use assembly-qualified names, and does not perform assembly scanning or unrestricted runtime activation. Task 2.3 can add production payload registrations by calling the same extension for concrete payload types.

## Dependency injection
- Extension method: `AddCgA2AstronomyKnowledgeFoundation()`.
- Services registered: `IAstronomyKnowledgeStatementValidator` and concrete `AstronomyKnowledgeStatementValidator`.
- Lifetime: singleton, matching existing stateless Task 1 domain service conventions.
- Idempotence: `TryAddSingleton` avoids duplicate default validator interface registration.
- Override behavior: a caller can register a custom `IAstronomyKnowledgeStatementValidator` before invoking the extension and keep it.

## Files changed
- Serialization: `KnowledgeFoundation/Serialization/KnowledgeFoundationJsonConverters.cs`, `KnowledgeFoundation/Serialization/AstronomyKnowledgeFoundationJsonOptionsExtensions.cs`.
- DI: `KnowledgeFoundation/Extensions/ServiceCollectionExtensions.cs`.
- Tests: `KnowledgeFoundation/KnowledgeFoundationSerializationAndDiTests.cs`.
- Report: this file.

## Tests added
One test class was added with coverage for primitive serialization, enum stability, complete statement round-trip, malformed JSON rejection, payload discriminators, serializer idempotence, DI registration, and architecture boundaries.

## Architectural self-review
No frozen public contracts were renamed or removed. No public mutable setters or parameterless constructors were added. Deserialization uses existing constructors and therefore preserves constructor guards. No global serializer options, static mutable registries, service locator, reflection scanning, CLR metadata, evidence, confidence, lifecycle, catalog/query, persistence, content generation, or production integration was introduced.

## Compatibility verification
- CG-A1: no source changes; combined DI test keeps CG-A1 registration independently resolvable.
- Task 1: no source changes; serializer test preserves Task 1 string enum behavior.
- Task 2.1A: primitive constructors and enum names unchanged.
- Task 2.1B: statement constructor, equality, and immutable collections unchanged.
- Task 2.1C: validator contract/codes unchanged; deserialized statements validate successfully.
- Existing serialization/DI behavior: new configuration is opt-in per JsonSerializerOptions and DI extension is separate/idempotent.

## Commands executed
- `pwd && find .. -name AGENTS.md -print && rg -n "JsonSerializerOptions|JsonConverter|AddCgA|IServiceCollection|TryAdd|Architecture" -S .`
- `cat AGENTS.md 2>/dev/null || true; find .. -path '*/AGENTS.md' -print -exec cat {} \;`
- `find Backend/src/Astronomy.MediaFactory.Core/AstronomyDomain -type f -maxdepth 5 | sort && find Backend/tests/Astronomy.MediaFactory.Tests/AstronomyDomain -type f | sort && find Backend -name '*.sln' -o -name '*.csproj'`
- `find Backend/src/Astronomy.MediaFactory.Core -path '*Knowledge*' -type f -print; find Backend/tests/Astronomy.MediaFactory.Tests -path '*Knowledge*' -type f -print`
- `sed -n '1,260p' Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/KnowledgePrimitives.cs; sed -n '1,260p' Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/KnowledgeStatements.cs; sed -n '1,220p' Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/AstronomyKnowledgeStatementValidator.cs`
- `dotnet restore Backend/Astronomy.MediaFactory.sln 2>/dev/null || dotnet restore Backend/src/Astronomy.MediaFactory.Core/Astronomy.MediaFactory.Core.csproj`
- `dotnet build Backend/Astronomy.MediaFactory.sln --no-restore`
- `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-build --filter "FullyQualifiedName~KnowledgeFoundation|FullyQualifiedName~AstronomyDomain"`
- `git diff --check`

## Final verification
Restore/build/test commands could not run in this container because `dotnet` is not installed. `git diff --check` passed. Physical verification searches were performed with ripgrep during implementation and review.

## Acceptance checklist
- Serialization: Passed by implementation and focused tests; execution blocked by missing dotnet.
- Dependency injection: Passed by implementation and focused tests; execution blocked by missing dotnet.
- Validation integration: Passed by constructor-based deserialization and validator test.
- Architecture: Passed by source guard test and review.
- Integration: Passed by review of Task 2.1A-C plus new Task 2.1D layer.
- Quality: Partially verified; repository cannot execute .NET tests in this environment.

## Explicit non-goal confirmation
No evidence, confidence, typed knowledge domains, lifecycle transitions, relationships, catalog, query, persistence, content generation, production integration, real astronomy data, Orion, or constellation implementation was added.

## Remaining risks
- Automated .NET restore/build/test verification must be rerun in an environment with the .NET SDK installed.

## RC1 recommendation
Task 2.1 is ready for independent RC1 review after CI or a developer workstation with the .NET SDK verifies restore, build, focused tests, full tests, and architecture tests.
