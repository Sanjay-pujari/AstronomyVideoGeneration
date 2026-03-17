using System.Text;
using Astronomy.MediaFactory.Contracts;
namespace Astronomy.MediaFactory.Core;
public static class AstronomyPromptBuilder
{
    public static string Build(ContentType contentType, AstronomyContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an astronomy educator and YouTube scriptwriter.");
        sb.AppendLine("Return JSON only with title, description, tags, estimatedDurationSeconds, scriptBody.");
        sb.AppendLine($"Date: {context.Date:yyyy-MM-dd}");
        sb.AppendLine($"Location: {context.LocationName}");
        sb.AppendLine($"Timezone: {context.TimeZone}");
        sb.AppendLine($"ContentType: {contentType}");
        sb.AppendLine();
        sb.AppendLine("Astronomy events:");
        foreach (var e in context.Events.OrderByDescending(x => x.Score))
            sb.AppendLine($"- {e.Category} | {e.ObjectName} | {e.VisibilityWindow} | {e.Direction} | {e.ObservationTool} | Score={e.Score:F2} | {e.Details}");
        if (context.NewsItems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Space news:");
            foreach (var n in context.NewsItems)
                sb.AppendLine($"- {n.PublishedDate:yyyy-MM-dd} | {n.SourceName} | {n.Headline} | {n.Summary}");
        }
        return sb.ToString();
    }
}
