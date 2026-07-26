# Documentary Export Specification Foundation (O2.15)

O2.15 sits above the O2.12 production package, O2.13 provenance record, and O2.14 certification record. It describes a logical `CertifiedKnowledgePackage`; physical materialization is a future objective.

Schema 1.0 uses `StructuredJson` and the canonical order AcceptedNarrative, FinalValidationEvidence, RevisionHistory, ConvergenceEvidence, AcceptanceEvidence, ProductionPackageManifest, ProvenanceRecord, CertificationDecision, CertificationRecord, and ExportManifest. Their corresponding content classifications follow the same order. Every item is required.

The specification identity is `{CertificationId}.export-specification`, its logical manifest identity is `{ExportSpecificationId}.manifest`, item identities are `{ItemType}.{ArtifactIdentity}.{ArtifactVersion}`, and dependency identities are `{SourceItemType}.depends-on.{TargetItemType}`. The manifest contains the ten ordered items. Their dependency counts are 0, 1, 1, 2, 1, 5, 1, 1, 2, and 9, for a canonical total of 23; each dependency list follows target-item order.

The builder returns either Complete with an immutable specification or Rejected with canonically ordered reasons. Exact ordinal correlation spans the retained package, revision history, provenance graph, certification evidence, export metadata, items, dependencies, and manifest. Caller-supplied timestamps and text are preserved. Value-derived identities, inventories, and ordering permit deterministic reconstruction without mutation or external work.

Canonical graph validation compares every export-item and dependency field, not identity strings alone.

Item rejection categories distinguish missing items, inventory defects, ordering defects, identity defects, dependency defects, and unsupported additions.

Certification, provenance, and production-package linkage require deterministic value equivalence where references differ.

The summarizer validates the complete logical export specification before deriving a summary.

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
