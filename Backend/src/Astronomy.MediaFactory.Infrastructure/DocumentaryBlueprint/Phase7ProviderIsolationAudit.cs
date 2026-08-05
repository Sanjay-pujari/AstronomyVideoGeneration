using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7ProviderIsolationAudit : IPhase7ProviderIsolationAudit
{
    public Phase7ProviderIsolationSnapshot CaptureStart() => new(DateTimeOffset.UtcNow);

    public Phase7ProviderIsolationEvidence Complete(Phase7ProviderIsolationSnapshot start) =>
        new(RuntimeCountersAvailable: false, ProviderDependenciesInjected: false, ProviderInvocationDetected: false,
            AzureOpenAiCalls: 0, PromptComposerCalls: 0, NarrationGeneratorCalls: 0, TranslationCalls: 0,
            AzureSpeechCalls: 0, TtsCalls: 0, RenderingCalls: 0);
}
