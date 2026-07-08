namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Connectors;
public static class EditorialConnectorLibrary
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> All { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["HookToScience"] = ["But why does this happen?", "To understand that, we need to look at the geometry of the sky."],
        ["ScienceToObservation"] = ["Now that you know what is happening, here is how to see it.", "And this is where the observation becomes simple."],
        ["ObservationToClosing"] = ["So when you step outside, you will know exactly what to look for.", "That small moment in the sky is the real story."]
    };
}
