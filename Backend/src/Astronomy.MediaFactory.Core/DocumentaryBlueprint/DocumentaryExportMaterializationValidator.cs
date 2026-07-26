using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal sealed class DocumentaryExportPayloadGraphSpecification
{
    internal DocumentaryExportPayloadGraphSpecification(IReadOnlyList<DocumentaryExportPayload> payloads) =>
        Payloads=DocumentaryExportMaterializationInventory.Copy(payloads,nameof(payloads));
    internal IReadOnlyList<DocumentaryExportPayload> Payloads { get; }
}

internal static class DocumentaryExportMaterializationValidator
{
    internal static readonly JsonSerializerOptions WebJsonOptions=new(JsonSerializerDefaults.Web);
    private static bool Eq(string? a,string? b)=>string.Equals(a,b,StringComparison.Ordinal);
    internal static bool PolicyValid(DocumentaryExportMaterializationPolicy p)=>p is not null&&new[]{p.RequireCompleteExportSpecification,p.RequireCanonicalPayloads,p.RequireCanonicalOrdering,p.RequireExactCorrelation,p.RequireDeterministicSerialization,p.RequireDeterministicIdentity,p.RequireDependencyPreservation,p.RequireUtf8Encoding}.All(x=>x)&&p.RequiredPayloadTypes.SequenceEqual(DocumentaryExportMaterializationInventory.PayloadTypes)&&p.RequiredPayloadContentTypes.SequenceEqual(DocumentaryExportMaterializationInventory.ContentTypes)&&p.SerializerProfile==DocumentaryExportSerializerProfile.CanonicalWebJson&&p.CharacterEncoding==DocumentaryExportCharacterEncoding.Utf8&&p.PolicySchemaVersion=="1.0";

    internal static IReadOnlyList<DocumentaryExportMaterializationRejectionReason> ValidatePayloads(DocumentaryExportSpecification specification,DocumentaryExportMaterializationMetadata metadata,DocumentaryExportSerializerProfile serializerProfile,IReadOnlyList<DocumentaryExportPayload> payloads)
    {
        ArgumentNullException.ThrowIfNull(specification);ArgumentNullException.ThrowIfNull(metadata);ArgumentNullException.ThrowIfNull(payloads);
        var expected=DocumentaryExportMaterializer.CreateCanonicalPayloadGraph(specification,metadata,serializerProfile).Payloads;
        var reasons=new HashSet<DocumentaryExportMaterializationRejectionReason>();
        var expectedIds=expected.Select(x=>x.PayloadId).ToHashSet(StringComparer.Ordinal);
        if(expected.Any(e=>!payloads.Any(a=>a.PayloadType==e.PayloadType&&Eq(a.PayloadId,e.PayloadId))))reasons.Add(DocumentaryExportMaterializationRejectionReason.RequiredPayloadMissing);
        if(payloads.Count!=expected.Count||payloads.Select(x=>x.PayloadType).Distinct().Count()!=payloads.Count||payloads.Select(x=>x.PayloadId).Distinct(StringComparer.Ordinal).Count()!=payloads.Count||payloads.Select(x=>x.Sequence).Distinct().Count()!=payloads.Count)reasons.Add(DocumentaryExportMaterializationRejectionReason.PayloadInventoryMismatch);
        if(payloads.Count!=expected.Count||payloads.Select((x,i)=>i<expected.Count&&x.PayloadType==expected[i].PayloadType&&x.Sequence==i).Any(x=>!x))reasons.Add(DocumentaryExportMaterializationRejectionReason.PayloadOrderMismatch);
        if(payloads.Count>expected.Count||payloads.Any(x=>!Enum.IsDefined(x.PayloadType)||!expectedIds.Contains(x.PayloadId))||payloads.GroupBy(x=>x.PayloadId,StringComparer.Ordinal).Any(g=>g.Count()>1))reasons.Add(DocumentaryExportMaterializationRejectionReason.UnsupportedPayloadPresent);
        foreach(var a in payloads)
        {
            var e=expected.FirstOrDefault(x=>x.PayloadType==a.PayloadType);if(e is null)continue;
            if(!Eq(a.PayloadId,e.PayloadId)||!Eq(a.ArtifactIdentity,e.ArtifactIdentity)||!Eq(a.PayloadId,$"{a.PayloadType}.{a.ArtifactIdentity}.{a.ArtifactVersion}.payload"))reasons.Add(DocumentaryExportMaterializationRejectionReason.PayloadIdentityMismatch);
            if(a.ContentType!=e.ContentType||a.SerializerProfile!=e.SerializerProfile||a.CharacterEncoding!=e.CharacterEncoding||!Eq(a.SourceItemId,e.SourceItemId)||!Eq(a.ArtifactVersion,e.ArtifactVersion)||!Eq(a.CorrelationId,e.CorrelationId)||a.CharacterCount!=e.CharacterCount||a.ByteCount!=e.ByteCount)reasons.Add(DocumentaryExportMaterializationRejectionReason.PayloadInventoryMismatch);
            if(!Eq(a.Content,e.Content)||!a.Utf8Bytes.SequenceEqual(e.Utf8Bytes)||a.CharacterCount!=e.CharacterCount||a.ByteCount!=e.ByteCount)reasons.Add(DocumentaryExportMaterializationRejectionReason.PayloadContentMismatch);
            if(!DependenciesEqual(a.Dependencies,e.Dependencies))reasons.Add(DocumentaryExportMaterializationRejectionReason.PayloadDependencyMismatch);
        }
        return reasons.OrderBy(x=>(int)x).ToArray();
    }

