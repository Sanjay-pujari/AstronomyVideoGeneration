using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Composition;

public sealed class PrimaryTargetResolver : IPrimaryTargetResolver
{
    private static readonly string[] HighPriority = ["moon", "venus", "jupiter", "saturn", "mars", "mercury"];
    private static readonly string[] ContextHints = ["constellation", "milky", "background", "starfield"];

    public PrimaryTargetResult Resolve(IReadOnlyList<SkyObjectPosition> visibleObjects, string? sceneCode, string? sceneTitle, IReadOnlyList<string>? explicitTargets)
    {
        var explicitSet = new HashSet<string>((explicitTargets ?? []).Select(Norm), StringComparer.OrdinalIgnoreCase);
        var code = Norm(sceneCode);
        var title = Norm(sceneTitle);
        var primary = new List<SkyObjectPosition>();
        var secondary = new List<SkyObjectPosition>();
        var context = new List<SkyObjectPosition>();

        foreach (var o in visibleObjects)
        {
            var n = Norm(o.Name);
            var isContext = ContextHints.Any(h => n.Contains(h, StringComparison.OrdinalIgnoreCase)) || (o.ObjectType?.Contains("constellation", StringComparison.OrdinalIgnoreCase) ?? false);
            var isPrimary = explicitSet.Any(t => n.Contains(t, StringComparison.OrdinalIgnoreCase)) || HighPriority.Any(h => n.Contains(h, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrWhiteSpace(code) && code.Contains(n, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrWhiteSpace(title) && title.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (isPrimary && !isContext) primary.Add(Scale(o, 1.5));
            else if (isContext) context.Add(Scale(o, 0.4));
            else secondary.Add(Scale(o, 1.0));
        }

        if (primary.Count == 0)
        {
            var brightest = visibleObjects.OrderBy(v => v.Magnitude).First();
            primary.Add(Scale(brightest, 1.5));
            secondary = secondary.Where(s => !Norm(s.Name).Equals(Norm(brightest.Name), StringComparison.OrdinalIgnoreCase)).ToList();
            context = context.Where(s => !Norm(s.Name).Equals(Norm(brightest.Name), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        primary = primary.OrderBy(v => v.Magnitude).Take(3).ToList();
        return new PrimaryTargetResult(primary, secondary, context);
    }

    private static SkyObjectPosition Scale(SkyObjectPosition o, double multiplier)
        => o with { Weight = (o.Weight <= 0 ? 1.0 : o.Weight) * multiplier };

    private static string Norm(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
