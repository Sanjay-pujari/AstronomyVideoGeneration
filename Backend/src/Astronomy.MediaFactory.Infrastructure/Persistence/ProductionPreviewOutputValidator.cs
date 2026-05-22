using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ProductionPreviewOutputValidator : IProductionPreviewOutputValidator
{
    public async Task<ProductionPreviewValidationResult> ValidateAsync(string? outputFolder, string? longAudioPath, string? longVideoPath, string? shortVideoPath, string? longThumbnailPath, string? shortThumbnailPath, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        ValidateNonEmptyFile(longAudioPath, "Long audio", errors);
        ValidateNonEmptyFile(longVideoPath, "Long video", errors);
        ValidateNonEmptyFile(shortVideoPath, "Short video", errors);
        ValidateNonEmptyFile(longThumbnailPath, "Long thumbnail", errors);
        ValidateNonEmptyFile(shortThumbnailPath, "Short thumbnail", errors);

        string? validationReportPath = null;
        if (!string.IsNullOrWhiteSpace(outputFolder) && Directory.Exists(outputFolder))
        {
            validationReportPath = Path.Combine(outputFolder, "production-preview-validation.json");
            var payload = JsonSerializer.Serialize(new
            {
                generatedUtc = DateTime.UtcNow,
                valid = errors.Count == 0,
                errors
            }, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(validationReportPath, payload, cancellationToken);
        }

        return new ProductionPreviewValidationResult(errors.Count == 0, errors, validationReportPath);
    }

    private static void ValidateNonEmptyFile(string? path, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            errors.Add($"{label} is missing.");
            return;
        }

        if (new FileInfo(path).Length <= 0)
        {
            errors.Add($"{label} exists but is empty: {path}");
        }
    }
}
