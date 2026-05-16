namespace Astronomy.MediaFactory.Rendering;

public static class CelestialObjectKeyMapper
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mercury"] = "mercury",
        ["venus"] = "venus",
        ["earth"] = "earth",
        ["mars"] = "mars",
        ["jupiter"] = "jupiter",
        ["saturn"] = "saturn",
        ["uranus"] = "uranus",
        ["neptune"] = "neptune",
        ["sun"] = "sun",
        ["moon"] = "moon",
        ["milky way"] = "milky-way",
        ["milky-way"] = "milky-way",
        ["orion nebula"] = "orion-nebula",
        ["andromeda galaxy"] = "andromeda-galaxy",
        ["ring nebula"] = "ring-nebula",
        ["meteor shower"] = "meteor-shower",
        ["meteor showers"] = "meteor-shower",
        ["meteor-showers"] = "meteor-shower",
        ["solar eclipse"] = "solar-eclipse",
        ["lunar eclipse"] = "lunar-eclipse",
        ["nebula"] = "orion-nebula",
        ["galaxy"] = "andromeda-galaxy"
    };

    public static string Map(string? objectName, string? objectType = null)
    {
        var text = $"{objectName} {objectType}".Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
            return "milky-way";

        foreach (var (needle, key) in Known.OrderByDescending(x => x.Key.Length))
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        return "milky-way";
    }
}
