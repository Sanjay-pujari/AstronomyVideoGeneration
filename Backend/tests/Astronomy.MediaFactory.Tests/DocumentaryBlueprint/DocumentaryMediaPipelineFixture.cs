using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class DocumentaryMediaPipelineFixture
{
    internal static readonly DateTimeOffset Timestamp = new DateTimeOffset(2025, 2, 17, 18, 19, 20, 123, TimeSpan.FromHours(5.5)).AddTicks(4567);
    internal const string Creator = " pipeline fixture creator ";
    internal static DocumentaryMediaProject Orion() => DocumentaryMediaProjectionFixture.Complete(DocumentaryMediaProjectionFixture.Orion());
    internal static DocumentaryMediaProject Leo() => DocumentaryMediaProjectionFixture.Complete(DocumentaryMediaProjectionFixture.Leo());
    internal static DocumentaryMediaProject Conjunction() => DocumentaryMediaProjectionFixture.Complete(DocumentaryMediaProjectionFixture.Conjunction());
    internal static DocumentaryMediaPipelinePolicy PlanOnly() => new(DocumentaryMediaPipelineMode.PlanOnly, true, 2, 2, 2);
    internal static DocumentaryMediaPipelinePolicy Execute() => new(DocumentaryMediaPipelineMode.Execute, true, 2, 2, 2);
    internal static DocumentaryMediaPipelinePolicy ExecuteWithoutPartialCompletion() => new(DocumentaryMediaPipelineMode.Execute, false, 2, 2, 2);
    internal static DocumentaryMediaPipelineRequest Request(DocumentaryMediaProject project, DocumentaryMediaPipelinePolicy? policy = null) =>
        new(project, policy ?? Execute(), new DocumentaryMediaPipelineMetadata(Timestamp, Creator, project.Metadata.CorrelationId, $"{project.MediaProjectId}.execution.1"));
    internal static DocumentaryMediaPipelineExecutionPlan Plan(DocumentaryMediaProject project, DocumentaryMediaPipelinePolicy? policy = null) =>
        new DocumentaryMediaPipelinePlanner().Plan(Request(project, policy));
    internal static DocumentaryMediaPipelineResult Run(DocumentaryMediaProject project, DocumentaryMediaPipelinePolicy? policy = null,
        DocumentaryMediaPipelineFakeProviders? providers = null) =>
        new DocumentaryMediaPipelineOrchestrator((providers ?? new DocumentaryMediaPipelineFakeProviders()).Registry).Execute(Request(project, policy));
    internal static DocumentaryMediaPipelineExecutionRecord Complete(DocumentaryMediaProject project) => Run(project).ExecutionRecord!;
    internal static DocumentaryMediaPipelineSummary Summary(DocumentaryMediaPipelineExecutionRecord record) =>
        new DocumentaryMediaPipelineSummarizer().Summarize(record);
    internal static DocumentaryMediaPipelineRequest EquivalentRequest(DocumentaryMediaProject project, DocumentaryMediaPipelinePolicy? policy = null) =>
        new(project, policy ?? new DocumentaryMediaPipelinePolicy(DocumentaryMediaPipelineMode.Execute, true, 2, 2, 2),
            new DocumentaryMediaPipelineMetadata(new DateTimeOffset(2025, 2, 17, 18, 19, 20, 123, TimeSpan.FromHours(5.5)).AddTicks(4567),
                new string(Creator.ToCharArray()), new string(project.Metadata.CorrelationId.ToCharArray()), $"{project.MediaProjectId}.execution.1"));
}
