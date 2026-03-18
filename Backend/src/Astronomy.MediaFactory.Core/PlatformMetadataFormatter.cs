using System.Text;
using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class PlatformMetadataFormatter : IShortFormPlatformMetadataFormatter
{
    private const int YouTubeTitleLimit = 90;
    private const int InstagramTitleLimit = 120;
    private const int FacebookTitleLimit = 120;
    private const int YouTubeCaptionLimit = 120;
    private const int InstagramCaptionLimit = 280;
    private const int FacebookCaptionLimit = 360;
    private static readonly string[] FallbackHashtags = ["#astronomy", "#nightsky", "#space"];

    private readonly PlatformPublishingOptions _options;

    public PlatformMetadataFormatter()
        : this(new PlatformPublishingOptions())
    {
    }

    public PlatformMetadataFormatter(PlatformPublishingOptions options)
        => _options = options ?? new PlatformPublishingOptions();

    public PlatformPublicationTarget FormatTarget(ShortFormPlatform platform, ShortFormPublicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedTitle = FirstNonEmpty(NormalizeInline(request.Title), "Astronomy update");
        var normalizedHook = FirstNonEmpty(NormalizeInline(request.HookLine), normalizedTitle);
        var normalizedCaptionBody = FirstNonEmpty(NormalizeInline(request.Caption), normalizedHook, normalizedTitle);
        var hashtags = NormalizeHashtags(request.Hashtags ?? Array.Empty<string>(), request.Tags ?? Array.Empty<string>());

        return platform switch
        {
            ShortFormPlatform.YouTubeShorts => BuildYouTubeTarget(request, normalizedTitle, normalizedHook, normalizedCaptionBody, hashtags),
            ShortFormPlatform.InstagramReels => BuildInstagramTarget(request, normalizedTitle, normalizedHook, normalizedCaptionBody, hashtags),
            ShortFormPlatform.Facebook => BuildFacebookTarget(request, normalizedTitle, normalizedHook, normalizedCaptionBody, hashtags),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported short-form platform.")
        };
    }

    private PlatformPublicationTarget BuildYouTubeTarget(
        ShortFormPublicationRequest request,
        string normalizedTitle,
        string normalizedHook,
        string normalizedCaptionBody,
        IReadOnlyCollection<string> hashtags)
    {
        var platformHashtags = EnsureHashtag(SelectHashtags(hashtags, minCount: 3, maxCount: 5), "shorts");
        var titleLikeLine = FirstNonEmpty(LimitAtWordBoundary(normalizedHook, 70), LimitAtWordBoundary(normalizedTitle, YouTubeTitleLimit));
        var cta = BuildYouTubeCta(normalizedCaptionBody);
        var caption = BuildCaption(
            YouTubeCaptionLimit,
            "\n",
            titleLikeLine,
            cta,
            string.Join(' ', platformHashtags.Take(5)));

        return new PlatformPublicationTarget
        {
            Platform = ShortFormPlatform.YouTubeShorts,
            Enabled = true,
            Title = LimitAtWordBoundary(normalizedTitle, YouTubeTitleLimit),
            Caption = caption,
            Hashtags = platformHashtags,
            PreferredPublishLocalTime = _options.YouTubeShortsPreferredPublishLocalTime,
            VideoPath = request.VideoPath,
            ThumbnailPath = request.ThumbnailPath
        };
    }

    private PlatformPublicationTarget BuildInstagramTarget(
        ShortFormPublicationRequest request,
        string normalizedTitle,
        string normalizedHook,
        string normalizedCaptionBody,
        IReadOnlyCollection<string> hashtags)
    {
        var platformHashtags = SelectInstagramHashtags(hashtags);
        var hookLine = FirstNonEmpty(LimitAtWordBoundary(normalizedHook, 110), LimitAtWordBoundary(normalizedTitle, InstagramTitleLimit));
        var bodyParagraph = TrimToSentenceBoundary(normalizedCaptionBody, 150);
        var caption = BuildCaption(
            InstagramCaptionLimit,
            "\n\n",
            hookLine,
            bodyParagraph,
            "Save this for tonight.",
            string.Join(' ', platformHashtags));

        return new PlatformPublicationTarget
        {
            Platform = ShortFormPlatform.InstagramReels,
            Enabled = true,
            Title = LimitAtWordBoundary(normalizedTitle, InstagramTitleLimit),
            Caption = caption,
            Hashtags = platformHashtags,
            PreferredPublishLocalTime = _options.InstagramReelsPreferredPublishLocalTime,
            VideoPath = request.VideoPath,
            ThumbnailPath = request.ThumbnailPath
        };
    }

    private PlatformPublicationTarget BuildFacebookTarget(
        ShortFormPublicationRequest request,
        string normalizedTitle,
        string normalizedHook,
        string normalizedCaptionBody,
        IReadOnlyCollection<string> hashtags)
    {
        var platformHashtags = (request.Hashtags?.Count ?? 0) == 0 && (request.Tags?.Count ?? 0) == 0
            ? Array.Empty<string>()
            : SelectHashtags(hashtags, minCount: 0, maxCount: 3, allowFallback: false);
        var openingLine = FirstNonEmpty(LimitAtWordBoundary(normalizedHook, 120), LimitAtWordBoundary(normalizedTitle, FacebookTitleLimit));
        var descriptiveBody = TrimToSentenceBoundary(normalizedCaptionBody, 230);
        var hashtagLine = platformHashtags.Count > 0 ? string.Join(' ', platformHashtags) : null;
        var caption = BuildCaption(
            FacebookCaptionLimit,
            "\n\n",
            openingLine,
            descriptiveBody,
            "Follow for more night-sky updates and share this with your stargazing crew.",
            hashtagLine);

        return new PlatformPublicationTarget
        {
            Platform = ShortFormPlatform.Facebook,
            Enabled = true,
            Title = LimitAtWordBoundary(normalizedTitle, FacebookTitleLimit),
            Caption = caption,
            Hashtags = platformHashtags,
            PreferredPublishLocalTime = _options.FacebookPreferredPublishLocalTime,
            VideoPath = request.VideoPath,
            ThumbnailPath = request.ThumbnailPath
        };
    }

    private static string BuildYouTubeCta(string captionBody)
    {
        if (string.IsNullOrWhiteSpace(captionBody))
        {
            return string.Empty;
        }

        return captionBody.Contains("tonight", StringComparison.OrdinalIgnoreCase)
            ? "Look up tonight."
            : "Watch for more.";
    }

    private static string BuildCaption(int maxLength, string separator, params string?[] segments)
    {
        var normalizedSegments = segments
            .Select(NormalizeInline)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        var builder = new StringBuilder();
        foreach (var segment in normalizedSegments)
        {
            var candidate = builder.Length == 0 ? segment : $"{builder}{separator}{segment}";
            if (candidate.Length <= maxLength)
            {
                builder.Clear();
                builder.Append(candidate);
                continue;
            }

            if (builder.Length == 0)
            {
                builder.Append(LimitAtWordBoundary(segment, maxLength));
            }

            break;
        }

        return NormalizeCaption(builder.ToString());
    }

    private static IReadOnlyCollection<string> SelectInstagramHashtags(IReadOnlyCollection<string> hashtags)
    {
        var specific = hashtags
            .Where(static tag => !tag.Equals("#astronomy", StringComparison.OrdinalIgnoreCase) && !tag.Equals("#shorts", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var values = new List<string>();
        AddDistinct(values, "#astronomy");

        foreach (var tag in specific.Take(9))
        {
            AddDistinct(values, tag);
        }

        if (values.Count < 5)
        {
            foreach (var fallback in hashtags.Concat(FallbackHashtags))
            {
                AddDistinct(values, fallback);
                if (values.Count >= 5)
                {
                    break;
                }
            }
        }

        return values.Take(10).ToArray();
    }

    private static IReadOnlyCollection<string> SelectHashtags(IReadOnlyCollection<string> hashtags, int minCount, int maxCount, bool allowFallback = true)
    {
        var values = new List<string>();
        foreach (var tag in hashtags)
        {
            AddDistinct(values, tag);
            if (values.Count >= maxCount)
            {
                break;
            }
        }

        if (allowFallback && values.Count < minCount)
        {
            foreach (var fallback in FallbackHashtags)
            {
                AddDistinct(values, fallback);
                if (values.Count >= minCount)
                {
                    break;
                }
            }
        }

        return values.Take(maxCount).ToArray();
    }

    private static void AddDistinct(ICollection<string> values, string hashtag)
    {
        if (values.Contains(hashtag, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        values.Add(hashtag);
    }

    private static string NormalizeInline(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string NormalizeCaption(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lines = value
            .Split('\n')
            .Select(NormalizeInline)
            .ToArray();

        var builder = new StringBuilder();
        var previousWasBlank = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (builder.Length > 0 && !previousWasBlank)
                {
                    builder.AppendLine();
                    previousWasBlank = true;
                }

                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
            previousWasBlank = false;
        }

        return builder.ToString().Trim();
    }

    private static string LimitAtWordBoundary(string value, int maxLength)
    {
        var normalized = NormalizeInline(value);
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var shortened = normalized[..Math.Min(normalized.Length, maxLength + 1)].TrimEnd();
        var lastSpace = shortened.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
        {
            shortened = shortened[..lastSpace];
        }
        else
        {
            shortened = shortened[..maxLength];
        }

        return shortened.TrimEnd(' ', ',', ';', ':', '-', '—');
    }

    private static string TrimToSentenceBoundary(string value, int maxLength)
    {
        var normalized = NormalizeInline(value);
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var limited = LimitAtWordBoundary(normalized, maxLength);
        var sentenceEnd = limited.LastIndexOfAny(['.', '!', '?']);
        if (sentenceEnd >= Math.Max(20, limited.Length / 2))
        {
            return limited[..(sentenceEnd + 1)].Trim();
        }

        return limited;
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static IReadOnlyCollection<string> NormalizeHashtags(IReadOnlyCollection<string> hashtags, IReadOnlyCollection<string> tags)
    {
        var values = hashtags
            .Concat(tags.Select(static tag => tag.Replace('#', ' ').Replace(' ', '_')))
            .Select(static tag => NormalizeInline(tag).Replace(" ", string.Empty))
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.StartsWith('#') ? tag : $"#{tag}")
            .Select(static tag => tag.Replace("##", "#"))
            .Where(static tag => tag.Length > 1)
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
