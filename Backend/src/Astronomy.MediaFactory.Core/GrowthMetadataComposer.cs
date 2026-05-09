using System.Text;
using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public static class GrowthMetadataComposer
{
    private const string AffiliateBlockHeading = "Observation gear links";

    public static GrowthMetadata BuildMetadata(GrowthOptions options, GrowthMetadataInput input)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);

        return new GrowthMetadata
        {
            Enabled = options.Enabled,
            CtaVariant = ResolveCtaVariant(input.Platform, options),
            PlatformCta = options.Enabled ? ResolvePlatformCta(input.Platform, input.Language) : string.Empty,
            DefaultCallToAction = options.Enabled ? ResolveDefaultCta(options, input.Language) : string.Empty,
            WebsiteUrl = NormalizeOptionalUrl(options.WebsiteUrl),
            NewsletterUrl = NormalizeOptionalUrl(options.NewsletterUrl),
            AppDownloadUrl = NormalizeOptionalUrl(options.AppDownloadUrl),
            AffiliateBlockEnabled = options.Enabled && options.EnableAffiliateBlocks,
            AffiliateDisclosure = options.Enabled && options.EnableAffiliateBlocks ? NormalizeDisclosure(options.AffiliateDisclosure) : null,
            Language = string.IsNullOrWhiteSpace(input.Language) ? "en" : input.Language.Trim(),
            Region = string.IsNullOrWhiteSpace(input.Region) ? null : input.Region.Trim(),
            Platform = input.Platform
        };
    }

    public static string ApplyGrowthBlock(string source, GrowthOptions options, GrowthMetadataInput input)
    {
        var metadata = BuildMetadata(options, input);
        if (!metadata.Enabled)
        {
            return source.Trim();
        }

        var block = BuildGrowthBlock(metadata, input.Language);
        return AppendBlockOnce(source, block);
    }

    public static string BuildGrowthBlock(GrowthMetadata metadata, string language)
    {
        if (!metadata.Enabled)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        AddLine(lines, metadata.PlatformCta);
        AddLine(lines, metadata.DefaultCallToAction);
        if (!string.IsNullOrWhiteSpace(metadata.WebsiteUrl))
        {
            AddLine(lines, LocalizationResolver.IsHindi(language) ? $"वेबसाइट: {metadata.WebsiteUrl}" : $"Website: {metadata.WebsiteUrl}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.NewsletterUrl))
        {
            AddLine(lines, LocalizationResolver.IsHindi(language) ? $"न्यूज़लेटर: {metadata.NewsletterUrl}" : $"Newsletter: {metadata.NewsletterUrl}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.AppDownloadUrl))
        {
            AddLine(lines, LocalizationResolver.IsHindi(language) ? $"ऐप डाउनलोड: {metadata.AppDownloadUrl}" : $"App download: {metadata.AppDownloadUrl}");
        }

        if (metadata.AffiliateBlockEnabled)
        {
            lines.Add(string.Empty);
            lines.Add(LocalizationResolver.IsHindi(language) ? "टेलिस्कोप और दूरबीन लिंक जल्द जोड़े जाएंगे।" : $"{AffiliateBlockHeading}: telescope and binocular links coming soon.");
            AddLine(lines, metadata.AffiliateDisclosure is null ? null : $"Disclosure: {metadata.AffiliateDisclosure}");
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }

    public static string AppendBlockOnce(string source, string block)
    {
        var normalizedSource = (source ?? string.Empty).Trim();
        var missingLines = (block ?? string.Empty)
            .Split('\n')
            .Select(NormalizeLine)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !normalizedSource.Contains(line, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (missingLines.Length == 0)
        {
            return normalizedSource;
        }

        var normalizedBlock = string.Join(Environment.NewLine, missingLines).Trim();
        return string.IsNullOrWhiteSpace(normalizedSource)
            ? normalizedBlock
            : $"{normalizedSource}{Environment.NewLine}{Environment.NewLine}{normalizedBlock}";
    }

    public static string ResolvePlatformCta(string platform, string language)
    {
        var isHindi = LocalizationResolver.IsHindi(language);
        return platform switch
        {
            "YouTube" or "YouTubeShorts" => isHindi ? "सब्सक्राइब करें और अगला स्काई गाइड देखें।" : "Subscribe and watch the next video.",
            "Instagram" or "InstagramReels" => isHindi ? "फॉलो करें और इस रील को सेव करें।" : "Follow and save this reel.",
            "Facebook" => isHindi ? "पेज फॉलो करें और दोस्तों के साथ शेयर करें।" : "Follow the page and share with friends.",
            _ => isHindi ? "और दैनिक आसमान गाइड के लिए फॉलो करें।" : "Follow for your daily sky guide."
        };
    }

    public static string ResolveCtaVariant(string platform, GrowthOptions options)
    {
        if (!options.Enabled)
        {
            return "disabled";
        }

        return platform switch
        {
            "YouTube" or "YouTubeShorts" => "youtube-subscribe-watch-next",
            "Instagram" or "InstagramReels" => "instagram-follow-save",
            "Facebook" => "facebook-follow-share",
            _ => "standard-follow"
        };
    }

    private static string ResolveDefaultCta(GrowthOptions options, string language)
    {
        if (LocalizationResolver.IsHindi(language) && options.DefaultCallToAction == "Follow AstroPulse for your daily sky guide.")
        {
            return "अपने दैनिक आकाश गाइड के लिए AstroPulse को फॉलो करें।";
        }

        return string.IsNullOrWhiteSpace(options.DefaultCallToAction) ? string.Empty : options.DefaultCallToAction.Trim();
    }

    private static string? NormalizeOptionalUrl(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeDisclosure(string? value) => string.IsNullOrWhiteSpace(value) ? "Some links may be affiliate links." : value.Trim();

    private static void AddLine(List<string> lines, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(value.Trim());
        }
    }

    private static string NormalizeLine(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
