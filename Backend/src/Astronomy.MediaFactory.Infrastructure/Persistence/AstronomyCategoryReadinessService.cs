using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyCategoryReadinessService(MediaFactoryDbContext db) : IAstronomyCategoryReadinessService
{
    private static readonly IReadOnlyDictionary<string, int> Phase7Order = AstronomyOpportunityCategoryCodes.Phase7CategoryCodes
        .Select((code, index) => new { code, index })
        .ToDictionary(x => x.code, x => x.index, StringComparer.OrdinalIgnoreCase);

    public async Task<AstronomyCategoryReadinessResult> GetCategoryReadinessAsync(IReadOnlyList<string>? categoryCodes, CancellationToken cancellationToken)
    {
        var requestedCodes = NormalizeCodes(categoryCodes);
        var existingRows = await db.ContentCategories
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var existing = existingRows.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

        var readiness = requestedCodes
            .Select(code => BuildReadiness(code, existing.GetValueOrDefault(code)))
            .ToArray();

        return new AstronomyCategoryReadinessResult(readiness);
    }

    private static IReadOnlyList<string> NormalizeCodes(IReadOnlyList<string>? categoryCodes)
    {
        var source = categoryCodes is { Count: > 0 }
            ? categoryCodes
            : AstronomyOpportunityCategoryCodes.Phase7CategoryCodes;

        return source
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => Phase7Order.GetValueOrDefault(code, int.MaxValue))
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AstronomyCategoryReadinessDto BuildReadiness(string code, ContentCategoryMaster? category)
    {
        if (category is null)
        {
            return new AstronomyCategoryReadinessDto(
                code,
                Exists: false,
                IsActive: false,
                DisplayName: null,
                CanPlan: false,
                Warning: $"Content category '{code}' is missing from content_categories.");
        }

        var canPlan = category.Enabled;
        return new AstronomyCategoryReadinessDto(
            code,
            Exists: true,
            IsActive: category.Enabled,
            DisplayName: category.DisplayName,
            CanPlan: canPlan,
            Warning: canPlan ? null : $"Content category '{code}' exists but is inactive in content_categories.");
    }
}
