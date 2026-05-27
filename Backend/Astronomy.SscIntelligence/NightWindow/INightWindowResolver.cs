using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.NightWindow;

public interface INightWindowResolver
{
    NightWindowResult Resolve(DateTime observationUtc, VisibilityRules rules, double? sunAltitudeDeg = null);
}
