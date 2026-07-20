using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Visibility;

public sealed class AstronomyVisibilityPeakValidationRule : AstronomyKnowledgeValidationRule<AstronomyVisibilityWindowsPayload>
{
    public const string Id = "visibility.peak.integrity";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Observational; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.VisibilityWindow; public override int Order => 400;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyVisibilityWindowsPayload payload, AstronomyKnowledgeValidationContext context)
    {
        for (var i = 0; i < payload.Windows.Count; i++)
        {
            var w = payload.Windows[i]; var path = $"$.windows[{i}]";
            if (w.PeakTimeUtc.HasValue && (w.PeakTimeUtc.Value.Offset != TimeSpan.Zero || w.PeakTimeUtc.Value < w.Window.StartUtc || w.PeakTimeUtc.Value > w.Window.EndUtc)) yield return new(AstronomyVisibilityValidationCodes.PeakTimeOutsideWindow, AstronomyKnowledgeValidationSeverity.Error, "Peak time must be UTC and lie inside the visibility window.", path + ".peakTimeUtc", RuleId, Domain, Family);
            if (w.PeakAltitude is not null)
            {
                foreach (var issue in AstronomyObservationalMeasurementValidator.Validate(w.PeakAltitude, path + ".peakAltitude", RuleId, Domain, Family)) yield return issue;
                if (w.PeakAltitude.Unit.Dimension != AstronomyMeasurementDimension.Angle) yield return new(AstronomyVisibilityValidationCodes.PeakAltitudeDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "Peak altitude must use an angular measurement.", path + ".peakAltitude.unit.dimension", RuleId, Domain, Family);
            }
        }
    }
}
