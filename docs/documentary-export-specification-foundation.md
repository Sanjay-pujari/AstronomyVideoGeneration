# O2.15 — Documentary Export Specification Foundation

O2.15 defines the logical versus physical export distinction. It validates a certified O2.14 knowledge package and describes a complete logical export specification; it does not perform an export.

The `CertifiedKnowledgePackage` profile uses `StructuredJson` logical encoding. Its canonical graph contains **10 canonical items** and **23 canonical dependencies**, in fixed order. The architecture derives a deterministic specification identity, deterministic manifest identity, deterministic item identity, and deterministic dependency identity from certified upstream identities and versions. Policy, metadata, request, dependency, item, logical manifest, specification, build-result, and summary contracts are immutable and JSON-reconstructable.

## Explicit boundaries

O2.15 does not create physical export files.

O2.15 does not create directories or archives.

O2.15 does not write structured JSON to disk.

O2.15 does not compress export content.

O2.15 does not upload or publish export content.

O2.15 does not use cloud storage.

O2.15 does not calculate hashes or checksums.

O2.15 does not create certificates or digital signatures.

O2.15 does not encrypt export content.

O2.15 does not invoke an external exporter.

O2.15 does not modify the certification record, provenance record, or production package.

O2.15 does not schedule export workflows.
