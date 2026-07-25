using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryBlueprintValidationSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Finding_json_round_trip_preserves_all_values()
    {
        var finding = new DocumentaryBlueprintValidationFinding(
            "DBP-EDITORIAL-002", DocumentaryBlueprintValidationSeverity.Error, "Scene number must be positive.",
            "blueprint", "scene-1", 1, "SceneNumber");

        var originalJson = JsonSerializer.Serialize(finding, JsonOptions);
        var reconstructed = JsonSerializer.Deserialize<DocumentaryBlueprintValidationFinding>(originalJson, JsonOptions)!;

        Assert.Equal(finding.RuleCode, reconstructed.RuleCode);
        Assert.Equal(finding.Severity, reconstructed.Severity);
        Assert.Equal(finding.Message, reconstructed.Message);
        Assert.Equal(finding.BlueprintId, reconstructed.BlueprintId);
        Assert.Equal(finding.SceneId, reconstructed.SceneId);
        Assert.Equal(finding.SceneNumber, reconstructed.SceneNumber);
        Assert.Equal(finding.FieldName, reconstructed.FieldName);
        Assert.Equal(originalJson, JsonSerializer.Serialize(reconstructed, JsonOptions));
    }

    [Fact]
    public void Result_json_round_trip_preserves_order_scope_and_derived_values()
    {
        var findings = new[]
        {
            new DocumentaryBlueprintValidationFinding(
                "DBP-EDITORIAL-001", DocumentaryBlueprintValidationSeverity.Error,
                "Blueprint must contain at least one scene.", "blueprint"),
            new DocumentaryBlueprintValidationFinding(
                "DBP-EDITORIAL-016", DocumentaryBlueprintValidationSeverity.Warning,
                "Zero-duration scene should be reviewed.", "blueprint", "scene-2", 2,
                "EstimatedDurationSeconds")
        };
        var result = new DocumentaryBlueprintValidationResult("blueprint", findings);

        var originalJson = JsonSerializer.Serialize(result, JsonOptions);
        var reconstructed = JsonSerializer.Deserialize<DocumentaryBlueprintValidationResult>(originalJson, JsonOptions)!;

        Assert.Equal(result.BlueprintId, reconstructed.BlueprintId);
        Assert.Equal(2, reconstructed.Findings.Count);
        Assert.Equal(findings.Select(x => x.RuleCode), reconstructed.Findings.Select(x => x.RuleCode));
        Assert.Equal(findings.Select(x => x.Severity), reconstructed.Findings.Select(x => x.Severity));
        Assert.Null(reconstructed.Findings[0].SceneId);
        Assert.Null(reconstructed.Findings[0].SceneNumber);
        Assert.Null(reconstructed.Findings[0].FieldName);
        Assert.Equal("scene-2", reconstructed.Findings[1].SceneId);
        Assert.Equal(2, reconstructed.Findings[1].SceneNumber);
        Assert.Equal("EstimatedDurationSeconds", reconstructed.Findings[1].FieldName);
        Assert.Equal(1, reconstructed.ErrorCount);
        Assert.Equal(1, reconstructed.WarningCount);
        Assert.False(reconstructed.IsValid);
        Assert.Equal(originalJson, JsonSerializer.Serialize(reconstructed, JsonOptions));
    }
}
