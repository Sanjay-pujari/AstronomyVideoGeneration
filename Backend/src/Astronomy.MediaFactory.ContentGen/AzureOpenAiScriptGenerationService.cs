using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.ContentGen;

public sealed class AzureOpenAiScriptGenerationService : IScriptGenerationService
{
    private readonly AzureOpenAiOptions _options;
    public AzureOpenAiScriptGenerationService(IOptions<AzureOpenAiOptions> options) { _options = options.Value; }
    public Task<ScriptResult> GenerateAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
    {
        var prompt = AstronomyPromptBuilder.Build(contentType, context);
        var title = contentType switch
        {
            ContentType.DailySkyGuide => $"What To See In The Sky Tonight - {context.Date:MMMM dd, yyyy}",
            ContentType.TelescopeTargets => $"Best Telescope Targets Tonight - {context.Date:MMMM dd, yyyy}",
            ContentType.SpaceNews => $"Space News Roundup - {context.Date:MMMM dd, yyyy}",
            _ => $"Astrophotography Targets Tonight - {context.Date:MMMM dd, yyyy}"
        };
        var script = $"Welcome to your astronomy briefing for {context.Date:MMMM dd, yyyy}. " + string.Join(" ", context.Events.OrderByDescending(x => x.Score).Select(x => $"{x.ObjectName} is visible {x.VisibilityWindow}, toward the {x.Direction}, best with {x.ObservationTool}. {x.Details}"));
        if (context.NewsItems.Count > 0) script += " " + string.Join(" ", context.NewsItems.Select(x => $"{x.Headline}. {x.Summary}"));
        return Task.FromResult(new ScriptResult { Prompt = prompt, Title = title, Description = $"Automated astronomy video for {context.Date:MMMM dd, yyyy}.", ScriptBody = script, Tags = new[] { "astronomy", "night sky", "space", contentType.ToString() }, EstimatedDurationSeconds = 900 });
    }
}
