using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class DocumentaryProductionCertificationFixture
{
    internal static DocumentaryProductionCertificationRequest Request(DocumentaryMediaProject project)
    {
        var execution=DocumentaryMediaPipelineFixture.Complete(project);
        return new(project.MaterializationRecord,project,execution,new DocumentaryProductionCertificationPolicy(),
            new DocumentaryProductionCertificationMetadata(DocumentaryMediaPipelineFixture.Timestamp," O2.19 certifier ",project.Metadata.CorrelationId,execution.ExecutionId));
    }
    internal static DocumentaryProductionCertificationResult Certify(DocumentaryMediaProject project)=>new DocumentaryProductionCertifier().Certify(Request(project));
}
