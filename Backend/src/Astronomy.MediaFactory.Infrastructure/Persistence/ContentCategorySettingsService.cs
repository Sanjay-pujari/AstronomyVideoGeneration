using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentCategorySettingsService(MediaFactoryDbContext db) : IContentCategorySettingsService
{
    public async Task<ContentCategorySettings?> GetSettingsAsync(ContentPipelineType type, CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        return await db.ContentCategorySettings.AsNoTracking().FirstOrDefaultAsync(x => x.PipelineType == type, cancellationToken);
    }

    public async Task<ContentCategoryPromptSettings?> GetPromptSettingsAsync(ContentPipelineType type, string language, CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        return await db.ContentCategoryPromptSettings.AsNoTracking().FirstOrDefaultAsync(x => x.PipelineType == type && x.Language == language, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ContentCategoryPublishingSettings>> GetPublishingSettingsAsync(ContentPipelineType type, CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        return await db.ContentCategoryPublishingSettings.AsNoTracking().Where(x => x.PipelineType == type).ToArrayAsync(cancellationToken);
    }

    public async Task<bool> IsEnabledAsync(ContentPipelineType type, CancellationToken cancellationToken = default) =>
        (await GetSettingsAsync(type, cancellationToken))?.Enabled ?? false;

    private async Task EnsureSeededAsync(CancellationToken ct)
    {
        if (await db.ContentCategorySettings.AnyAsync(ct)) return;
        var now = DateTimeOffset.UtcNow;
        var all = Enum.GetValues<ContentPipelineType>();
        foreach (var t in all)
        {
            db.ContentCategorySettings.Add(new ContentCategorySettings { PipelineType = t, DisplayName = t.ToString(), Enabled = t == ContentPipelineType.DailySkyGuide || t == ContentPipelineType.WeeklySkyForecast, DefaultLanguage = "en", DefaultRegionId = "india-udaipur", Frequency = t == ContentPipelineType.MonthlySkyReport ? "Monthly" : t == ContentPipelineType.WeeklySkyForecast ? "Weekly" : "Daily", TargetDurationSeconds = 360, MaxDurationSeconds = 540, MaxObjects = 6, GenerateLongVideo = true, GenerateShortVideo = true, GenerateThumbnail = true, PublishToYouTube = false, PublishToFacebook = false, PublishToInstagram = false, Priority = t == ContentPipelineType.DailySkyGuide ? 100 : 10, CreatedUtc = now, UpdatedUtc = now });
            db.ContentCategoryPromptSettings.Add(new ContentCategoryPromptSettings { PipelineType = t, Language = "en", ScriptPromptTemplate = "Generate {pipelineType} astronomy script.", HookPromptTemplate = "Write hook for {pipelineType}.", ThumbnailTextPromptTemplate = "Thumbnail text for {pipelineType}.", SeoPromptTemplate = "SEO metadata for {pipelineType}.", CreatedUtc = now, UpdatedUtc = now });
            db.ContentCategoryPublishingSettings.Add(new ContentCategoryPublishingSettings { PipelineType = t, Platform = "YouTube", Enabled = t == ContentPipelineType.DailySkyGuide, ContentType = "Long", PrivacyStatus = "private", PublishTimeWindowStart = "18:00", PublishTimeWindowEnd = "23:00", HashtagTemplate = "#astronomy #skygazing", CreatedUtc = now, UpdatedUtc = now });
        }

        await db.SaveChangesAsync(ct);
    }
}
