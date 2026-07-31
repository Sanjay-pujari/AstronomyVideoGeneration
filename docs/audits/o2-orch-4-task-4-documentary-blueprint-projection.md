# O2.ORCH.4 Task 4 — Documentary Blueprint Projection Audit

## 1. Architecture implemented

`DocumentaryBlueprintProjector` is a pure, synchronous integration layer from the certified
`DocumentaryIntent` authority to two existing `DocumentaryBlueprint` domain instances. It performs
validation, maps each opportunity to one builder input, calls the existing builder once for each
variant, creates two projection envelopes, and then creates and validates one canonical aggregate.
It performs no publication or filesystem work.

## 2. Files added

- `DocumentaryBlueprintProjectionContracts.cs`: request/result, traceability, variant, aggregate,
  coverage, duration, issue, and service contracts.
- `DocumentaryBlueprintProjectionChecksum.cs`: canonical web-JSON SHA-256 checksums.
- `DocumentaryBlueprintProjector.cs`: thin validation and projection service.
- `DocumentaryBlueprintAggregateValidator.cs`: cross-variant and aggregate reconciliation.
- `DocumentaryBlueprintProfileResolver.cs`: data-free adapter over profiles supplied by the owning
  composition/catalog registration.
- This audit.

## 3. Files modified

Only `ServiceCollectionExtensions.cs` was modified to register the resolver, aggregate validator,
and projector. No Phase 1–3 writer, schema, planner, builder, or production pipeline execution path
was changed.

## 4. Existing builder reuse evidence

The projector constructs one `DocumentaryBlueprintBuildRequest` within the per-variant method and
calls the injected `DocumentaryBlueprintBuilder.Build` exactly once. The method is called once with
`LongVariantIntent` and once with `ShortVariantIntent`. `DocumentaryBlueprintBuilder.cs` is unchanged;
there is no second blueprint construction engine.

## 5. Canonical aggregate contract

`DocumentaryBlueprintAggregate` carries stable schema/contract/projection versions, deterministic
identity, execution authority, profile authority, source intent and lineage, exactly one complete Long
and one complete Short `DocumentaryBlueprintVariantArtifact`, deterministic union coverage, duration
summary, diagnostics, and its checksum. Blueprints and projection checksums are compatibility views of
the embedded authorities, so future external files can add no information. It exposes no collection into which a Master variant could
be inserted.

## 6. Long variant projection

Long is projected only by passing `intent.LongVariantIntent` and `profile.LongProfile` to the isolated
per-variant projection method. Its publication format is `LongDocumentary`.

## 7. Short variant projection

Short is independently projected only from `intent.ShortVariantIntent` and `profile.ShortProfile`.
It does not enumerate Long scenes, truncate Long, or inherit Long ordering. Its publication format is
`ShortDocumentary`.

## 8. Scene mapping

| Builder field | Authoritative source | Mapping rule | Validation rule |
|---|---|---|---|
| Scene ID / number | opportunity identity + order | deterministic variant-qualified ID; exact order | exact positional equality |
| Title / viewer question | primary viewer question | preserve text | exact equality and trace question ID |
| Stage / role | profile slot via opportunity | enum conversion only | exact equality |
| Objective summary / learning goal | certified objective | preserve objective text | exact value and objective ID |
| Objective curiosity / emotional goals | profile slot authorities | preserve distinct values | exact equality |
| Editorial outcome / priority | profile slot authorities | preserve outcome/code/priority | exact equality plus safety trace |
| Knowledge references | certified selections | narrow builder view; full lineage in trace | exact ordered equality |
| Visual opportunity | profile slot authority | preserve description/type/required flag | exact equality |
| Transition intent / next seed / direction | three distinct profile slot authorities | preserve each independently | exact equality |
| Duration | planner allocation and profile range | preserve target | target equality and trace min/max |


Opportunity order, stage, role, primary question text, objective text, editorial outcome, selected
knowledge, high-level visual opportunity, transition intent, and target duration are supplied to the
existing builder. A deterministic wrapper trace records primary/supporting question identities,
objective identity, evidence state, slot, min/max durations, safety constraints, `MustNotClaim`, full
knowledge selection lineage, opportunity identity, and opportunity checksum. Scene IDs hash projection
version, intent ID, variant intent ID, opportunity ID, variant, order, and slot ID.

## 9. Question/objective/knowledge reconciliation

Pre-build checks require exactly one structured Primary coverage record matching each opportunity,
a nonblank objective identity/text, and knowledge selections whose opportunity, primary question,
variant, authority fields, purpose, and evidence status match the opportunity. The complete certified
selection is retained in traceability, preventing loss hidden by the builder's narrower reference type.

## 10. Editorial safety

Editorial-only and Mixed opportunities must retain constraints and `MustNotClaim`; Editorial-only
opportunities cannot contain knowledge selections. No factual prose, narration, prompts, image
instructions, or unsupported claim is generated.

## 11. Duration and transition reconciliation

Each target is checked against its unchanged source min/max. Counts and sums must equal both intent
and profile values. Non-terminal transitions must be nonblank and the terminal transition must be
`Close`. Mapping copies transition intent without translating it into a rendering transition.

## 12. Long/Short independence proof

The variant method accepts a single `DocumentaryVariantIntent`; there is no access to the sibling.
Variant-qualified IDs include distinct variant intent IDs and variant names. Aggregate validation
rejects intersecting scene IDs and identical variant artifact IDs.

## 13. Checksum design

Variant and aggregate checksums recursively sort JSON object and dictionary keys ordinally before SHA-256, with
the object's own checksum replaced by an empty string. Ordered scene arrays remain semantic. IDs use the same stable invariant-culture
hashing inputs. The only builder timestamp is the constant Unix epoch; no clock, GUID, path, elapsed
time, or dictionary enumeration is introduced.

## 14. Profile resolver ownership and DI

The canonical owner is Core `IFamilyCertificationProfileRegistry` / `ConstellationCertificationProfile`.
`CanonicalDocumentaryBlueprintProfileAdapter` first resolves that owner and projects its Orion Gold
planning policy (12 Long, 4 Short); it contains no lookup dictionary. DI registers that projection as
the resolver input. The resolver detects zero, one, and ambiguous matches explicitly, with ambiguity
reported as `DocumentaryBlueprintProfileConfigurationException`. Nothing is wired into pipeline execution.

## 15. Orion Gold results

Production DI now contains the canonical Orion Gold projection with Long expected scene count 12 and
Short expected scene count 4. Execution could not be certified in this container because the `dotnet`
executable is absent; no passing runtime result is fabricated.

## 16. Focused test results

Focused tests could not be compiled or run because the `dotnet` executable is absent. Static review
confirmed no physical artifact writer and no call from `ProductionPipelineExecutionService`.

## 17. Full test results

`dotnet clean`, restore, build, and the complete test suite could not run because the .NET SDK is not
installed. Passed/failed/skipped counts are therefore unavailable; there are no claimed unrelated
pre-existing test failures.

## 18. Deferred Task 5 publication work

Serialization to `04-blueprint/documentary-blueprint.json`, transactional publication, manifest and
validation output, rollback/recovery, and downstream invalidation remain deferred. No such artifacts
are written by this change.

## 19. Final verdict

The projection architecture and canonical Orion Gold registration are implemented, but runtime
certification cannot be declared until the requested focused/full test suites run successfully.

NOT_READY_FOR_PHASE_4_TASK_5
