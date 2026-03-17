namespace Astronomy.MediaFactory.Rendering;
public sealed class StellariumScriptService
{
    public string BuildSkyOverviewScript(DateOnly date, string locationName)
    {
        return $"core.clear(\"natural\");\ncore.setDate(\"{date:yyyy-MM-dd}T20:00:00\", \"local\");\ncore.screenshot(\"sky-overview-{date:yyyyMMdd}.png\", false, \"png\");\n";
    }
}
