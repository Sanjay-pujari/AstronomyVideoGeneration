# Phase 7 DocumentaryBlueprint alignment report

## Authority chain

Phase 7 now physically reads and validates the committed authority chain before it invokes
`NarrationGeneratorV5`:

1. `04-blueprint/documentary-blueprint.json` (`DocumentaryBlueprintAggregate`)
2. `04-blueprint/documentary-blueprint.long.json` and
   `04-blueprint/documentary-blueprint.short.json` (`DocumentaryBlueprintVariantArtifact`)
3. Phase 4 validation, Phase 5 `blueprint-certification.json` and validation
4. `06-story-frames/story-frames.json` (`StoryFramesAuthority`) and Phase 6 validation
5. Phase 7 knowledge enrichment and natural narration working artifacts
6. `07-narration/{long|short}/accepted-release-candidate.json`

The implementation reuses `DocumentaryBlueprintAggregate`,
`DocumentaryBlueprintVariantArtifact`, `DocumentaryBlueprint`,
`DocumentarySceneBlueprint`, `BlueprintPublicationFormat`, `DocumentarySceneRole`,
`SceneObjective`, `EditorialOutcome`, `SceneTransition`, `KnowledgeReference`, and
`StoryFramesAuthority`. It introduces no blueprint builder, projector, or replacement
structural model.

## Physical identities and checksums

IDs and checksums are execution-specific and are read rather than hardcoded. The accepted
candidate records the exact published aggregate ID/checksum, Long or Short blueprint
ID/checksum, and Story Frames authority ID/checksum. The lifecycle rejects a Story Frames
`SourcePhase4Checksum` that differs from the aggregate checksum and rejects a physical
variant whose ID/checksum differs from the embedded aggregate variant.

The canonical Long path is `04-blueprint/documentary-blueprint.long.json`; the canonical
Short path is `04-blueprint/documentary-blueprint.short.json`. Long and Short scene mapping
counts are derived from their respective blueprint scene arrays. No global 12/4 constant is
used.

## Scene projection and enrichment

Each composition scene is joined by governed scene ID to its own variant blueprint and its
ordered Story Frames. Performer context contains the scene title, stage, role, viewer-question
text, objective and learning goal, editorial takeaway/contribution/priority, transition intent,
duration, and knowledge-reference boundary. Visual opportunities and visual intent remain
separate non-speakable context.

Blueprint `KnowledgeReference.KnowledgeEntryId` values are placed before matching Story Frame
reference IDs in the scene-level selection boundary. Working semantic resolvers may enrich
that boundary with resolved claims; they may not retrieve a replacement scene structure.
Generic production placeholders are replaced in performer purpose text by the blueprint
objective, viewer goal, takeaway, and semantic transition intent. Leakage validation blocks
internal IDs and transition tokens such as `Advance01` from accepted narration.

## Release lineage and downstream authority

Each candidate includes aggregate, variant-blueprint, and Story Frames IDs/checksums, governed
and accepted scene counts, ordered blueprint scene IDs, and a deterministic checksum. Each
scene includes blueprint scene ID, Story Frame ID, Phase 7 knowledge authority ID (when
published), selected knowledge references and claims, source narration artifact, language,
variant, and viewer-facing narration. Lineage is metadata and is never inserted into
`narrationText`.

Long and Short requests are independently projected and generated. Cross-variant validation
rejects identical, contiguous-copy, and near-verbatim reuse. Working `narration-v5` files are
input/evidence only; production readiness and downstream phase gating resolve the accepted
release candidates plus `narration-certification.json`.

## Behavioral coverage

The Documentary Narrative Lifecycle test suite covers independent Long/Short mapping,
natural-prose validation, duplicate and leakage blocking, generic internal-token rejection,
factual substance, bounded repair, missing-authority failure, and generator diagnostics.
The alignment adds exact sequence validation, physical aggregate/variant checksum validation,
variant ownership validation, and Story Frame-to-blueprint sequence validation. Publication is
blocked for a missing, reordered, wrong-variant, ungoverned, or checksum-mismatched scene.
