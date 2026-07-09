namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Diagnostics;

/// <summary>Diagnostics emitted when the Documentary Style Director builds the style contract.</summary>
public sealed record DocumentaryStyleDiagnostics(int SceneCount, int TransitionCount, int VocabularyTransformations, int FactTransformations, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, string ExecutionTime, string Version);
