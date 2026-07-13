using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;

public sealed record FamilyProfileCompatibilityResult(
    bool Succeeded,
    AstronomyFamilyProfile? LegacyProfile,
    FamilyProfileCompatibilityDiagnostics Diagnostics,
    IReadOnlyList<string> BlockingErrors);
