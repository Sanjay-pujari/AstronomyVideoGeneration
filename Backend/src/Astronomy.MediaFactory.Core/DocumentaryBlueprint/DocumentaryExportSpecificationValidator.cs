namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal static class DocumentaryExportSpecificationValidator
{
    private static bool Eq(string a,string b)=>DocumentaryExportSpecificationInventory.Eq(a,b);
    private static IReadOnlyList<DocumentaryExportSpecificationRejectionReason> Ordered(IEnumerable<DocumentaryExportSpecificationRejectionReason> values)=>
        Array.AsReadOnly(values.Distinct().OrderBy(x=>(int)x).ToArray());

    internal static bool PolicyValid(DocumentaryExportSpecificationPolicy p)=>p is not null&&new[]{p.RequireCertifiedRecord,p.RequireCompleteProductionPackage,p.RequireCompleteProvenance,p.RequireCanonicalItems,p.RequireCanonicalOrdering,p.RequireExactCorrelation,p.RequireDeterministicIdentity,p.RequireDeterministicDependencies}.All(x=>x)&&p.RequiredItemTypes.SequenceEqual(DocumentaryExportSpecificationInventory.Items)&&p.RequiredContentTypes.SequenceEqual(DocumentaryExportSpecificationInventory.Contents)&&p.ExportEncoding==DocumentaryExportEncoding.StructuredJson&&p.PolicySchemaVersion==DocumentaryExportSpecificationInventory.Schema;

    internal static bool ItemsValid(IReadOnlyList<DocumentaryExportSpecificationItem> items,string correlation)
    {
        if(items is null||items.Count!=10)return false;
        return items.Select((item,index)=>(item,index)).All(pair=>
        {
            var x=pair.item;var targets=DocumentaryExportSpecificationInventory.DependencyTargetsFor(x.ItemType);
            return x.ItemType==(DocumentaryExportItemType)pair.index&&x.Sequence==pair.index&&x.Requirement==DocumentaryExportSpecificationInventory.RequirementFor(x.ItemType)&&x.ContentType==DocumentaryExportSpecificationInventory.ContentTypeFor(x.ItemType)&&x.Encoding==DocumentaryExportSpecificationInventory.EncodingFor(x.ItemType)&&Eq(x.ItemId,$"{x.ItemType}.{x.ArtifactIdentity}.{x.ArtifactVersion}")&&Eq(x.CorrelationId,correlation)&&x.Dependencies.Count==targets.Count&&x.Dependencies.Select((d,i)=>d.SourceItemType==x.ItemType&&d.TargetItemType==targets[i]&&d.Sequence==i&&Eq(d.DependencyId,$"{x.ItemType}.depends-on.{targets[i]}")&&Eq(d.CorrelationId,correlation)).All(v=>v);
        })&&items.Select(x=>x.ItemId).Distinct(StringComparer.Ordinal).Count()==10;
    }

    internal static IReadOnlyList<DocumentaryExportSpecificationRejectionReason> ValidateItems(DocumentaryCertificationRecord certificationRecord,string exportSpecificationId,DocumentaryExportSpecificationMetadata metadata,IReadOnlyList<DocumentaryExportSpecificationItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);var expected=DocumentaryExportSpecificationBuilder.CreateCanonicalGraph(certificationRecord,exportSpecificationId,metadata).Items;var r=new List<DocumentaryExportSpecificationRejectionReason>();
        var expectedTypes=expected.Select(x=>x.ItemType).ToArray();var expectedIds=expected.Select(x=>x.ItemId).ToArray();var actualTypes=items.Select(x=>x.ItemType).ToArray();var actualIds=items.Select(x=>x.ItemId).ToArray();
        if(expectedTypes.Except(actualTypes).Any()||expectedIds.Except(actualIds,StringComparer.Ordinal).Any())r.Add(DocumentaryExportSpecificationRejectionReason.RequiredItemMissing);
        if(items.Count!=expected.Count||items.Select(x=>x.ItemType).Distinct().Count()!=items.Count||items.Select(x=>x.ItemId).Distinct(StringComparer.Ordinal).Count()!=items.Count||items.Select(x=>x.Sequence).Distinct().Count()!=items.Count)r.Add(DocumentaryExportSpecificationRejectionReason.ItemInventoryMismatch);
        if(items.Select(x=>x.Sequence).SequenceEqual(Enumerable.Range(0,items.Count))==false||actualTypes.SequenceEqual(expectedTypes)==false)r.Add(DocumentaryExportSpecificationRejectionReason.ItemOrderMismatch);
        var overlap=Math.Min(items.Count,expected.Count);
        for(var i=0;i<overlap;i++)
        {
            var a=items[i];var e=expected[i];
            if(a.ItemType!=e.ItemType||a.Requirement!=e.Requirement||a.ContentType!=e.ContentType||a.Encoding!=e.Encoding||!Eq(a.ArtifactVersion,e.ArtifactVersion)||!Eq(a.CorrelationId,e.CorrelationId))r.Add(DocumentaryExportSpecificationRejectionReason.ItemInventoryMismatch);
            if(!Eq(a.ItemId,e.ItemId)||!Eq(a.ArtifactIdentity,e.ArtifactIdentity)||!Eq(a.ItemId,$"{a.ItemType}.{a.ArtifactIdentity}.{a.ArtifactVersion}"))r.Add(DocumentaryExportSpecificationRejectionReason.ItemIdentityMismatch);
            if(!DependenciesEqual(a.Dependencies,e.Dependencies))r.Add(DocumentaryExportSpecificationRejectionReason.ItemDependencyMismatch);
        }
        if(items.Count>expected.Count||items.Any(x=>!expectedIds.Contains(x.ItemId,StringComparer.Ordinal)||!Enum.IsDefined(x.ItemType)))r.Add(DocumentaryExportSpecificationRejectionReason.UnsupportedItemPresent);
        return Ordered(r);
    }

    private static bool DependenciesEqual(IReadOnlyList<DocumentaryExportItemDependency> a,IReadOnlyList<DocumentaryExportItemDependency> e)=>a.Count==e.Count&&a.Select((x,i)=>{var y=e[i];return Eq(x.DependencyId,y.DependencyId)&&x.SourceItemType==y.SourceItemType&&x.TargetItemType==y.TargetItemType&&x.Sequence==y.Sequence&&Eq(x.CorrelationId,y.CorrelationId);}).All(x=>x);
    private static bool ItemsEqual(IReadOnlyList<DocumentaryExportSpecificationItem> a,IReadOnlyList<DocumentaryExportSpecificationItem> e)=>a.Count==e.Count&&a.Select((x,i)=>{var y=e[i];return Eq(x.ItemId,y.ItemId)&&x.ItemType==y.ItemType&&x.Requirement==y.Requirement&&x.ContentType==y.ContentType&&x.Encoding==y.Encoding&&Eq(x.ArtifactIdentity,y.ArtifactIdentity)&&Eq(x.ArtifactVersion,y.ArtifactVersion)&&x.Sequence==y.Sequence&&Eq(x.CorrelationId,y.CorrelationId)&&DependenciesEqual(x.Dependencies,y.Dependencies);}).All(x=>x);

    internal static void ValidateCertificationRecord(DocumentaryCertificationRecord c)
    {
        ArgumentNullException.ThrowIfNull(c);if(!c.IsCertified||!c.Decision.IsCertified||c.FailedRuleCount!=0||c.PassedRuleCount!=22||c.TotalRuleCount!=22||c.Decision.Findings.Count!=0||c.Decision.RuleResults.Count!=22||c.Decision.RuleResults.Any(x=>!x.Passed)||c.CertificationId!=$"{c.ProvenanceId}.certification")throw new ArgumentException("Certification record is invalid.");
        DocumentaryCertificationValidator.ValidatePolicy(c.Policy);DocumentaryCertificationValidator.ValidateEvidence(c.Metadata,c.UpstreamCertificationEvidence,c.DocumentationEvidence);DocumentaryProvenanceValidator.ValidateRecord(c.ProvenanceRecord);
        if(!ReferenceEquals(c.ProductionPackage,c.ProvenanceRecord.ProductionPackage)&&!DocumentaryProductionPackageValidator.PackagesAreEquivalent(c.ProductionPackage,c.ProvenanceRecord.ProductionPackage))throw new ArgumentException("Certification package differs.");
    }

    internal static bool SpecificationValid(DocumentaryExportSpecification s)
    { try{ValidateSpecificationCore(s,false);return true;}catch(ArgumentException){return false;} }
    internal static void ValidateSpecification(DocumentaryExportSpecification s)=>ValidateSpecificationCore(s,true);
    private static void ValidateSpecificationCore(DocumentaryExportSpecification s,bool requireComplete)
    {
        ArgumentNullException.ThrowIfNull(s);var c=s.CertificationRecord;var p=s.ProductionPackage;var provenance=s.ProvenanceRecord;var correlation=s.Metadata.CorrelationId;ValidateCertificationRecord(c);DocumentaryProvenanceValidator.ValidateRecord(provenance);
        if(requireComplete&&!s.IsComplete||!PolicyValid(s.Policy)||s.Profile!=DocumentaryExportProfile.CertifiedKnowledgePackage||!Eq(s.ExportSpecificationId,$"{c.CertificationId}.export-specification")||!Eq(s.CertificationId,c.CertificationId)||!Eq(s.ProvenanceId,provenance.ProvenanceId)||!Eq(s.PackageId,p.PackageId)||!Eq(s.ReleaseCandidateId,p.ReleaseCandidateId)||!Eq(s.ConvergenceId,p.ConvergenceId)||!ReferenceEquals(p,c.ProductionPackage)&&!DocumentaryProductionPackageValidator.PackagesAreEquivalent(p,c.ProductionPackage)||!ReferenceEquals(p,provenance.ProductionPackage)&&!DocumentaryProductionPackageValidator.PackagesAreEquivalent(p,provenance.ProductionPackage))throw new ArgumentException("Specification linkage is invalid.");
        if(!Eq(c.Metadata.CorrelationId,correlation)||!Eq(p.Metadata.CorrelationId,correlation)||!Eq(provenance.Metadata.CorrelationId,correlation)||ValidateItems(c,s.ExportSpecificationId,s.Metadata,s.Items).Count!=0||s.ItemCount!=10||s.RequiredItemCount!=10)throw new ArgumentException("Specification graph is invalid.");
        var m=s.Manifest;if(!Eq(m.ManifestId,$"{s.ExportSpecificationId}.manifest")||!Eq(m.ExportSpecificationId,s.ExportSpecificationId)||m.Profile!=s.Profile||m.ItemCount!=s.ItemCount||m.RequiredItemCount!=s.RequiredItemCount||m.Encoding!=DocumentaryExportEncoding.StructuredJson||!Eq(m.ManifestSchemaVersion,DocumentaryExportSpecificationInventory.Schema)||!Eq(m.CorrelationId,correlation)||!ItemsEqual(m.Items,s.Items))throw new ArgumentException("Manifest is invalid.");
    }
}