    internal static bool PayloadsStructurallyValid(IReadOnlyList<DocumentaryExportPayload> payloads,string correlation)=>payloads is not null&&payloads.Count==10&&payloads.Select((p,i)=>(p,i)).All(x=>x.p.PayloadType==(DocumentaryExportPayloadType)x.i&&x.p.ContentType==DocumentaryExportMaterializationInventory.PayloadContentTypeFor(x.p.PayloadType)&&x.p.Sequence==x.i&&Eq(x.p.CorrelationId,correlation)&&x.p.Dependencies.Select(d=>d.TargetPayloadType).SequenceEqual(DocumentaryExportMaterializationInventory.DependencyTargetsFor(x.p.PayloadType)))&&payloads.Sum(x=>x.Dependencies.Count)==23;
    private static bool DependenciesEqual(IReadOnlyList<DocumentaryExportPayloadDependency> a,IReadOnlyList<DocumentaryExportPayloadDependency> b)=>a.Count==b.Count&&a.Select((x,i)=>{var y=b[i];return Eq(x.DependencyId,y.DependencyId)&&x.SourcePayloadType==y.SourcePayloadType&&x.TargetPayloadType==y.TargetPayloadType&&x.Sequence==y.Sequence&&Eq(x.CorrelationId,y.CorrelationId);}).All(x=>x);
    private static bool PayloadsEqual(IReadOnlyList<DocumentaryExportPayload> a,IReadOnlyList<DocumentaryExportPayload> b)=>a.Count==b.Count&&a.Select((x,i)=>JsonSerializer.Serialize(x,WebJsonOptions)==JsonSerializer.Serialize(b[i],WebJsonOptions)).All(x=>x);
    private static bool ValueEqual<T>(T a,T b)=>ReferenceEquals(a,b)||JsonSerializer.Serialize(a,WebJsonOptions)==JsonSerializer.Serialize(b,WebJsonOptions);

    internal static bool RecordValid(DocumentaryExportMaterializationRecord r)
    {
        try{DocumentaryExportSpecificationValidator.ValidateSpecification(r.ExportSpecification);}catch(ArgumentException){return false;}
        var s=r.ExportSpecification;var expected=DocumentaryExportMaterializer.CreateCanonicalPayloadGraph(s,r.Metadata,r.SerializerProfile).Payloads;var m=r.Manifest;
        return Eq(r.MaterializationId,$"{s.ExportSpecificationId}.materialization")&&Eq(r.ExportSpecificationId,s.ExportSpecificationId)&&Eq(r.CertificationId,s.CertificationId)&&Eq(r.ProvenanceId,s.ProvenanceId)&&Eq(r.PackageId,s.PackageId)&&Eq(r.ReleaseCandidateId,s.ReleaseCandidateId)&&Eq(r.ConvergenceId,s.ConvergenceId)
            &&ValueEqual(r.CertificationRecord,s.CertificationRecord)&&ValueEqual(r.ProvenanceRecord,s.ProvenanceRecord)&&(ReferenceEquals(r.ProductionPackage,s.ProductionPackage)||DocumentaryProductionPackageValidator.PackagesAreEquivalent(r.ProductionPackage,s.ProductionPackage))
            &&PolicyValid(r.Policy)&&r.SerializerProfile==DocumentaryExportSerializerProfile.CanonicalWebJson&&r.CharacterEncoding==DocumentaryExportCharacterEncoding.Utf8&&ValidatePayloads(s,r.Metadata,r.SerializerProfile,r.Payloads).Count==0&&PayloadsEqual(r.Payloads,expected)
            &&r.PayloadCount==10&&r.DependencyCount==23&&r.TotalCharacterCount==r.Payloads.Sum(x=>x.CharacterCount)&&r.TotalByteCount==r.Payloads.Sum(x=>x.ByteCount)
            &&Eq(m.ManifestId,$"{r.MaterializationId}.manifest")&&Eq(m.MaterializationId,r.MaterializationId)&&Eq(m.ExportSpecificationId,s.ExportSpecificationId)&&m.SerializerProfile==r.SerializerProfile&&m.CharacterEncoding==r.CharacterEncoding&&PayloadsEqual(m.Payloads,r.Payloads)&&m.PayloadCount==r.PayloadCount&&m.DependencyCount==r.DependencyCount&&m.TotalCharacterCount==r.TotalCharacterCount&&m.TotalByteCount==r.TotalByteCount&&Eq(m.ManifestSchemaVersion,"1.0")&&Eq(m.CorrelationId,r.Metadata.CorrelationId);
    }
}
