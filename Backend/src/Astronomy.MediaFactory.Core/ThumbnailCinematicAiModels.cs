namespace Astronomy.MediaFactory.Core;

public sealed class CinematicThumbnailAiRequest
{
    public required ThumbnailGenerationRequest GenerationRequest { get; init; }
    public required ThumbnailCandidateScore SelectedCandidate { get; init; }
    public required string HookText { get; init; }
    public required int TargetWidth { get; init; }
    public required int TargetHeight { get; init; }
}

public sealed class CinematicThumbnailAiRecommendation
{
    public string DominantObject { get; init; } = "unknown";
    public string DominantObjectType { get; init; } = "unknown";
    public string MoodProfile { get; init; } = "dramatic";
    public string CropStrategy { get; init; } = "center-crop";
    public double FocusX { get; init; } = 0.5;
    public double FocusY { get; init; } = 0.5;
    public double ScaleBoost { get; init; } = 1;
    public bool EnhancementApplied { get; init; }
    public bool PortraitSafe { get; init; }
    public double AnalyticsInfluence { get; init; }
    public string Rationale { get; init; } = "Rule-based safe cinematic composition using rendered astronomy frames only.";
}

public sealed class ThumbnailMoodGradingRequest
{
    public required string DominantObjectType { get; init; }
    public string? EventType { get; init; }
    public bool IsShortForm { get; init; }
    public IReadOnlyCollection<string> AllowedMoodProfiles { get; init; } = [];
}

public sealed class ThumbnailMoodGradingResult
{
    public string MoodProfile { get; init; } = "dramatic";
    public double Contrast { get; init; } = 1.08;
    public double Saturation { get; init; } = 1.06;
    public double Brightness { get; init; } = 1.0;
    public string HighlightColor { get; init; } = "warm";
}

public sealed class ThumbnailVisualHierarchyRequest
{
    public required ThumbnailGenerationRequest GenerationRequest { get; init; }
    public required ThumbnailCandidateScore SelectedCandidate { get; init; }
    public required string HookText { get; init; }
    public required string DominantObjectType { get; init; }
    public bool PortraitSafe { get; init; }
}

public sealed class ThumbnailVisualHierarchyResult
{
    public double VisualHierarchyScore { get; init; }
    public double ReadabilityScore { get; init; }
    public IReadOnlyCollection<string> Recommendations { get; init; } = [];
}

public sealed class AstronomyIntegrityValidation
{
    public bool NoSyntheticObjectsAdded { get; init; } = true;
    public bool ObjectCountPreserved { get; init; } = true;
    public bool ConstellationRelationshipsPreserved { get; init; } = true;
    public bool AstronomicalIntegrityMaintained { get; init; } = true;
    public IReadOnlyCollection<string> Notes { get; init; } = [];
}

public sealed class CinematicThumbnailAiReport
{
    public string DominantObject { get; init; } = "unknown";
    public string MoodProfile { get; init; } = "dramatic";
    public string CropStrategy { get; init; } = "center-crop";
    public bool EnhancementApplied { get; init; }
    public double ScaleBoost { get; init; } = 1;
    public IReadOnlyCollection<string> OverlaysApplied { get; init; } = [];
    public double ObjectScaleBoost { get; init; } = 1;
    public IReadOnlyCollection<string> FinalPaths { get; init; } = [];
    public double VisualHierarchyScore { get; init; }
    public double ReadabilityScore { get; init; }
    public double OrganicAtmosphereScore { get; init; }
    public double NaturalLightingScore { get; init; }
    public double VisualArtifactPenalty { get; init; }
    public double CompositingVisibilityPenalty { get; init; }
    public double EdgeIntegrationScore { get; init; }
    public double CompositingSeamPenalty { get; init; }
    public double AtmosphereContinuityScore { get; init; }
    public double EnvironmentalDepthScore { get; init; }
    public double SupportObjectDepthScore { get; init; }
    public double CinematicSubtletyScore { get; init; }
    public AstronomyIntegrityValidation AstronomyIntegrityValidation { get; init; } = new();
    public bool PortraitSafe { get; init; }
    public string FinalThumbnailPath { get; init; } = "";
    public bool FallbackUsed { get; init; }
    public bool VisualPolishPassApplied { get; init; }
    public IReadOnlyCollection<string> Diagnostics { get; init; } = [];
}
