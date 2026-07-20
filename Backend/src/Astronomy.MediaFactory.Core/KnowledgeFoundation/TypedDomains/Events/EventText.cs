using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

internal static class EventText
{
    public const int MaxTokenLength = 128;
    public const int MaxLabelLength = 128;
    public const int MaxNoteLength = 512;
    public const int MaxNameLength = 256;
    public const int MaxSummaryLength = 1024;
    public static string Token(string value, string name, string display) => KnowledgeId.NormalizeToken(value, name, display, MaxTokenLength).ToLowerInvariant();
    public static string? Optional(string? value, int max, string name, string display) => TypedKnowledgeTextGuards.NormalizeOptionalText(value, max, name, display);
}
