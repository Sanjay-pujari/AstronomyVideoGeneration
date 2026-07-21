# CG-A2 Knowledge Foundation

Purpose: documents the frozen Task 2 Knowledge Foundation: immutable knowledge statements, typed payload registration, validation, catalog metadata, typed query model, in-memory execution, root registration, capabilities, compatibility verification, and limitations.

## Quick start registration
```csharp
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAstronomyKnowledgeFoundation();
```

## Minimal catalog lookup
```csharp
var provider = services.BuildServiceProvider();
var catalog = provider.GetRequiredService<IAstronomyKnowledgeCatalog>();
var id = new AstronomyKnowledgeCatalogEntryId(AstronomyKnowledgeCatalogEntryKind.KnowledgeType, "typed.physical.properties.v1");
var found = catalog.Snapshot.TryGet(id, out var entry);
```

## Minimal catalog query
```csharp
var engine = provider.GetRequiredService<IAstronomyKnowledgeCatalogQueryEngine>();
var query = new AstronomyKnowledgeCatalogQuery(knowledgeTypes: new AstronomyKnowledgeTypeFilter(new[] { new AstronomyKnowledgeTypeId("typed.physical.properties.v1") }));
var result = engine.Execute(query);
```

## Minimal statement query
```csharp
var statementEngine = provider.GetRequiredService<IAstronomyKnowledgeStatementQueryEngine>();
var result = statementEngine.Execute(new AstronomyKnowledgeStatementQuery(), statements);
```

Validation entry points are `IAstronomyTypedKnowledgeValidator`, `IAstronomyCrossDomainValidator`, and `IAstronomyKnowledgeGraphValidator`. Extension must add explicit descriptors/rules and update tests and documentation. The foundation is frozen through Task 2.5D; Task 2.6 adds documentation and certification only.

## Navigation
- [Overview](KnowledgeFoundationOverview.md)
- [Layers](KnowledgeFoundationLayers.md)
- [Contracts](KnowledgeContracts.md)
- [Typed Domains](TypedKnowledgeDomains.md)
- [Validation](ValidationArchitecture.md)
- [Cross Domain](CrossDomainValidation.md)
- [Graph Validation](KnowledgeGraphValidation.md)
- [Catalog](KnowledgeCatalog.md)
- [Query Model](KnowledgeQueryModel.md)
- [Query Execution](KnowledgeQueryExecution.md)
- [Registration and Capabilities](RegistrationAndCapabilities.md)
- [Extension Guide](ExtensionGuide.md)
- [Testing and Certification](TestingAndCertification.md)
- [Frozen Contracts](FrozenContracts.md)
- [Known Limitations](KnownLimitations.md)
- [Task 2 Completion Report](Task2CompletionReport.md)
