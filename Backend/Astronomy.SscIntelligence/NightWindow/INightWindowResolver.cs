using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.NightWindow;

public interface INightWindowResolver
{
    NightWindowResult Resolve(DateTime date, string timezone, double latitude, double longitude, VisibilityRules rules, DateTime? astronomicalNightStartUtc = null, DateTime? astronomicalNightEndUtc = null, double? sunAltitudeDeg = null);
}
