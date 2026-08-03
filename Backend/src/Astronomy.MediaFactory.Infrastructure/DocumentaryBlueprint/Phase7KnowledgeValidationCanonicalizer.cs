using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>
/// Defines the stable semantic projection used to persist and compare Phase 7
/// knowledge validation.  Validation must not depend on the order in which
/// independent validators or filesystem inventory walkers happened to run.
/// </summary>
public static class Phase7KnowledgeValidationCanonicalizer
{
    public static Phase7KnowledgeValidation Canonicalize(Phase7KnowledgeValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        var inventory = Canonicalize(validation.ArtifactInventory);
        return validation with
        {
            Gates = validation.Gates
                .Select(gate => gate with
                {
                    Errors = Sort(gate.Errors),
                    Warnings = Sort(gate.Warnings)
                })
                .OrderBy(gate => gate.Name, StringComparer.Ordinal)
                .ToArray(),
            Errors = Sort(validation.Errors),
            Warnings = Sort(validation.Warnings),
            ArtifactInventory = inventory
        };
    }

    public static string ComputeChecksum(Phase7KnowledgeValidation validation) =>
        Phase7Determinism.Hash(Canonicalize(validation) with { DeterministicChecksum = "" });

    public static bool Equivalent(Phase7KnowledgeValidation left, Phase7KnowledgeValidation right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Phase7Determinism.Hash(Canonicalize(left) with { DeterministicChecksum = "" }) ==
               Phase7Determinism.Hash(Canonicalize(right) with { DeterministicChecksum = "" });
    }

    private static Phase7KnowledgeArtifactInventory? Canonicalize(Phase7KnowledgeArtifactInventory? inventory)
    {
        if (inventory is null) return null;
        var canonical = inventory with
        {
            Artifacts = inventory.Artifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            DeterministicChecksum = ""
        };
        return canonical with { DeterministicChecksum = Phase7Determinism.Hash(canonical) };
    }

    private static string[] Sort(IEnumerable<string> values) =>
        values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
}
