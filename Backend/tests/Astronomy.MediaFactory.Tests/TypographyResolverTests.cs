using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Rendering;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Astronomy.MediaFactory.Tests;

public sealed class TypographyResolverTests
{
    [Fact]
    public void EnglishHero_UsesExistingLatinFontStackAndUnchangedMetrics()
    {
        var resolver = new TypographyResolver();

        var typography = resolver.Resolve(new TypographyRequest("en", TypographyTextRole.Title, TypographyAssetKind.Hero, 56f, FontStyle.Bold, 690f, 1280, 720));

        typography.FontFamilyName.Should().BeOneOf("Inter", "Segoe UI", "Arial", "DejaVu Sans", "Liberation Sans");
        typography.Font.Size.Should().Be(56f);
        typography.FontSizeScale.Should().Be(1.0f);
        typography.WrapWidth.Should().Be(690f);
        typography.BaselinePadding.Should().Be(0f);
    }

    [Fact]
    public void HindiHero_UsesConfiguredDevanagariCompatibleFont()
    {
        var resolver = new TypographyResolver(Options.Create(new TypographyOptions
        {
            Languages = new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = TypographyOptions.CreateEnglishDefaults(),
                ["hi"] = new TypographyLanguageOptions
                {
                    FontFamilies = ["DejaVu Sans"],
                    FontSizeScale = 0.94f,
                    LineHeight = 1.24f,
                    BaselinePadding = 0.18f,
                    WrapWidthScale = 0.96f,
                    Roles = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Title"] = new() { FontFamilies = ["DejaVu Sans"], FontSizeScale = 0.92f, LineHeight = 1.28f, BaselinePadding = 0.22f }
                    }
                }
            }
        }));

        var typography = resolver.Resolve(new TypographyRequest("hi-IN", TypographyTextRole.Title, TypographyAssetKind.Hero, 56f, FontStyle.Bold, 690f, 1280, 720));

        typography.FontFamilyName.Should().Be("DejaVu Sans");
        typography.Font.Size.Should().BeApproximately(51.52f, 0.01f);
        typography.LineHeight.Should().Be(1.28f);
        typography.BaselinePadding.Should().Be(0.22f);
    }

    [Fact]
    public void HindiText_DoesNotClipWithLanguageAwareMetrics()
    {
        var resolver = new TypographyResolver();
        var typography = resolver.Resolve(new TypographyRequest("hi", TypographyTextRole.Title, TypographyAssetKind.Hero, 56f, FontStyle.Bold, 690f, 1280, 720));
        var text = "आज रात पश्चिम में शुक्र और गुरु देखें";
        var options = new RichTextOptions(typography.Font)
        {
            Origin = new PointF(80, 54 + typography.Font.Size * typography.BaselinePadding),
            WrappingLength = typography.WrapWidth,
            LineSpacing = typography.LineHeight
        };

        var bounds = TextMeasurer.MeasureBounds(text, options);

        bounds.Top.Should().BeGreaterThanOrEqualTo(54f);
        bounds.Bottom.Should().BeLessThan(54f + 82f + typography.Font.Size * 1.35f);
    }

    [Fact]
    public async Task HindiFooterMetadata_RendersCorrectly()
    {
        using var image = await AstronomyVisualCompositionEngine.ComposeAsync(new AstronomyVisualCompositionRequest(
            1280,
            720,
            "आज रात आकाश देखें",
            "दुर्लभ ग्रह जोड़ी",
            "",
            [],
            labels: [new AstronomyVisualLabel("आज रात • पश्चिम दिशा", 0, 0, Color.White)],
            compositionMode: AstronomyVisualCompositionMode.HeroAsset,
            language: "hi"), CancellationToken.None);

        image.Width.Should().Be(1280);
        image.Height.Should().Be(720);
        ImageHasNonBackgroundPixelsNearFooter(image).Should().BeTrue();
    }

    [Theory]
    [InlineData(TypographyAssetKind.Thumbnail)]
    [InlineData(TypographyAssetKind.Gallery)]
    public void SameResolver_CanBeReusedByThumbnailAndGallery(TypographyAssetKind assetKind)
    {
        var resolver = new TypographyResolver();

        var title = resolver.Resolve(new TypographyRequest("hi", TypographyTextRole.Title, assetKind, 64f, FontStyle.Bold, 720f, 1080, 1080));
        var footer = resolver.Resolve(new TypographyRequest("hi", TypographyTextRole.Footer, assetKind, 32f, FontStyle.Regular, 720f, 1080, 1080));

        title.FontFamilyName.Should().NotBeNullOrWhiteSpace();
        footer.FontFamilyName.Should().NotBeNullOrWhiteSpace();
        title.Font.Size.Should().BeGreaterThan(0f);
        footer.WrapWidth.Should().BeGreaterThan(0f);
    }

    private static bool ImageHasNonBackgroundPixelsNearFooter(Image<Rgba32> image)
    {
        var nonDarkPixels = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 540; y < 630; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 70; x < 430; x++)
                {
                    var pixel = row[x];
                    if (pixel.R > 170 || pixel.G > 170 || pixel.B > 170)
                        nonDarkPixels++;
                }
            }
        });
        return nonDarkPixels > 20;
    }
}
