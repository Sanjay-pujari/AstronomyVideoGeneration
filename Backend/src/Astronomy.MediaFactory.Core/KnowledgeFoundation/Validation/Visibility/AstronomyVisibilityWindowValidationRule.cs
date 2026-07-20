using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Visibility;

public sealed class AstronomyVisibilityWindowValidationRule : AstronomyKnowledgeValidationRule<AstronomyVisibilityWindowsPayload>
{
    public const string Id = "visibility.window.integrity";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Observational; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.VisibilityWindow; public override int Order => 200;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyVisibilityWindowsPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var seen = new HashSet<(DateTimeOffset, DateTimeOffset, AstronomyVisibilityStatus, AstronomyVisibilityMethod)>();
        for (var i = 0; i < payload.Windows.Count; i++)
        {
            var w = payload.Windows[i]; var path = $"$.windows[{i}]";
            if (w.Window.StartUtc.Offset != TimeSpan.Zero || w.Window.EndUtc.Offset != TimeSpan.Zero || w.Window.StartUtc >= w.Window.EndUtc) yield return new(AstronomyVisibilityValidationCodes.WindowOrderInvalid, AstronomyKnowledgeValidationSeverity.Error, "Visibility window start and end must be UTC with start before end.", path + ".window", RuleId, Domain, Family);
            if (!seen.Add((w.Window.StartUtc, w.Window.EndUtc, w.Assessment.Status, w.Assessment.Method))) yield return new(AstronomyVisibilityValidationCodes.WindowDuplicate, AstronomyKnowledgeValidationSeverity.Error, "Duplicate visibility window identity.", path, RuleId, Domain, Family);
            for (var j = 0; j < i; j++)
            {
                var p = payload.Windows[j];
                if (w.Window.StartUtc < p.Window.EndUtc && p.Window.StartUtc < w.Window.EndUtc && w.Assessment.Status == p.Assessment.Status && w.Assessment.Method == p.Assessment.Method)
                    yield return new(AstronomyVisibilityValidationCodes.WindowOverlap, AstronomyKnowledgeValidationSeverity.Warning, "Equivalent visibility windows partially overlap.", path, RuleId, Domain, Family);
            }
        }
    }
}
