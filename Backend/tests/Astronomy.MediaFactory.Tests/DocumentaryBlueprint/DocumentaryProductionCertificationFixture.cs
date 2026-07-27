using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class DocumentaryProductionCertificationFixture
{
    internal static DocumentaryProductionCertificationRequest Request(DocumentaryMediaProject project)
    {
        var execution=DocumentaryMediaPipelineFixture.Complete(project);
        return new(project.MaterializationRecord,project,execution,new DocumentaryProductionCertificationPolicy(),
            new DocumentaryProductionCertificationMetadata(DocumentaryMediaPipelineFixture.Timestamp," O2.19 certifier ",project.Metadata.CorrelationId,execution.ExecutionId), Evidence(execution));
    }
    private static DocumentaryProductionCertificationEvidencePackage Evidence(DocumentaryMediaPipelineExecutionRecord execution)
    {
        var run=$"{execution.ExecutionId}.production-certification.1";var package=$"{run}.evidence-package";
        var references=Enum.GetValues<CertificationEvidenceType>().Select((type,index)=>new CertificationEvidenceReference(type,$"{package}.{type}",$"fixture:{type}",true,1,0,0,10,"fixture-revision",index,execution.Metadata.CorrelationId)).ToArray();
        return new(package,references,references.Length,"1.0",execution.Metadata.CorrelationId);
    }
    internal static DocumentaryProductionCertificationResult Certify(DocumentaryMediaProject project)=>new DocumentaryProductionCertifier().Certify(Request(project));
}
