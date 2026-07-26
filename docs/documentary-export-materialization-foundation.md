# Documentary Export Materialization Foundation (O2.16)

## Architectural position

O2.16 is the deterministic, in-memory projection immediately above the O2.15 logical export specification. It transforms the ten specification items into an immutable payload set for a future delivery layer without performing that delivery.

This in-memory materialization contains exactly 10 canonical payloads and 23 canonical dependencies. It has a deterministic materialization identity, deterministic payload manifest identity, deterministic payload identity, and deterministic dependency identity.

## Materialization contract

The `CanonicalWebJson` profile serializes each source artifact as canonical Web JSON with `new JsonSerializerOptions(JsonSerializerDefaults.Web)`. The character encoding inventory names this representation `Utf8`; each canonical JSON string is encoded with UTF-8, and the payload records both its .NET character count and encoded byte count. Reconstruction uses only the retained source item identity, artifact identity and version, sequence, dependency graph, correlation, profile, content, and bytes.

The canonical mapping, in order, is accepted narrative, final validation evidence, revision history, convergence evidence, acceptance evidence, production package manifest, provenance record, certification decision, certification record, and export manifest. The corresponding logical content classifications follow that same order. A zero-cycle revision history is `[]`; other histories retain their canonical array content.

Payload IDs use `{PayloadType}.{ArtifactIdentity}.{ArtifactVersion}.payload`. Dependency IDs use `{SourcePayloadType}.depends-on.{TargetPayloadType}`. All 23 O2.15 dependencies retain their order, sequence, and exact correlation. The logical payload manifest uses `{MaterializationId}.manifest`, retains all ten payloads, and totals their dependency, character, and byte counts.

Equivalent complete specifications therefore reconstruct identical identities, canonical JSON content, UTF-8 bytes, ordering, dependencies, and totals. The materializer and summarizer neither mutate retained artifacts nor perform external work.

## Explicit exclusions

O2.16 materializes export content only in memory.

O2.16 does not create physical files.

O2.16 does not create directories, streams, or archives.

O2.16 does not compress payloads.

O2.16 does not upload or publish payloads.

O2.16 does not use cloud storage.

O2.16 does not calculate hashes or checksums.

O2.16 does not create certificates or digital signatures.

O2.16 does not encrypt payloads.

O2.16 does not invoke an external exporter.

O2.16 does not modify the export specification or any upstream artifact.

O2.16 does not schedule materialization workflows.
