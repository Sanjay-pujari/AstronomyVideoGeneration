namespace Astronomy.MediaFactory.Core.EditorialIntelligence.StyleGuide;

public static class AstroPulseStyleGuide
{
    public const string Version = "AstroPulse-v1";
    public static EditorialRules EditorialRules { get; } = new(["calm", "clear", "documentary", "scientifically careful", "emotionally warm but not exaggerated"]);
    public static VocabularyRules VocabularyRules { get; } = new(
        ["appears", "look toward", "visible near", "rises", "sets", "reaches its highest point", "brighter object", "steady glow", "western horizon", "eastern horizon", "clear skies"],
        ["insane", "crazy", "unbelievable", "magical", "mind-blowing", "once in a lifetime", "shocking", "you won’t believe", "mysterious object", "alien-like"]);
    public static ChannelIdentityRules ChannelIdentityRules { get; } = new("Until next time, keep looking up.", ["Clear skies, and see you in the next Astro Pulse.", "The universe always has another story to tell."]);
}
