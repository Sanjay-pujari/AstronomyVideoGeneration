namespace Astronomy.MediaFactory.Core;

public sealed class PlatformMetadataFormatter : IShortFormPlatformMetadataFormatter
{
    public PlatformPublicationTarget FormatTarget(ShortFormPlatform platform, ShortFormPublicationRequest request)
    {
        var hashtags = NormalizeHashtags(request.Hashtags, request.Tags);
        var hook = string.IsNullOrWhiteSpace(request.HookLine) ? request.Title.Trim() : request.HookLine.Trim();
        var captionBody = string.IsNullOrWhiteSpace(request.Caption) ? request.Title.Trim() : request.Caption.Trim();

        return platform switch
        {
            ShortFormPlatform.YouTubeShorts => new PlatformPublicationTarget
            {
                Platform = platform,
                Enabled = true,
                Title = Limit(request.Title, 90),
                Caption = BuildYouTubeCaption(hook, captionBody, EnsureHashtag(hashtags, "shorts")),
                Hashtags = EnsureHashtag(hashtags, "shorts"),
                VideoPath = request.VideoPath,
                ThumbnailPath = request.ThumbnailPath
            },
            ShortFormPlatform.InstagramReels => new PlatformPublicationTarget
            {
                Platform = platform,
                Enabled = true,
                Title = Limit(request.Title, 120),
                Caption = BuildInstagramCaption(hook, captionBody, hashtags),
                Hashtags = hashtags.Take(5).ToArray(),
                VideoPath = request.VideoPath,
                ThumbnailPath = request.ThumbnailPath
            },
            ShortFormPlatform.Facebook => new PlatformPublicationTarget
            {
                Platform = platform,
                Enabled = true,
                Title = Limit(request.Title, 120),
                Caption = BuildFacebookCaption(hook, captionBody, hashtags),
                Hashtags = hashtags.Take(3).ToArray(),
                VideoPath = request.VideoPath,
                ThumbnailPath = request.ThumbnailPath
            },
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported short-form platform.")
        };
    }

    private static string BuildYouTubeCaption(string hook, string captionBody, IReadOnlyCollection<string> hashtags)
    {
        var hashText = string.Join(' ', hashtags.Take(4));
        return $"{Limit(hook, 80)}\n\n{Limit(captionBody, 180)}\n\n{hashText}".Trim();
    }

    private static string BuildInstagramCaption(string hook, string captionBody, IReadOnlyCollection<string> hashtags)
    {
        var hashText = string.Join(' ', hashtags.Take(5));
        return $"{Limit(hook, 120)}\n\n{Limit(captionBody, 220)}\nSave this reel for tonight's sky.\n\n{hashText}".Trim();
    }

    private static string BuildFacebookCaption(string hook, string captionBody, IReadOnlyCollection<string> hashtags)
    {
        var hashText = string.Join(' ', hashtags.Take(3));
        return $"{Limit(hook, 140)}\n\n{Limit(captionBody, 260)}\nFollow for more astronomy shorts and nightly sky updates.\n\n{hashText}".Trim();
    }

    private static string Limit(string value, int maxLength)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Length <= maxLength
                ? value.Trim()
                : value.Trim()[..maxLength].TrimEnd();

    private static IReadOnlyCollection<string> NormalizeHashtags(IReadOnlyCollection<string> hashtags, IReadOnlyCollection<string> tags)
    {
        var values = hashtags
            .Concat(tags.Select(static tag => $"#{tag.Replace(' ', '_')}"))
            .Select(static tag => tag.Trim())
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.StartsWith('#') ? tag : $"#{tag}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? ["#astronomy"] : values;
    }

    private static IReadOnlyCollection<string> EnsureHashtag(IReadOnlyCollection<string> hashtags, string hashtag)
    {
        var normalized = hashtag.StartsWith('#') ? hashtag : $"#{hashtag}";
        return [normalized, .. hashtags.Where(x => !x.Equals(normalized, StringComparison.OrdinalIgnoreCase))];
    }
}
