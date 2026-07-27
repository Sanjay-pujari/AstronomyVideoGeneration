using Astronomy.MediaFactory.Core;
namespace Astronomy.MediaFactory.Rendering;
public sealed class FileVisualAssetProvider : IVisualAssetProvider
{
    private readonly StellariumScriptService _stellariumScriptService;
    public FileVisualAssetProvider(StellariumScriptService stellariumScriptService) { _stellariumScriptService = stellariumScriptService; }
    public async Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var scriptPath = Path.Combine(outputDirectory, "stellarium-overview.ssc");
        await File.WriteAllTextAsync(scriptPath, _stellariumScriptService.BuildSkyOverviewScript(context.Date, context.LocationName), cancellationToken);
        var files = new List<string>();
        var index = 1;
        foreach (var visual in context.VisualIdeas.DefaultIfEmpty(new VisualIdeaModel { Title = "Fallback visual", Description = "Fallback card." }))
        {
            var path = Path.Combine(outputDirectory, $"visual-{index:000}.txt");
            await File.WriteAllTextAsync(path, $"{visual.Title}{Environment.NewLine}{visual.Description}{Environment.NewLine}{visual.SourcePathOrUrl}", cancellationToken);
            files.Add(path);
            index++;
        }
        return files;
    }

    public Task<string?> SelectExistingAssetAsync(string assetKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(assetKey)) return Task.FromResult<string?>(null);
        var path = Path.GetFullPath(assetKey);
        return Task.FromResult(File.Exists(path) ? path : null);
    }
}
