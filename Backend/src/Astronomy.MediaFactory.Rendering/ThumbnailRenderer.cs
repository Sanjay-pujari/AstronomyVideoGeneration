using System.Text.Json;
using Astronomy.MediaFactory.Core;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

public interface IThumbnailRenderer
{
    Task<ThumbnailRenderResult> RenderAsync(ThumbnailRendererInput input, CancellationToken cancellationToken);
}

public sealed class ThumbnailRenderer : IThumbnailRenderer
{
    public async Task<ThumbnailRenderResult> RenderAsync(ThumbnailRendererInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ThumbnailPromptContractValidator.Validate(input.PromptContract);
        Directory.CreateDirectory(Path.GetDirectoryName(input.OutputImagePath) ?? ".");
        Directory.CreateDirectory(Path.GetDirectoryName(input.OutputLayoutJsonPath) ?? ".");

        using var image = await Image.LoadAsync<Rgba32>(input.ArtworkImagePath, cancellationToken);
        var sourceRatio = Math.Round((double)image.Width / image.Height, 3);
        var targetRatio = Math.Round((double)input.PromptContract.Platform.Width / input.PromptContract.Platform.Height, 3);
        if (Math.Abs(sourceRatio - targetRatio) > 0.01)
            throw new InvalidOperationException($"ThumbnailRenderer requires ratio-native artwork. Source ratio {sourceRatio:0.###} does not match target ratio {targetRatio:0.###}.");
        image.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(input.PromptContract.Platform.Width, input.PromptContract.Platform.Height), Mode = ResizeMode.Max }));

        var w = image.Width;
        var h = image.Height;
        var safe = SafeArea(w, h, input.CompositionProfile);
        var layouts = new List<ThumbnailRenderComponentLayout>();
        var fontFamily = ResolveFont(input.Theme.FontFamily);
        var titleFont = fontFamily.CreateFont(MathF.Max(42, w * 0.058f), FontStyle.Bold);
        var subtitleFont = fontFamily.CreateFont(MathF.Max(26, w * 0.027f), FontStyle.Regular);
        var bodyFont = fontFamily.CreateFont(MathF.Max(24, w * 0.022f), FontStyle.Regular);
        var ctaFont = fontFamily.CreateFont(MathF.Max(24, w * 0.024f), FontStyle.Bold);
        var title = FirstNonEmpty(input.PromptContract.Display.LocalizedTitle, input.PromptContract.Display.DisplayTitle);
        var subtitle = FirstNonEmpty(input.Subtitle, input.PromptContract.Visual.EducationalIntent, input.PromptContract.EventIdentity.EventAction);
        var observation = FirstNonEmpty(input.PromptContract.Observation.BestViewingWindow, input.PromptContract.Observation.ObservationWindow);
        var equipment = ResolveEquipment(input.PromptContract);
        var safety = ResolveSafety(input.PromptContract);
        var cta = FirstNonEmpty(input.Cta, ResolveCta(input.PromptContract));

        image.Mutate(ctx =>
        {
            RenderBackground(ctx, w, h, input.Theme);
            var titleBox = new RectangleF(safe.X, safe.Y, safe.Width * 0.56f, h * 0.22f);
            DrawText(ctx, title, titleFont, titleBox, Color.ParseHex(input.Theme.PrimaryTextColor));
            layouts.Add(ToLayout("Title", "TypographyRenderer", title, titleBox, 20, safe));
            var subtitleBox = new RectangleF(titleBox.X, titleBox.Bottom + h * 0.012f, titleBox.Width, h * 0.075f);
            DrawText(ctx, subtitle, subtitleFont, subtitleBox, Color.ParseHex(input.Theme.SecondaryTextColor));
            layouts.Add(ToLayout("Subtitle", "LocalizationRenderer", subtitle, subtitleBox, 21, safe));

            if (input.PlatformStorytellingStrategy.ObservationCardEnabled || input.PlatformStorytellingStrategy.MaximumInformationItems > 0)
            {
                var card = CardBounds(w, h, safe, input.CompositionProfile);
                RenderCard(ctx, card, input.Theme);
                var lines = new[] { $"OBSERVE  {observation}", $"DIRECTION  {input.PromptContract.Observation.Direction}", $"EQUIPMENT  {equipment}", $"SAFETY  {safety}" };
                for (var i = 0; i < lines.Length; i++)
                    DrawText(ctx, lines[i], bodyFont, new RectangleF(card.X + 28, card.Y + 24 + i * bodyFont.Size * 1.45f, card.Width - 56, bodyFont.Size * 1.35f), i == 0 ? Color.ParseHex(input.Theme.AccentColor) : Color.White);
                layouts.Add(ToLayout("Observation", "ObservationCardRenderer", observation, card, 30, safe));
                layouts.Add(ToLayout("Equipment", "IconRenderer", equipment, new RectangleF(card.X + 28, card.Y + 24 + bodyFont.Size * 2.9f, card.Width - 56, bodyFont.Size * 1.35f), 31, safe));
                layouts.Add(ToLayout("Safety", "SafeAreaRenderer", safety, new RectangleF(card.X + 28, card.Y + 24 + bodyFont.Size * 4.35f, card.Width - 56, bodyFont.Size * 1.35f), 32, safe));
            }

            var ctaBox = new RectangleF(safe.X, safe.Bottom - h * 0.09f, MathF.Min(safe.Width * 0.42f, w * 0.38f), h * 0.07f);
            RenderPill(ctx, ctaBox, input.Theme);
            DrawText(ctx, cta, ctaFont, ctaBox with { X = ctaBox.X + 22, Width = ctaBox.Width - 44 }, Color.ParseHex(input.Theme.BackgroundOverlayColor));
            layouts.Add(ToLayout("CTA", "CTARenderer", cta, ctaBox, 40, safe));
            var brandBox = new RectangleF(safe.Right - w * 0.18f, safe.Bottom - h * 0.055f, w * 0.18f, h * 0.04f);
            DrawText(ctx, input.Theme.BrandText, bodyFont, brandBox, Color.White.WithAlpha(0.78f));
            layouts.Add(ToLayout("Brand", "BrandRenderer", input.Theme.BrandText, brandBox, 50, safe));
        });

        await image.SaveAsync(input.OutputImagePath, new PngEncoder(), cancellationToken);
        await File.WriteAllTextAsync(input.OutputLayoutJsonPath, JsonSerializer.Serialize(new { theme = input.Theme.Name, compositionProfile = input.CompositionProfile.Name, strategy = input.PlatformStorytellingStrategy.Name, components = layouts }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return new ThumbnailRenderResult(input.OutputImagePath, input.OutputLayoutJsonPath, layouts);
    }

    private static RectangleF SafeArea(int w, int h, CompositionProfile p) => p.AspectRatio == "9:16" ? new(w * .08f, h * .09f, w * .84f, h * .82f) : new(w * .055f, h * .07f, w * .89f, h * .84f);
    private static RectangleF CardBounds(int w, int h, RectangleF safe, CompositionProfile p) => p.AspectRatio == "9:16" ? new(safe.X, safe.Y + h * .48f, safe.Width, h * .18f) : new(safe.X, safe.Y + h * .36f, w * .43f, h * .25f);
    private static FontFamily ResolveFont(string name) => SystemFonts.TryGet(name, out var f) ? f : SystemFonts.Collection.Families.First();
    private static void RenderBackground(IImageProcessingContext ctx, int w, int h, ThumbnailTheme t) => ctx.Fill(Color.ParseHex(t.BackgroundOverlayColor).WithAlpha(t.BackgroundOverlayOpacity), new RectangleF(0, 0, w, h));
    private static void RenderCard(IImageProcessingContext ctx, RectangleF r, ThumbnailTheme t) { ctx.Fill(Color.ParseHex(t.CardFillColor).WithAlpha(t.CardFillOpacity), r); ctx.Draw(Color.ParseHex(t.CardStrokeColor).WithAlpha(.86f), Math.Max(2, r.Width * .006f), r); }
    private static void RenderPill(IImageProcessingContext ctx, RectangleF r, ThumbnailTheme t) => ctx.Fill(Color.ParseHex(t.AccentColor), r);
    private static void DrawText(IImageProcessingContext ctx, string text, Font font, RectangleF bounds, Color color) { var opt = new RichTextOptions(font) { Origin = new PointF(bounds.X, bounds.Y), WrappingLength = bounds.Width }; ctx.DrawText(new RichTextOptions(opt) { Origin = new PointF(bounds.X + 3, bounds.Y + 3) }, text, Color.Black.WithAlpha(.68f)); ctx.DrawText(opt, text, color); }
    private static ThumbnailRenderComponentLayout ToLayout(string component, string renderer, string text, RectangleF r, int z, RectangleF safe) => new(component, renderer, text, r.X, r.Y, r.Width, r.Height, z, $"x={safe.X:0},y={safe.Y:0},w={safe.Width:0},h={safe.Height:0}");
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    private static string ResolveEquipment(ThumbnailPromptContract c) => c.Prompt.PositivePrompt.Contains("solar", StringComparison.OrdinalIgnoreCase) ? "Certified solar filter" : c.Prompt.PositivePrompt.Contains("meteor", StringComparison.OrdinalIgnoreCase) ? "Eyes + dark sky" : "Naked eye / binoculars";
    private static string ResolveSafety(ThumbnailPromptContract c) => c.Validation.ScientificRules.Concat(c.Validation.PlatformRules).FirstOrDefault(r => r.Contains("safety", StringComparison.OrdinalIgnoreCase) || r.Contains("safe", StringComparison.OrdinalIgnoreCase)) ?? "Check local conditions";
    private static string ResolveCta(ThumbnailPromptContract c) => c.EventIdentity.EventFamily.Contains("Eclipse", StringComparison.OrdinalIgnoreCase) ? "Check Safety" : "Watch Tonight";
}
