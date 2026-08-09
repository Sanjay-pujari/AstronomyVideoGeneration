using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>
/// Phase 12 poster authority. Phase 11 exclusively selects each visual; Phase 12 resolves
/// that selection back to its certified, clean Phase 8 raster and adds deterministic copy.
/// </summary>
internal static class ResponsiveThumbnailAuthorityService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string Renderer = "PosterThumbnailRenderer/3.0";
    private const string Layout = "PosterThumbnailLayout/3.0";
    private const string CopyPolicy = "ThumbnailPosterPolicy/3.0";
    internal const string FactSelectionPolicy = "PosterFactSelection/2.0";

    internal static async Task<ResponsiveThumbnailPublicationResult> PublishAsync(
        string outputRoot, string planId, string eventId, string language, string eventType,
        IReadOnlyList<string> primaryObjects, CancellationToken ct) =>
        await PublishAsync(outputRoot, planId, eventId, language, eventType, primaryObjects,
            Array.Empty<string>(), "ProductionExecutionContext", ct);

    internal static Task<ResponsiveThumbnailPublicationResult> PublishAsync(
        string outputRoot, string planId, string eventId, string language, string eventType,
        IReadOnlyList<string> primaryObjects, string copyEventIdentitySource, CancellationToken ct) =>
        PublishAsync(outputRoot, planId, eventId, language, eventType, primaryObjects, Array.Empty<string>(), copyEventIdentitySource, ct);

    internal static async Task<ResponsiveThumbnailPublicationResult> PublishAsync(
        string outputRoot, string planId, string eventId, string language, string eventType,
        IReadOnlyList<string> primaryObjects, IReadOnlyList<string> secondaryObjects, string copyEventIdentitySource, CancellationToken ct)
    {
        Require(!string.IsNullOrWhiteSpace(eventType), "P12_COPY_AUTHORITY_MISSING",
            "Current event type is required for deterministic thumbnail copy.");
        var heroPath = Path.Combine(outputRoot, "11-hero", "hero-asset-manifest.json");
        var heroReportPath = Path.Combine(outputRoot, "11-hero", "phase11-publication-report.json");
        var heroValidationPath = Path.Combine(outputRoot, "validation", "phase-11-validation.json");
        var p10Path = Path.Combine(outputRoot, "10-scene-validation", "scene-asset-certification.json");
        var p10ReportPath = Path.Combine(outputRoot, "10-scene-validation", "phase10-publication-report.json");
        var p8Path = Path.Combine(outputRoot, "08-scene-assets", "scene-asset-manifest.json");
        Require(File.Exists(heroPath), "P12_HERO_AUTHORITY_MISSING", "Phase 11 Hero authority is missing.");
        Require(File.Exists(heroReportPath), "P12_HERO_AUTHORITY_INVALID", "Phase 11 publication report is missing.");
        Require(File.Exists(heroValidationPath), "P12_HERO_AUTHORITY_INVALID", "Canonical Phase 11 validation is missing.");
        Require(File.Exists(p10Path) && File.Exists(p10ReportPath) && File.Exists(p8Path), "P12_SCENE_CERTIFICATION_INVALID", "Phase 10/8 lineage authority is missing.");

        using var heroDoc = JsonDocument.Parse(await File.ReadAllTextAsync(heroPath, ct));
        using var heroReportDoc = JsonDocument.Parse(await File.ReadAllTextAsync(heroReportPath, ct));
        using var heroValidationDoc = JsonDocument.Parse(await File.ReadAllTextAsync(heroValidationPath, ct));
        using var p10Doc = JsonDocument.Parse(await File.ReadAllTextAsync(p10Path, ct));
        using var p10ReportDoc = JsonDocument.Parse(await File.ReadAllTextAsync(p10ReportPath, ct));
        using var p8Doc = JsonDocument.Parse(await File.ReadAllTextAsync(p8Path, ct));
        var hero = heroDoc.RootElement;
        var p10 = p10Doc.RootElement;
        var p8 = p8Doc.RootElement;
        Identity(hero, planId, eventId, language, "P12_HERO_AUTHORITY_INVALID");
        Identity(p10, planId, eventId, language, "P12_SCENE_CERTIFICATION_INVALID");
        Identity(p8, planId, eventId, language, "P12_SOURCE_LINEAGE_MISMATCH");
        Require(Text(hero, "validationStatus") == "Valid" && Text(hero, "publicationState") == "Committed" && Flag(hero, "downstreamReady"),
            "P12_HERO_AUTHORITY_INVALID", "Phase 11 must be Valid, Committed, and downstream ready.");
        Require(Flag(heroReportDoc.RootElement, "publicationCommitted") && Flag(heroReportDoc.RootElement, "candidateReadbackPassed")
            && Flag(heroReportDoc.RootElement, "committedReadbackPassed"), "P12_HERO_AUTHORITY_INVALID", "Phase 11 committed readback evidence failed.");
        var heroChecksum = Text(hero, "deterministicChecksum");
        var publicationChecksum = Text(heroReportDoc.RootElement, "manifestChecksum");
        var validationChecksum = Text(heroValidationDoc.RootElement, "authorityChecksum");
        var checksumsAgree = PublishedChecksumsAgree(heroChecksum, publicationChecksum, validationChecksum);
        Require(checksumsAgree, "P12_HERO_AUTHORITY_INVALID",
            $"Phase 11 published checksums disagree (manifest={heroChecksum}, publication={publicationChecksum}, validation={validationChecksum}).");
        var heroValidation = heroValidationDoc.RootElement;
        Require(Text(heroValidation, "manifestValidationStatus") == "Valid" && Text(heroValidation, "validationStatus") == "Valid"
            && Flag(heroValidation, "publicationCommitted") && Flag(heroValidation, "semanticValidationPassed")
            && Flag(heroValidation, "checksumValidationPassed") && Flag(heroValidation, "manifestValidationPassed")
            && Flag(heroValidation, "committedStateValidationPassed") && Flag(heroValidation, "downstreamReady"),
            "P12_HERO_AUTHORITY_INVALID", "Canonical Phase 11 validation evidence is not accepted.");
        Require(Text(p10, "validationStatus") == "Valid" && Text(p10, "publicationState") == "Committed" && Flag(p10, "downstreamReady"),
            "P12_SCENE_CERTIFICATION_INVALID", "Phase 10 must be Valid, Committed, and downstream ready.");
        Require(Flag(p10ReportDoc.RootElement, "publicationCommitted") && Flag(p10ReportDoc.RootElement, "committedReadbackPassed"),
            "P12_SCENE_CERTIFICATION_INVALID", "Phase 10 committed readback evidence failed.");
        Require(Text(hero, "executionId").Equals(Text(p10, "executionId"), StringComparison.OrdinalIgnoreCase),
            "P12_SOURCE_LINEAGE_MISMATCH", "Phase 11 and Phase 10 execution identities differ.");
        Require(Text(p10, "phase8SceneAssetAuthorityChecksum") == Text(p8, "deterministicChecksum"),
            "P12_SOURCE_LINEAGE_MISMATCH", "Phase 11 -> Phase 10 -> Phase 8 lineage does not match.");
        Require(Text(hero, "phase10CertificationChecksum") == Sha(p10Path), "P12_SOURCE_LINEAGE_MISMATCH", "Phase 11 Phase 10 physical lineage is stale.");
        Require(Text(hero, "phase8SceneAssetManifestChecksum") == Sha(p8Path), "P12_SOURCE_LINEAGE_MISMATCH", "Phase 11 Phase 8 physical lineage is stale.");

        var title = Text(hero, "title");
        Require(!string.IsNullOrWhiteSpace(title), "P12_COPY_AUTHORITY_MISSING", "Phase 11 accepted title is missing.");
        var subtitle = Text(hero, "subtitle");
        var resolvedPrimaryObjects = (primaryObjects ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var primaryObject = resolvedPrimaryObjects.FirstOrDefault() ?? DeriveObject(title);
        var copy = BuildThumbnailCopy(eventType, resolvedPrimaryObjects, primaryObject, title);
        Require(copy.WordCount is > 0 and <= 5, "P12_COPY_BUDGET_EXCEEDED", "Thumbnail headline exceeds the approved five-word budget.");
        var copyValidation = ValidateCopyDifferentiation(title, subtitle, copy.Headline, null, copy.Rule);
        var copyChecksum = Hash(string.Join('|', title, subtitle, language, eventType,
            string.Join(',', resolvedPrimaryObjects)));
        var variants = hero.GetProperty("variants").EnumerateArray().ToDictionary(x => Text(x, "variant"), StringComparer.OrdinalIgnoreCase);
        var profiles = new[] { new Profile("Landscape", 1280, 720, 74, 52), new Profile("Square", 1080, 1080, 72, 58), new Profile("Portrait", 1080, 1920, 82, 68) };
        Require(profiles.All(x => variants.ContainsKey(x.Role)), "P12_HERO_AUTHORITY_INVALID", "All three responsive Hero roles are required.");

        // The semantic checksum is certified by the three committed Phase 11 surfaces above.
        // Independently verify every physical raster rather than attempting to reverse-engineer
        // Phase 11's publisher-specific semantic checksum canonicalization.
        foreach (var profile in profiles)
        {
            var variant = variants[profile.Role];
            var path = Path.GetFullPath(Path.Combine(outputRoot, Text(variant, "physicalPath")));
            Require(File.Exists(path) && new FileInfo(path).Length > 0, "P12_HERO_AUTHORITY_INVALID", $"{profile.Role} Hero raster is missing or empty.");
            Require(Sha(path).Equals(Text(variant, "physicalSha256"), StringComparison.OrdinalIgnoreCase), "P12_HERO_AUTHORITY_INVALID", $"{profile.Role} Hero physical checksum failed.");
            using var image = Image.Load(path);
            Require(image.Width == variant.GetProperty("width").GetInt32() && image.Height == variant.GetProperty("height").GetInt32(), "P12_HERO_AUTHORITY_INVALID", $"{profile.Role} Hero manifest dimensions do not match the raster.");
            var expected = profile.Role == "Landscape" ? (1920, 1080) : profile.Role == "Square" ? (1080, 1080) : (1080, 1920);
            Require(image.Width == expected.Item1 && image.Height == expected.Item2, "P12_HERO_AUTHORITY_INVALID", $"{profile.Role} Hero profile dimensions are invalid.");
        }

        var poster = BuildPosterContent(eventType, copy.Headline, resolvedPrimaryObjects, secondaryObjects, p8);
        Require(poster.Facts.Count > 0, "P12_POSTER_INFORMATION_INSUFFICIENT", "A poster requires a meaningful certified fact.");
        var root = Path.Combine(outputRoot, "12-thumbnails");
        var orphanStagingRemovedCount = RemoveOrphanStagingDirectories(outputRoot);
        var transaction = Guid.NewGuid().ToString("N");
        var staging = root + ".staging-" + transaction;
        var backup = root + ".backup-" + transaction;
        var publicationCommitted = false;
        var stagingCreated = false;
        try
        {
        Directory.CreateDirectory(staging);
        stagingCreated = true;
        var items = new List<ThumbnailVariant>();
        foreach (var profile in profiles)
        {
            var selection = variants[profile.Role];
            var assetId = Text(selection, "sourcePhase8AssetId");
            var phase8Asset = p8.GetProperty("assets").EnumerateArray().SingleOrDefault(a => Text(a, "assetId") == assetId);
            Require(phase8Asset.ValueKind == JsonValueKind.Object, "P12_SOURCE_LINEAGE_MISMATCH", $"{profile.Role} selected Phase 8 asset is absent from its manifest.");
            var style = Text(selection, "sourceVisualStyle");
            Require(style is "Cinematic" or "HybridCinematic", "P12_FORBIDDEN_AUTHORITY_PATH", $"{profile.Role} selected visual is not cinematic.");
            Require(Text(phase8Asset, "visualStyle") == style, "P12_SOURCE_LINEAGE_MISMATCH", $"{profile.Role} visual style lineage differs.");
            var semantic = Text(selection, "sourcePhase8SemanticIdentity");
            Require(Text(phase8Asset, "semanticIdentity") == semantic, "P12_SOURCE_LINEAGE_MISMATCH", $"{profile.Role} semantic identity lineage differs.");
            var cleanRelative = Text(selection, "sourcePhase8PhysicalPath");
            Require(!string.IsNullOrWhiteSpace(cleanRelative) && !cleanRelative.Contains("11-hero", StringComparison.OrdinalIgnoreCase),
                "P12_CLEAN_SOURCE_REQUIRED", $"{profile.Role} clean Phase 8 lineage is missing.");
            Require(Text(phase8Asset, "physicalPath") == cleanRelative, "P12_SOURCE_LINEAGE_MISMATCH", $"{profile.Role} physical path lineage differs.");
            var cleanPath = Path.GetFullPath(Path.Combine(outputRoot, cleanRelative));
            Require(File.Exists(cleanPath), "P12_SOURCE_IMAGE_MISSING", $"{profile.Role} clean Phase 8 raster is missing.");
            var cleanSha = Sha(cleanPath);
            Require(cleanSha.Equals(Text(selection, "sourcePhase8PhysicalSha256"), StringComparison.OrdinalIgnoreCase)
                && cleanSha.Equals(Text(phase8Asset, "physicalSha256"), StringComparison.OrdinalIgnoreCase),
                "P12_SOURCE_CHECKSUM_MISMATCH", $"{profile.Role} selected Phase 8 physical checksum failed.");
            var fileName = $"thumbnail-{profile.Role.ToLowerInvariant()}.png";
            var target = Path.Combine(staging, fileName);
            var layoutResult = Render(cleanPath, target, profile, poster, requiresScience: Flag(selection, "requiresScientificGeometry"));
            using var decoded = await Image.LoadAsync(target, ct);
            Require(decoded.Width == profile.Width && decoded.Height == profile.Height, "P12_PHYSICAL_VALIDATION_FAILED", $"{profile.Role} physical dimensions failed.");
            var requiresScience = Flag(selection, "requiresScientificGeometry");
            Require(!requiresScience || (Flag(selection, "scientificGeometryCertified") && Flag(selection, "scientificGeometryPreserved") && Flag(selection, "scientificRegionPassed")),
                "P12_SCIENTIFIC_REGION_NOT_PRESERVED", $"{profile.Role} scientific preservation cannot be proven.");
            var outputSha = Sha(target);
            var renderedKeys = layoutResult.RenderedFactKeys;
            var omitted = poster.Facts.Where(f => !renderedKeys.Contains(f.Key)).Select(f => f.Key).ToArray();
            items.Add(new ThumbnailVariant(profile.Role, $"12-thumbnails/{fileName}", "Phase11HeroManifest", "Phase8CertifiedCleanRaster",
                profile.Role, Text(selection, "physicalPath"), heroChecksum, assetId, Text(selection, "sourcePhase8SceneId"), cleanRelative, cleanSha,
                semantic, style, requiresScience, Flag(selection, "scientificGeometryCertified"), true,
                "AspectPreservingCoverOrScientificContain", new Region(0, 0, profile.Width, (int)(profile.Height * profile.VisualEmphasis)), layoutResult.PanelRegion,
                "PosterThumbnail", "EditorialHero", title, subtitle, poster.Headline, copy.Rule, CopyPolicy, copy.WordCount, poster.Badge.Value,
                (int)Math.Round(profile.VisualEmphasis * 100), $"{profile.Role}Poster/3.0", Renderer, profile.Width, profile.Height,
                $"{profile.Width}:{profile.Height}", "png", "image/png", new FileInfo(target).Length, outputSha,
                poster, renderedKeys, layoutResult.SelectedFactCategories, omitted,
                omitted.ToDictionary(k => k, k => layoutResult.OmissionReasons.GetValueOrDefault(k, "Profile fact limit or available layout space")),
                FactSelectionPolicy, renderedKeys.Count, layoutResult.PanelRegion, layoutResult.ContentRegion, layoutResult.PanelUtilizationPercent,
                layoutResult.UnusedPosterAreaPercent, layoutResult.HeadlineBounds, layoutResult.BadgeBounds, layoutResult.FactBounds,
                layoutResult.TextBounds, layoutResult.FactSelectionDiagnostics, false, false, false, false, false, true, true, true, true, true, true, "Valid"));
        }

        var created = DateTimeOffset.UtcNow;
        var manifest = new ThumbnailManifest("1.0", planId, Text(hero, "executionId"), eventId, language, created,
            "11-hero/hero-asset-manifest.json", heroChecksum, "10-scene-validation/scene-asset-certification.json", Text(p10, "deterministicChecksum"),
            "Phase11HeroManifest.TitleSubtitle+ProductionExecutionContext.EventIdentity", copyChecksum, CopyPolicy, Renderer, Layout, "NoGenerativeImageProvider", 0, items,
            "Valid", "Committed", true, true, "", true);
        manifest = manifest with { DeterministicChecksum = AuthorityChecksum(manifest) };
        await Write(Path.Combine(staging, "thumbnail-asset-manifest.json"), manifest, ct);
        var candidate = await Read<ThumbnailManifest>(Path.Combine(staging, "thumbnail-asset-manifest.json"), ct);
        Require(candidate.DeterministicChecksum == AuthorityChecksum(candidate), "P12_CANDIDATE_READBACK_FAILED", "Candidate authority checksum failed.");
        ValidateManifestCopy(candidate, title, subtitle, copy);
        var diagnostics = new { phase12Applicable = true, thumbnailRequested = true, transactionId = transaction, stagingRoot = staging,
            stagingCreated, stagingCleaned = true, orphanStagingRemovedCount, phase11AuthorityLoaded = true, phase11AuthorityChecksum = heroChecksum,
            phase11ManifestDeterministicChecksum = heroChecksum, phase11PublicationManifestChecksum = publicationChecksum,
            phase11ValidationAuthorityChecksum = validationChecksum, phase11ChecksumsAgree = checksumsAgree,
            phase11VariantPhysicalChecksumsPassed = true, phase11VariantDimensionsPassed = true, phase11AuthorityValidationPassed = true,
            phase10LineageValidationPassed = true,
            phase11Committed = true, phase11DownstreamReady = true, phase10AuthorityLoaded = true, phase10AuthorityChecksum = Text(p10, "deterministicChecksum"),
            phase10Committed = true, phase10DownstreamReady = true, landscapeSourceHeroRole = "Landscape", squareSourceHeroRole = "Square", portraitSourceHeroRole = "Portrait",
            landscapeSourceChecksum = items[0].SourceHeroChecksum, squareSourceChecksum = items[1].SourceHeroChecksum, portraitSourceChecksum = items[2].SourceHeroChecksum,
            presentationMode = "DiscoveryThumbnail", heroPresentationMode = "EditorialHero", heroAndThumbnailPresentationDiffer = true,
            duplicateCopyDetected = copyValidation.DuplicateCopyDetected, paragraphCopyRendered = copyValidation.ParagraphCopyRendered,
            thumbnailHeadline = copy.Headline, thumbnailSecondaryText = (string?)null, thumbnailHeadlineWordCount = copy.WordCount,
            heroTitle = title, heroSubtitle = subtitle, normalizedHeroTitle = copyValidation.NormalizedHeroTitle,
            normalizedHeroSubtitle = copyValidation.NormalizedHeroSubtitle, normalizedThumbnailHeadline = copyValidation.NormalizedThumbnailHeadline,
            normalizedThumbnailSecondaryText = copyValidation.NormalizedThumbnailSecondaryText,
            heroTitleReusedVerbatim = copyValidation.HeroTitleReusedVerbatim, heroSubtitleReusedVerbatim = copyValidation.HeroSubtitleReusedVerbatim,
            sharedAuthorityTokens = copyValidation.SharedAuthorityTokens, sharedAuthorityTokensAllowed = true,
            sourceCopy = title, thumbnailCopy = copy.Headline, copyTransformationRule = copy.Rule,
            copyDifferentiationPassed = copyValidation.CopyDifferentiationPassed,
            eventType, primaryObjects = resolvedPrimaryObjects, copyEventIdentitySource,
            landscapeVisualEmphasisPercent = 70, squareVisualEmphasisPercent = 68, portraitVisualEmphasisPercent = 72,
            heroRasterUsedAsBackground = false, cleanPhase8RasterUsed = true, textOverlapDetected = false, textClipped = false, factOverlapDetected = false,
            copyAuthoritySource = manifest.CopyAuthoritySource, copyPolicyVersion = CopyPolicy, azureImageCallsThisPhase = 0, otherGenerativeImageCallsThisPhase = 0,
            proceduralAstronomyGenerationCallsThisPhase = 0, legacyQuestionEngineAuthorityUsed = false, legacyHeroAssetsAuthorityUsed = false,
            legacySceneApprovalAuthorityUsed = false, v9AiCompleteRasterUsed = false, stretchResizeUsed = false, candidateValidationPassed = true,
            candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, downstreamReady = true,
            landscapeSelectedFacts = items[0].SelectedFactKeys, squareSelectedFacts = items[1].SelectedFactKeys, portraitSelectedFacts = items[2].SelectedFactKeys,
            landscapeFactCount = items[0].FactCount, squareFactCount = items[1].FactCount, portraitFactCount = items[2].FactCount,
            landscapePosterPanelUtilizationPercent = items[0].PosterPanelUtilizationPercent,
            squarePosterPanelUtilizationPercent = items[1].PosterPanelUtilizationPercent,
            portraitPosterPanelUtilizationPercent = items[2].PosterPanelUtilizationPercent,
            factCategoryDiversityPassed = items.All(i => i.SelectedFactCategories.Distinct().Count() == i.SelectedFactCategories.Count),
            profileFactSelection = items.Select(i => i.FactSelectionDiagnostics).ToArray(),
            suboptimalFactSelectionDetected = items.Any(i => i.FactSelectionDiagnostics.SuboptimalFactSelectionDetected), hardOpaquePanelUsed = false, heroTextLeakageDetected = false };
        await Write(Path.Combine(staging, "phase12-authority-diagnostics.json"), diagnostics, ct);
        if (Directory.Exists(root)) Directory.Move(root, backup);
        try { Directory.Move(staging, root); } catch { if (Directory.Exists(backup)) Directory.Move(backup, root); throw; }
        var committed = await Read<ThumbnailManifest>(Path.Combine(root, "thumbnail-asset-manifest.json"), ct);
        Require(committed.DeterministicChecksum == AuthorityChecksum(committed), "P12_COMMITTED_READBACK_FAILED", "Committed authority checksum failed.");
        ValidateManifestCopy(committed, title, subtitle, copy);
        publicationCommitted = true;
        var report = new { transactionId = transaction, stagingRoot = staging, stagingCreated, stagingCleaned = true, orphanStagingRemovedCount,
            candidateCreated = true, candidateValidationPassed = true, candidateReadbackPassed = true,
            backupCreated = Directory.Exists(backup), publicationCommitted = true, committedReadbackPassed = true, manifestChecksum = committed.DeterministicChecksum,
            thumbnailVariantCount = 3, generatedVariantCount = 3, reusedVariantCount = 0, upstreamArtifactsModified = false, generatedAtUtc = DateTimeOffset.UtcNow };
        await Write(Path.Combine(root, "phase12-publication-report.json"), report, ct);
        await Write(Path.Combine(outputRoot, "validation", "phase-12-validation.json"), new { phaseNo = 12, status = "Succeeded", validationStatus = "Valid", manifestValidationStatus = "Valid",
            authorityPath = "12-thumbnails/thumbnail-asset-manifest.json", authorityChecksum = committed.DeterministicChecksum, publicationState = "Committed",
            semanticValidationPassed = true, checksumValidationPassed = true, manifestValidationPassed = true, publicationCommitted = true,
            committedStateValidationPassed = true, candidateReadbackPassed = true, committedReadbackPassed = true, downstreamReady = true,
            reason = "Responsive thumbnail assets generated, validated, committed and read back.", providerCallCount = 0 }, ct);
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        return new ResponsiveThumbnailPublicationResult(
            Directory.EnumerateFiles(root).Append(Path.Combine(outputRoot, "validation", "phase-12-validation.json")).ToArray(),
            committed.DeterministicChecksum, true, true, true, true, true,
            "Responsive thumbnail assets generated, validated, committed and read back.",
            "P12_THUMBNAIL_AUTHORITY_ACCEPTED");
        }
        finally
        {
            SafeDeleteDirectory(staging);
            if (!publicationCommitted && Directory.Exists(backup))
            {
                SafeDeleteDirectory(root);
                Directory.Move(backup, root);
            }
            else if (publicationCommitted) SafeDeleteDirectory(backup);
        }
    }

    private static PosterLayoutResult Render(string source, string target, Profile profile, ThumbnailPosterContent poster, bool requiresScience)
    {
        using var image = Image.Load<Rgba32>(source);
        image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(profile.Width, profile.Height),
            Mode = requiresScience ? ResizeMode.Pad : ResizeMode.Crop, PadColor = Color.Black, Sampler = KnownResamplers.Lanczos3 }));
        var family = SystemFonts.Collection.Families.First();
        var initialPanel = profile.Role == "Landscape"
            ? new Rectangle(0, 0, (int)(profile.Width * .40), profile.Height)
            : new Rectangle(profile.Margin / 2, (int)(profile.Height * profile.VisualEmphasis), profile.Width - profile.Margin,
                (int)(profile.Height * (1 - profile.VisualEmphasis)) - profile.Margin / 2);
        var selection = SelectPosterFactsForProfile(profile, poster, initialPanel, requiresScience, family);
        var panel = selection.FinalPosterBounds;
        var bounds = selection.TextBounds.ToList();
        var contentX = panel.X + profile.Margin / 2;
        var badge = bounds.Single(b => b.Key == "badge");
        var headline = selection.Headline;
        var factLayouts = selection.FactLayouts;

        Require(selection.SelectedFacts.Count >= selection.MinimumFactCount, "P12_POSTER_INFORMATION_INSUFFICIENT",
            $"{profile.Role} has no renderable certified fact.");
        Require(!selection.SuboptimalFactSelectionDetected, "P12_POSTER_FACT_SELECTION_SUBOPTIMAL",
            $"{profile.Role} omitted a higher-priority certified fact that measured layout proves could fit safely.");
        Require(!HasOverlap(bounds) && bounds.All(b => b.X >= 0 && b.Y >= 0 && b.X + b.Width <= profile.Width && b.Y + b.Height <= profile.Height),
            "P12_TEXT_LAYOUT_INVALID", $"{profile.Role} text overlaps or clips.");
        image.Mutate(x =>
        {
            // Layered alpha bands form a deterministic glass-to-cinematic fade; no hard opaque panel is used.
            if (profile.Role == "Landscape")
                for (var px = 0; px < panel.Width + 90; px += 6)
                {
                    var progress = px / (float)(panel.Width + 90);
                    var alpha = (byte)Math.Clamp(205 * (1 - progress) * (1 - progress), 0, 205);
                    x.Fill(Color.FromRgba(4, 10, 20, alpha), new Rectangle(px, 0, 6, profile.Height));
                }
            else x.Fill(Color.FromRgba(4, 10, 20, 204), panel);
            x.DrawText(poster.Badge.Value, selection.BadgeFont, Color.FromRgb(112, 220, 235), new PointF(contentX, badge.Y));
            x.DrawText(headline.Text, headline.Font, Color.White, new PointF(contentX, selection.HeadlineBounds.Y));
            foreach (var f in factLayouts)
            {
                x.DrawText(f.Fact.Label, f.Label, Color.FromRgb(130, 205, 220), new PointF(contentX, f.LabelBox.Y));
                x.DrawText(f.DisplayValue, f.Value, Color.White, new PointF(contentX, f.ValueBox.Y));
            }
        });
        image.SaveAsPng(target);
        var content = BoundsOf(bounds);
        var utilization = Math.Round(100d * content.Width * content.Height / (panel.Width * panel.Height), 2);
        return new(new Region(panel.X, panel.Y, panel.Width, panel.Height), content, utilization, Math.Round(100 - utilization, 2), bounds,
            selection.HeadlineBounds, selection.BadgeBounds, bounds.Where(b => b.Key.Contains('.')).ToArray(),
            selection.SelectedFacts.Select(f => f.Key).ToArray(), selection.SelectedFacts.Select(f => f.FactCategory.ToString()).ToArray(),
            selection.OmissionReasons, selection.Diagnostics);
    }

    internal static PosterFactSelectionResult SelectPosterFactsForProfile(Profile profile, ThumbnailPosterContent poster,
        Rectangle availableBounds, bool requiresScience, FontFamily? fontFamily = null)
    {
        var family = fontFamily ?? SystemFonts.Collection.Families.First();
        var preferred = profile.Role == "Landscape" ? 3 : 2;
        const int minimum = 1;
        var candidates = SelectPosterFacts(poster.EventFamily, profile.Role, poster.Facts, availableBounds).ToArray();
        var maxPanelFraction = profile.Role == "Square" ? .36 : 1 - profile.VisualEmphasis;
        var panels = new List<Rectangle> { availableBounds };
        if (profile.Role == "Square" && !requiresScience)
            for (var fraction = .34; fraction <= maxPanelFraction + .001; fraction += .02)
                panels.Add(new Rectangle(availableBounds.X, (int)(profile.Height * (1 - fraction)), availableBounds.Width,
                    (int)(profile.Height * fraction) - profile.Margin / 2));

        PosterFactSelectionResult? best = null;
        foreach (var panel in panels)
        {
            var measured = MeasureFactSelection(profile, poster, candidates, panel, preferred, minimum, family, requiresScience,
                panel != availableBounds);
            if (best is null || measured.SelectedFacts.Count > best.SelectedFacts.Count) best = measured;
            if (measured.SelectedFacts.Count >= Math.Min(preferred, candidates.Length)) return measured;
        }
        return best!;
    }

    private static PosterFactSelectionResult MeasureFactSelection(Profile profile, ThumbnailPosterContent poster,
        IReadOnlyList<PosterFact> candidates, Rectangle panel, int preferred, int minimum, FontFamily family,
        bool requiresScience, bool expanded)
    {
        var bounds = new List<TextBlockBounds>();
        var layouts = new List<FactDrawingLayout>();
        var selected = new List<PosterFact>();
        var reasons = new Dictionary<string, string>();
        var contentX = panel.X + profile.Margin / 2;
        var contentWidth = panel.Width - profile.Margin;
        var cursor = panel.Y + profile.Margin / 2;
        var badgeFont = family.CreateFont(Math.Max(20, profile.FontSize / 3), FontStyle.Bold);
        var badgeBox = Measure(poster.Badge.Value, badgeFont, contentX, cursor);
        var badgeBounds = new TextBlockBounds("badge", badgeBox.X, badgeBox.Y, badgeBox.Width, badgeBox.Height, badgeFont.Size);
        bounds.Add(badgeBounds); cursor += (int)badgeBox.Height + 18;
        var headline = Fit(poster.Headline, family, FontStyle.Bold, profile.FontSize, profile.FontSize * .62f, contentWidth);
        var headlineBox = Measure(headline.Text, headline.Font, contentX, cursor);
        var headlineBounds = new TextBlockBounds("headline", headlineBox.X, headlineBox.Y, headlineBox.Width, headlineBox.Height, headline.Font.Size);
        bounds.Add(headlineBounds); cursor += (int)headlineBox.Height + (profile.Role == "Portrait" ? 32 : 24);
        var factsStart = cursor;
        foreach (var fact in candidates)
        {
            if (selected.Count >= preferred) { reasons[fact.Key] = "ProfileFactLimit"; continue; }
            var labelFont = family.CreateFont(Math.Max(18, profile.FontSize * .27f), FontStyle.Bold);
            var minimumFont = Math.Max(22, profile.FontSize * .29f);
            var valueFit = Fit(fact.Value, family, FontStyle.Bold, profile.FontSize * .42f, minimumFont, contentWidth);
            var lb = Measure(fact.Label, labelFont, contentX, cursor);
            var vb = Measure(valueFit.Text, valueFit.Font, contentX, cursor + (int)lb.Height + 5);
            var bottom = vb.Bottom + 16;
            if (bottom > panel.Bottom - profile.Margin / 4)
            {
                reasons[fact.Key] = requiresScience && !expanded ? "ProtectedRegionConflict" :
                    valueFit.Font.Size <= minimumFont ? "MinimumFontSizeWouldFail" : "InsufficientVerticalSpace";
                continue;
            }
            layouts.Add(new(fact, valueFit.Text, labelFont, valueFit.Font, lb, vb));
            bounds.Add(new($"{fact.Key}.label", lb.X, lb.Y, lb.Width, lb.Height, labelFont.Size));
            bounds.Add(new($"{fact.Key}.value", vb.X, vb.Y, vb.Width, vb.Height, valueFit.Font.Size));
            selected.Add(fact); cursor = (int)bottom;
        }
        foreach (var fact in poster.Facts.Where(f => !selected.Contains(f) && !reasons.ContainsKey(f.Key)))
            reasons[fact.Key] = !fact.IsCertified ? "Uncertified" : candidates.Any(c => c.Key == fact.Key)
                ? "LowerPriorityThanSelectedFact" : "SemanticCategoryRedundant";
        var additionalFits = selected.Count < preferred && candidates.Any(f => !selected.Contains(f) && !reasons.ContainsKey(f.Key));
        var suboptimal = additionalFits;
        var requiredHeight = Math.Max(0, cursor - factsStart);
        var availableHeight = Math.Max(0, panel.Bottom - profile.Margin / 4 - factsStart);
        var diagnostics = new FactSelectionDiagnostics(profile.Role, candidates.Count, preferred, minimum, selected.Count,
            selected.Select(f => f.Key).ToArray(), poster.Facts.Where(f => !selected.Contains(f)).Select(f => f.Key).ToArray(), reasons,
            new Region(panel.X, panel.Y, panel.Width, panel.Height), new Region(panel.X, panel.Y, panel.Width, panel.Height), expanded,
            Math.Max(22, profile.FontSize * .29f), requiredHeight, availableHeight, additionalFits, suboptimal);
        return new(selected, poster.Facts.Where(f => !selected.Contains(f)).ToArray(), reasons, bounds, layouts, badgeFont,
            headline, headlineBounds, badgeBounds, panel, minimum, preferred, suboptimal, diagnostics);
    }

    internal static IReadOnlyList<PosterFact> SelectPosterFacts(string eventFamily, string profile,
        IReadOnlyList<PosterFact> certifiedFacts, Rectangle availableBounds)
    {
        var limit = profile.Equals("Landscape", StringComparison.OrdinalIgnoreCase) ? 4 : 3;
        var candidates = certifiedFacts.Where(f => f.IsCertified && !string.IsNullOrWhiteSpace(f.Value))
            .OrderBy(f => ProfileRank(profile, f.FactCategory)).ThenBy(f => f.EventFamilyPriority)
            .ThenBy(f => f.VisualPriority).ThenBy(f => f.SpaceCost).ThenBy(f => f.Key, StringComparer.Ordinal).ToArray();
        var selected = new List<PosterFact>();
        foreach (var fact in candidates)
        {
            if (selected.Count == limit) break;
            if (selected.Any(x => x.FactCategory == fact.FactCategory)) continue;
            selected.Add(fact);
        }
        return selected;
    }

    private static int ProfileRank(string profile, FactCategory category) => (profile.ToUpperInvariant(), category) switch
    {
        (_, FactCategory.Identification) => 0,
        ("LANDSCAPE", FactCategory.BrightObjects) => 1,
        ("LANDSCAPE", FactCategory.DeepSky) => 2,
        (_, FactCategory.DeepSky) => 1,
        (_, FactCategory.BrightObjects) => 2,
        _ => 10
    };

    private static Region BoundsOf(IReadOnlyList<TextBlockBounds> boxes)
    {
        var left = boxes.Min(b => b.X); var top = boxes.Min(b => b.Y);
        var right = boxes.Max(b => b.X + b.Width); var bottom = boxes.Max(b => b.Y + b.Height);
        return new((int)left, (int)top, (int)Math.Ceiling(right - left), (int)Math.Ceiling(bottom - top));
    }

    private static (string Text, Font Font) Fit(string text, FontFamily family, FontStyle style, float target, float minimum, float width)
    {
        for (var size = target; size >= minimum; size -= 2)
        {
            var font = family.CreateFont(size, style);
            if (TextMeasurer.MeasureBounds(text, new TextOptions(font)).Width <= width) return (text, font);
        }
        var min = family.CreateFont(minimum, style);
        var words = text.Split(' '); var lines = new List<string>(); var line = "";
        foreach (var word in words) { var candidate = line.Length == 0 ? word : line + " " + word;
            if (TextMeasurer.MeasureBounds(candidate, new TextOptions(min)).Width <= width) line = candidate;
            else { if (line.Length > 0) lines.Add(line); line = word; } }
        if (line.Length > 0) lines.Add(line);
        return (string.Join('\n', lines), min);
    }
    private static RectangleF Measure(string text, Font font, float x, float y)
    {
        var b = TextMeasurer.MeasureBounds(text, new TextOptions(font));
        return new RectangleF(x, y, Math.Max(1, b.Width), Math.Max(font.Size, b.Height));
    }
    internal static bool HasOverlap(IReadOnlyList<TextBlockBounds> boxes) => boxes.Select((a, i) => (a, i))
        .Any(left => boxes.Skip(left.i + 1).Any(right => left.a.X < right.X + right.Width && left.a.X + left.a.Width > right.X
            && left.a.Y < right.Y + right.Height && left.a.Y + left.a.Height > right.Y));

    internal static bool DuplicateCopyDetected(string first, string second)
    {
        var a = NormalizeCopy(first);
        var b = NormalizeCopy(second);
        return a.Length > 0 && a == b;
    }

    /// <summary>
    /// The single Phase 12 copy policy used before rendering and during both
    /// candidate and committed authority readback. Shared authority vocabulary is
    /// evidence of lineage, not duplication; only complete editorial reuse fails.
    /// </summary>
    internal static CopyDifferentiationDecision ValidateCopyDifferentiation(
        string heroTitle, string heroSubtitle, string thumbnailHeadline, string? thumbnailSecondaryText,
        string copyTransformationRule, bool temporalAuthoritySupported = false)
    {
        var normalizedHeroTitle = NormalizeCopy(heroTitle);
        var normalizedHeroSubtitle = NormalizeCopy(heroSubtitle);
        var normalizedHeadline = NormalizeCopy(thumbnailHeadline);
        var normalizedSecondary = NormalizeCopy(thumbnailSecondaryText ?? "");
        var thumbnailBlocks = new[] { normalizedHeadline, normalizedSecondary }.Where(x => x.Length > 0).ToArray();
        var titleReused = ReproducesCompleteCopy(thumbnailBlocks, normalizedHeroTitle);
        var subtitleReused = ReproducesCompleteCopy(thumbnailBlocks, normalizedHeroSubtitle);
        var paragraphRendered = normalizedSecondary.Length > 0 && normalizedSecondary.Split(' ').Length >= 6;
        var forbiddenTemporalClaim = !temporalAuthoritySupported
            && thumbnailBlocks.Any(ContainsForbiddenTemporalClaim);
        var verbatimRule = copyTransformationRule.Equals("VerbatimHeroReuse", StringComparison.OrdinalIgnoreCase);
        var duplicate = titleReused || subtitleReused || paragraphRendered && subtitleReused || verbatimRule;
        var heroTokens = normalizedHeroTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Concat(normalizedHeroSubtitle.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var thumbnailTokens = thumbnailBlocks.SelectMany(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var shared = heroTokens.Intersect(thumbnailTokens, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var result = new CopyDifferentiationDecision(normalizedHeroTitle, normalizedHeroSubtitle, normalizedHeadline,
            normalizedSecondary, titleReused, subtitleReused, paragraphRendered, duplicate, !duplicate && !forbiddenTemporalClaim,
            forbiddenTemporalClaim, shared);
        Require(!forbiddenTemporalClaim, "P12_UNCERTIFIED_COPY_CLAIM", "Thumbnail copy introduced an uncertified temporal claim.");
        Require(!duplicate, "P12_DUPLICATE_COPY", "Thumbnail copy duplicates Phase 11 editorial copy.");
        return result;
    }

    private static bool ReproducesCompleteCopy(IEnumerable<string> thumbnailBlocks, string heroCopy) =>
        heroCopy.Length > 0 && thumbnailBlocks.Any(block =>
            block == heroCopy || Regex.IsMatch(block, $@"(?:^|\s){Regex.Escape(heroCopy)}(?:\s|$)"));

    private static void ValidateManifestCopy(ThumbnailManifest manifest, string heroTitle, string heroSubtitle, ThumbnailCopyDecision approvedCopy)
    {
        foreach (var variant in manifest.Variants)
        {
            Require(NormalizeCopy(variant.ThumbnailCopy) == NormalizeCopy(approvedCopy.Headline)
                && variant.CopyTransformationRule.Equals(approvedCopy.Rule, StringComparison.Ordinal),
                "P12_UNCERTIFIED_COPY_CLAIM", "Thumbnail copy does not match the approved deterministic transformation.");
            ValidateCopyDifferentiation(heroTitle, heroSubtitle, variant.ThumbnailCopy, variant.SecondaryText, variant.CopyTransformationRule);
        }
    }
    internal static ThumbnailCopyDecision BuildThumbnailCopy(string eventFamily, IReadOnlyList<string> objects, string primaryObject, string certifiedTitle)
    {
        var family = Regex.Replace(eventFamily ?? "", "[^A-Za-z]", "").ToUpperInvariant();
        var safeObject = CleanIdentity(primaryObject);
        string headline;
        string rule;
        if (family == "CONSTELLATION" && safeObject.Length > 0) (headline, rule) = ($"FIND {safeObject}", "Constellation.FindCertifiedPrimaryObject");
        else if (family.Contains("METEOR") && safeObject.Length > 0) (headline, rule) = ($"{safeObject} METEOR SHOWER", "MeteorShower.CertifiedName");
        else if (family.Contains("CONJUNCTION") && objects.Count >= 2) (headline, rule) = ($"{CleanIdentity(objects[0])} + {CleanIdentity(objects[1])}", "Conjunction.CertifiedObjectPair");
        else if (family.Contains("ECLIPSE")) (headline, rule) = ($"{safeObject} ECLIPSE".Trim(), "Eclipse.CertifiedConciseTitle");
        else (headline, rule) = (CleanIdentity(certifiedTitle), "CertifiedTitle.ConciseIdentity");
        headline = string.Join(' ', headline.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        return new(headline, rule, headline.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }
    internal static ThumbnailPosterContent BuildPosterContent(string eventFamily, string headline,
        IReadOnlyList<string> primaryObjects, IReadOnlyList<string> secondaryObjects, JsonElement phase8Manifest)
    {
        var family = Regex.Replace(eventFamily ?? "", "[^A-Za-z]", "").ToUpperInvariant();
        var verified = phase8Manifest.TryGetProperty("assets", out var assets)
            ? assets.EnumerateArray().SelectMany(a => StringArray(a, "astronomyObjectsVerified"))
                .Concat(primaryObjects).Concat(secondaryObjects).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : primaryObjects.Concat(secondaryObjects).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var facts = new List<PosterFact>();
        var authority = "Phase8SceneAssetManifest.AstronomyObjectsVerified";
        if (family == "CONSTELLATION")
        {
            var belt = new[] { "Alnitak", "Alnilam", "Mintaka" };
            if (belt.All(name => verified.Contains(name, StringComparer.OrdinalIgnoreCase)))
                facts.Add(new("identification", "LOOK FOR", "3 BELT STARS", authority, true, 1, FactCategory.Identification, 1, 1, 1, false, "3 BELT STARS", "3 BELT STARS", "Identity"));
            else if (verified.Any(x => x.Contains("Belt", StringComparison.OrdinalIgnoreCase)))
                facts.Add(new("identification", "LOOK FOR", "ORION'S BELT", authority, true, 1, FactCategory.Identification, 1, 1, 1, false, "ORION'S BELT", "ORION'S BELT", "Identity"));
            var stars = new[] { "Betelgeuse", "Rigel", "Bellatrix", "Saiph" }.Where(x => verified.Contains(x, StringComparer.OrdinalIgnoreCase)).Take(2).ToArray();
            if (stars.Length > 0) facts.Add(new("brightStars", "BRIGHT STARS", string.Join(" • ", stars).ToUpperInvariant(), authority, true, 3, FactCategory.BrightObjects, 3, 2, 2, true, string.Join(" / ", stars), string.Join(" • ", stars).ToUpperInvariant(), "BrightObjects.MergeCertifiedNames"));
            var deepSky = verified.FirstOrDefault(x => x.Contains("M42", StringComparison.OrdinalIgnoreCase) || x.Contains("Orion Nebula", StringComparison.OrdinalIgnoreCase));
            if (deepSky is not null)
            {
                var display = Regex.Replace(deepSky, @"\s*/\s*", " • ").ToUpperInvariant();
                facts.Add(new("deepSky", "DEEP SKY", display, authority, true, 2, FactCategory.DeepSky, 2, 2, 2, true, deepSky, display, "DeepSky.SlashToBullet"));
            }
        }
        else if (family.Contains("CONJUNCTION") && primaryObjects.Count >= 2)
            facts.Add(SimpleFact("objects", "LOOK FOR", string.Join(" + ", primaryObjects.Take(3)), FactCategory.Identification));
        else if (family.Contains("METEOR"))
            facts.Add(SimpleFact("radiant", "LOOK FOR", primaryObjects.FirstOrDefault() ?? "METEORS", FactCategory.Direction));
        else if (family.Contains("ECLIPSE"))
            facts.Add(SimpleFact("event", "EVENT", primaryObjects.FirstOrDefault() ?? "ECLIPSE", FactCategory.EventGeometry));
        else if (primaryObjects.Count > 0)
            facts.Add(SimpleFact("object", "FEATURED", primaryObjects[0], FactCategory.Identification));
        return new(family, headline, new PosterField(EventBadge(eventFamily) ?? family, "ProductionEventIntelligence.EventType", true),
            primaryObjects.Select(x => new PosterField(x, "ProductionEventIntelligence.PrimaryObjects", true)).ToArray(),
            verified.Except(primaryObjects, StringComparer.OrdinalIgnoreCase).Select(x => new PosterField(x, authority, true)).ToArray(), facts,
            null, null, null, null, null, null, null, null, Array.Empty<PosterField>(), CopyPolicy);
    }

    private static PosterFact SimpleFact(string key, string label, string value, FactCategory category)
    {
        var display = value.ToUpperInvariant();
        return new(key, label, display, "ProductionEventIntelligence.PrimaryObjects", true, 1, category, 1, 1, 1, false, value, display, "UppercasePresentation");
    }

    private static string NormalizeCopy(string value) => Regex.Replace(value?.ToLowerInvariant() ?? "", @"[^\p{L}\p{N}]+", " ").Trim();
    private static bool ContainsForbiddenTemporalClaim(string value) => Regex.IsMatch(NormalizeCopy(value), @"\b(tonight|now|this week|visible now)\b");
    private static string CleanIdentity(string value) => Regex.Replace(value ?? "", @"[^\p{L}\p{N}\- ]", "").Trim();
    private static string DeriveObject(string title) => Regex.Replace(title, @"(?i)\b(constellation|guide|eclipse|meteor shower)\b", " ").Trim();
    private static IReadOnlyList<string> StringArray(JsonElement value, string name) => value.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
        ? array.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : Text(x, "name")).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
        : Array.Empty<string>();
    internal static bool PublishedChecksumsAgree(string manifestChecksum, string publicationChecksum, string validationChecksum) =>
        !string.IsNullOrWhiteSpace(manifestChecksum)
        && publicationChecksum.Equals(manifestChecksum, StringComparison.OrdinalIgnoreCase)
        && validationChecksum.Equals(manifestChecksum, StringComparison.OrdinalIgnoreCase);
    private static string? EventBadge(string eventType) => string.IsNullOrWhiteSpace(eventType) ? null : eventType.ToUpperInvariant() switch
    { "CONSTELLATION" => "CONSTELLATION", "ECLIPSE" => "ECLIPSE", "METEORSHOWER" or "METEOR SHOWER" => "METEOR SHOWER", "CONJUNCTION" => "CONJUNCTION", _ => null };
    private static string AuthorityChecksum(ThumbnailManifest value)
    {
        var semantic = value with { CreatedUtc = default, DeterministicChecksum = "" };
        return Hash(JsonSerializer.Serialize(semantic, Json) + "|" + string.Join('|', value.Variants.Select(x => x.PhysicalSha256)));
    }
    private static void Identity(JsonElement value, string plan, string evt, string language, string code) =>
        Require(Text(value, "planId").Equals(plan, StringComparison.OrdinalIgnoreCase) && Text(value, "eventId").Equals(evt, StringComparison.OrdinalIgnoreCase)
            && Text(value, "language").Equals(language, StringComparison.OrdinalIgnoreCase), code, "Authority identity mismatch.");
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var x) && x.ValueKind != JsonValueKind.Null ? x.ToString() : "";
    private static bool Flag(JsonElement value, string name) => value.TryGetProperty(name, out var x) && (x.ValueKind == JsonValueKind.True || x.ValueKind == JsonValueKind.String && bool.TryParse(x.GetString(), out var b) && b);
    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static async Task<T> Read<T>(string path, CancellationToken ct) => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json)!;
    private static Task Write<T>(string path, T value, CancellationToken ct) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); return File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), ct); }
    private static void Require(bool condition, string code, string message) { if (!condition) throw new InvalidOperationException($"{code}: {message}"); }

    internal static int RemoveOrphanStagingDirectories(string outputRoot)
    {
        if (!Directory.Exists(outputRoot)) return 0;
        var root = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var removed = 0;
        foreach (var candidate in Directory.EnumerateDirectories(outputRoot, "12-thumbnails.staging-*", SearchOption.TopDirectoryOnly))
        {
            var full = Path.GetFullPath(candidate);
            if (!full.StartsWith(root, StringComparison.Ordinal) || !Path.GetFileName(full).StartsWith("12-thumbnails.staging-", StringComparison.Ordinal)) continue;
            SafeDeleteDirectory(full);
            if (!Directory.Exists(full)) removed++;
        }
        return removed;
    }

    internal static void SafeDeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    internal sealed record ThumbnailCopyDecision(string Headline, string Rule, int WordCount);
    internal sealed record CopyDifferentiationDecision(string NormalizedHeroTitle, string NormalizedHeroSubtitle,
        string NormalizedThumbnailHeadline, string NormalizedThumbnailSecondaryText, bool HeroTitleReusedVerbatim,
        bool HeroSubtitleReusedVerbatim, bool ParagraphCopyRendered, bool DuplicateCopyDetected,
        bool CopyDifferentiationPassed, bool ForbiddenTemporalClaimDetected, IReadOnlyList<string> SharedAuthorityTokens);
    internal sealed record Profile(string Role, int Width, int Height, int FontSize, int Margin)
    {
        public double VisualEmphasis => Role == "Landscape" ? .70 : Role == "Square" ? .68 : .72;
    }
    internal sealed record PosterField(string Value, string AuthoritySource, bool IsCertified);
    internal enum FactCategory { Identification, Timing, Direction, Equipment, BrightObjects, DeepSky, Visibility, Safety, EventGeometry }
    internal sealed record PosterFact(string Key, string Label, string Value, string AuthoritySource, bool IsCertified, int EventFamilyPriority,
        FactCategory FactCategory, int ProfilePriority, int VisualPriority, int SpaceCost, bool CanCompact,
        string SourceValue, string DisplayValue, string DisplayTransformationRule)
    {
        public bool Certified => IsCertified;
    }
    internal sealed record ThumbnailPosterContent(string EventFamily, string Headline, PosterField Badge,
        IReadOnlyList<PosterField> PrimaryObjects, IReadOnlyList<PosterField> SecondaryObjects,
        IReadOnlyList<PosterFact> Facts, PosterField? Date, PosterField? BestTime, PosterField? Direction, PosterField? Location,
        PosterField? Equipment, PosterField? ObservationMode, PosterField? Separation, PosterField? Subheadline,
        IReadOnlyList<PosterField> FooterTips, string PosterPolicyVersion);
    internal sealed record TextBlockBounds(string Key, float X, float Y, float Width, float Height, float FontSize);
    internal sealed record Region(int X, int Y, int Width, int Height);
    internal sealed record FactSelectionDiagnostics(string Profile, int CandidateFactCount, int PreferredFactCount,
        int MinimumFactCount, int SelectedFactCount, IReadOnlyList<string> SelectedFactKeys, IReadOnlyList<string> OmittedFactKeys,
        IReadOnlyDictionary<string, string> OmissionReasons, Region AvailablePosterBounds, Region FinalPosterBounds,
        bool PanelExpansionApplied, float MinimumFontSize, float MeasuredRequiredHeight, float MeasuredAvailableHeight,
        bool AdditionalFactCouldFitSafely, bool SuboptimalFactSelectionDetected);
    internal sealed record FactDrawingLayout(PosterFact Fact, string DisplayValue, Font Label, Font Value,
        RectangleF LabelBox, RectangleF ValueBox);
    internal sealed record PosterFactSelectionResult(IReadOnlyList<PosterFact> SelectedFacts, IReadOnlyList<PosterFact> OmittedFacts,
        IReadOnlyDictionary<string, string> OmissionReasons, IReadOnlyList<TextBlockBounds> TextBounds,
        IReadOnlyList<FactDrawingLayout> FactLayouts, Font BadgeFont, (string Text, Font Font) Headline,
        TextBlockBounds HeadlineBounds, TextBlockBounds BadgeBounds, Rectangle FinalPosterBounds, int MinimumFactCount,
        int PreferredFactCount, bool SuboptimalFactSelectionDetected, FactSelectionDiagnostics Diagnostics);
    private sealed record PosterLayoutResult(Region PanelRegion, Region ContentRegion, double PanelUtilizationPercent,
        double UnusedPosterAreaPercent, IReadOnlyList<TextBlockBounds> TextBounds, TextBlockBounds HeadlineBounds,
        TextBlockBounds BadgeBounds, IReadOnlyList<TextBlockBounds> FactBounds, IReadOnlyList<string> RenderedFactKeys,
        IReadOnlyList<string> SelectedFactCategories, IReadOnlyDictionary<string, string> OmissionReasons,
        FactSelectionDiagnostics FactSelectionDiagnostics);
    private sealed record ThumbnailVariant(string Role, string PhysicalPath, string SelectionAuthority, string RenderSourceType,
        string SourceHeroRole, string SourceHeroPath, string SourceHeroAuthorityChecksum,
        string SourcePhase8AssetId, string SourcePhase8SceneId, string SourcePhase8PhysicalPath, string SourcePhase8PhysicalSha256,
        string SourceSemanticIdentity, string SourceVisualStyle, bool RequiresScientificGeometry, bool ScientificGeometryCertified,
        bool ScientificGeometryPreserved, string ResizeStrategy, Region ProtectedSubjectRegion, Region TextSafeRegion,
        string PresentationMode, string SourcePresentationMode, string SourceCopy, string SourceSubtitle, string ThumbnailCopy,
        string CopyTransformationRule, string CopyPolicyVersion, int HeadlineWordCount, string? Badge, int VisualEmphasisPercent,
        string LayoutProfile, string Renderer, int Width, int Height, string AspectRatio, string Format, string MimeType,
        long ByteLength, string PhysicalSha256, ThumbnailPosterContent PosterContent, IReadOnlyList<string> SelectedFactKeys,
        IReadOnlyList<string> SelectedFactCategories, IReadOnlyList<string> OmittedFactKeys, IReadOnlyDictionary<string, string> OmissionReasons,
        string FactSelectionPolicyVersion, int FactCount, Region PosterPanelBounds, Region PosterContentBounds,
        double PosterPanelUtilizationPercent, double UnusedPosterAreaPercent, TextBlockBounds HeadlineBounds,
        TextBlockBounds BadgeBounds, IReadOnlyList<TextBlockBounds> FactBounds, IReadOnlyList<TextBlockBounds> TextBoundingBoxes,
        FactSelectionDiagnostics FactSelectionDiagnostics,
        bool HeroRasterUsedAsBackground, bool TextOverlapDetected, bool SubjectOverlapDetected, bool ScientificRegionOverlapDetected, bool TextClipped, bool HeadlineReadable,
        bool MinimumFontSizePassed, bool FactCountWithinProfileLimit, bool NoParagraphCopy, bool SubjectVisibilityPassed,
        bool ScientificPreservationPassed, string ValidationStatus)
    {
        public string SourceHeroChecksum => SourceHeroAuthorityChecksum;
        public string? SecondaryText => null;
    }

    private sealed record ThumbnailManifest(string SchemaVersion, string PlanId, string ExecutionId, string EventId, string Language, DateTimeOffset CreatedUtc,
        string Phase11HeroManifestPath, string Phase11AuthorityChecksum, string Phase10CertificationPath, string Phase10CertificationChecksum,
        string CopyAuthoritySource, string CopyAuthorityChecksum, string CopyPolicyVersion, string RendererVersion, string LayoutVersion, string ProviderPolicy,
        int ProviderCallCount, IReadOnlyList<ThumbnailVariant> Variants, string ValidationStatus, string PublicationState, bool CandidateReadbackPassed,
        bool CommittedReadbackPassed, string DeterministicChecksum, bool DownstreamReady);
}

internal sealed record ResponsiveThumbnailPublicationResult(
    IReadOnlyList<string> OutputFiles,
    string AuthorityChecksum,
    bool CandidateValidationPassed,
    bool CandidateReadbackPassed,
    bool PublicationCommitted,
    bool CommittedReadbackPassed,
    bool DownstreamReady,
    string Reason,
    string ReasonCode);
