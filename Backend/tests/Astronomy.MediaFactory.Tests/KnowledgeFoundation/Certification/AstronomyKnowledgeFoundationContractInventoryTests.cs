using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class AstronomyKnowledgeFoundationContractInventoryTests
{
    [Fact]
    public void Required_public_contracts_exist_in_expected_namespaces()
    {
        var expected = new (Type Type, string Namespace)[]
        {
            (typeof(KnowledgeId), "Astronomy.MediaFactory.Core.KnowledgeFoundation"), (typeof(KnowledgeVersion), "Astronomy.MediaFactory.Core.KnowledgeFoundation"),
            (typeof(KnowledgeStatementKind), "Astronomy.MediaFactory.Core.KnowledgeFoundation"), (typeof(KnowledgeFoundationStatus), "Astronomy.MediaFactory.Core.KnowledgeFoundation"),
            (typeof(AstronomyEntityReference), "Astronomy.MediaFactory.Core.KnowledgeFoundation"), (typeof(KnowledgeAuditMetadata), "Astronomy.MediaFactory.Core.KnowledgeFoundation"),
            (typeof(ITypedAstronomyKnowledgePayload), "Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains"), (typeof(AstronomyKnowledgeDomain), "Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains"),
            (typeof(AstronomyKnowledgePayloadFamily), "Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains"), (typeof(AstronomyKnowledgeTypeId), "Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains"),
            (typeof(AstronomyTypedPayloadDescriptor), "Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration"), (typeof(IAstronomyTypedPayloadRegistry), "Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration"),
            (typeof(AstronomyKnowledgeValidationIssue), "Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation"), (typeof(IAstronomyKnowledgeValidationRuleRegistry), "Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation"),
            (typeof(IAstronomyCrossDomainValidator), "Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain"), (typeof(IAstronomyKnowledgeGraphValidator), "Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph"),
            (typeof(IAstronomyKnowledgeCatalog), "Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog"), (typeof(AstronomyKnowledgeCatalogQuery), "Astronomy.MediaFactory.Core.KnowledgeFoundation.Query"),
            (typeof(IAstronomyKnowledgeCatalogQueryEngine), "Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution"), (typeof(IAstronomyKnowledgeFoundationCapabilities), "Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration")
        };
        foreach (var item in expected)
        {
            Assert.True(item.Type.IsPublic || item.Type.IsNestedPublic, item.Type.FullName);
            Assert.Equal(item.Namespace, item.Type.Namespace);
        }
        Assert.True(typeof(KnowledgeId).IsValueType);
        Assert.True(typeof(KnowledgeVersion).IsValueType);
    }
}
