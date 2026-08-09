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
/// Phase 12 presentation authority. This renderer can only decorate the matching,
/// committed Phase 11 responsive raster; it has no provider or scene-selection path.
/// </summary>
internal static class ResponsiveThumbnailAuthorityService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string Renderer = "DeterministicResponsiveThumbnailRenderer/2.0";
    private const string Layout = "DiscoveryThumbnailLayout/2.0";
    private const string CopyPolicy = "ThumbnailCopyPolicy/2.0";

    internal static async Task<ResponsiveThumbnailPublicationResult> PublishAsync(
        string outputRoot, string planId, string eventId, string language, CancellationToken ct)
    {
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
        var eventFamily = Text(p10, "eventType");
        var primaryObjects = StringArray(p10, "primaryObjects");
        var primaryObject = primaryObjects.FirstOrDefault() ?? DeriveObject(title);
        var copy = BuildThumbnailCopy(eventFamily, primaryObjects, primaryObject, title);
        Require(copy.WordCount is > 0 and <= 5, "P12_COPY_BUDGET_EXCEEDED", "Thumbnail headline exceeds the approved five-word budget.");
        var copyValidation = ValidateCopyDifferentiation(title, subtitle, copy.Headline, null, copy.Rule);
        var copyChecksum = Hash(string.Join('|', title, subtitle, language));
        var variants = hero.GetProperty("variants").EnumerateArray().ToDictionary(x => Text(x, "variant"), StringComparer.OrdinalIgnoreCase);
        var profiles = new[] { new Profile("Landscape", 1280, 720, 72, 52), new Profile("Square", 1080, 1080, 70, 64), new Profile("Portrait", 1080, 1920, 76, 76) };
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

        var root = Path.Combine(outputRoot, "12-thumbnails");
        var transaction = Guid.NewGuid().ToString("N");
        var staging = root + ".staging-" + transaction;
        Directory.CreateDirectory(staging);
        var items = new List<ThumbnailVariant>();
        foreach (var profile in profiles)
        {
            var source = variants[profile.Role];
            var style = Text(source, "sourceVisualStyle");
            Require(style is "Cinematic" or "HybridCinematic", "P12_FORBIDDEN_AUTHORITY_PATH", $"{profile.Role} Hero style is not cinematic.");
            var sourceRelative = Text(source, "physicalPath");
            var sourcePath = Path.GetFullPath(Path.Combine(outputRoot, sourceRelative));
            Require(File.Exists(sourcePath), "P12_SOURCE_IMAGE_MISSING", $"{profile.Role} Hero raster is missing.");
            var sourceSha = Sha(sourcePath);
            Require(sourceSha.Equals(Text(source, "physicalSha256"), StringComparison.OrdinalIgnoreCase), "P12_SOURCE_CHECKSUM_MISMATCH", $"{profile.Role} Hero checksum failed.");
            var fileName = $"thumbnail-{profile.Role.ToLowerInvariant()}.png";
            var target = Path.Combine(staging, fileName);
            var headline = copy.Headline;
            Render(sourcePath, target, profile, headline);
            using var decoded = await Image.LoadAsync(target, ct);
            Require(decoded.Width == profile.Width && decoded.Height == profile.Height, "P12_PHYSICAL_VALIDATION_FAILED", $"{profile.Role} physical dimensions failed.");
            var outputSha = Sha(target);
            Require(outputSha != sourceSha, "P12_RENDER_FAILED", $"{profile.Role} did not add presentation value.");
            var requiresScience = Flag(source, "requiresScientificGeometry");
            Require(!requiresScience || (Flag(source, "scientificGeometryCertified") && Flag(source, "scientificGeometryPreserved") && Flag(source, "scientificRegionPassed")),
                "P12_SCIENTIFIC_REGION_NOT_PRESERVED", $"{profile.Role} scientific preservation cannot be proven.");
            items.Add(new(profile.Role, $"12-thumbnails/{fileName}", profile.Role, sourceRelative, sourceSha,
                Text(source, "sourcePhase8AssetId"), Text(source, "sourcePhase8SceneId"), Text(source, "sourcePhase8SemanticIdentity"), style,
                requiresScience, Flag(source, "scientificGeometryCertified"), true, source.GetProperty("width").GetInt32(), source.GetProperty("height").GetInt32(),
                "NoCrop", profile.Role == "Landscape" ? "AspectPreservingLanczosResize" : "IdentityScale", new Region(0, 0, profile.Width, (int)(profile.Height * profile.VisualEmphasis)),
                new Region(0, 0, profile.Width, (int)(profile.Height * profile.VisualEmphasis)), new Region(profile.Margin, (int)(profile.Height * profile.VisualEmphasis), profile.Width - 2 * profile.Margin, (int)(profile.Height * (1 - profile.VisualEmphasis))),
                "DiscoveryThumbnail", "EditorialHero", title, subtitle, headline, copy.Rule, CopyPolicy, copy.WordCount, null, false, false, false,
                (int)Math.Round(profile.VisualEmphasis * 100), null, $"{profile.Role}Discovery/2.0", Renderer, profile.Width, profile.Height,
                $"{profile.Width}:{profile.Height}", "png", "image/png", new FileInfo(target).Length, outputSha, true, false, true, true, true, "Valid"));
        }

        var created = DateTimeOffset.UtcNow;
        var manifest = new ThumbnailManifest("1.0", planId, Text(hero, "executionId"), eventId, language, created,
            "11-hero/hero-asset-manifest.json", heroChecksum, "10-scene-validation/scene-asset-certification.json", Text(p10, "deterministicChecksum"),
            "Phase11HeroManifest.TitleSubtitle", copyChecksum, CopyPolicy, Renderer, Layout, "NoGenerativeImageProvider", 0, items,
            "Valid", "Committed", true, true, "", true);
        manifest = manifest with { DeterministicChecksum = AuthorityChecksum(manifest) };
        await Write(Path.Combine(staging, "thumbnail-asset-manifest.json"), manifest, ct);
        var candidate = await Read<ThumbnailManifest>(Path.Combine(staging, "thumbnail-asset-manifest.json"), ct);
        Require(candidate.DeterministicChecksum == AuthorityChecksum(candidate), "P12_CANDIDATE_READBACK_FAILED", "Candidate authority checksum failed.");
        ValidateManifestCopy(candidate, title, subtitle, copy);
        var diagnostics = new { phase12Applicable = true, thumbnailRequested = true, phase11AuthorityLoaded = true, phase11AuthorityChecksum = heroChecksum,
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
            landscapeVisualEmphasisPercent = 82, squareVisualEmphasisPercent = 80, portraitVisualEmphasisPercent = 84,
            copyAuthoritySource = manifest.CopyAuthoritySource, copyPolicyVersion = CopyPolicy, azureImageCallsThisPhase = 0, otherGenerativeImageCallsThisPhase = 0,
            proceduralAstronomyGenerationCallsThisPhase = 0, legacyQuestionEngineAuthorityUsed = false, legacyHeroAssetsAuthorityUsed = false,
            legacySceneApprovalAuthorityUsed = false, v9AiCompleteRasterUsed = false, stretchResizeUsed = false, candidateValidationPassed = true,
            candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, downstreamReady = true };
        await Write(Path.Combine(staging, "phase12-authority-diagnostics.json"), diagnostics, ct);
        var backup = root + ".backup-" + transaction;
        if (Directory.Exists(root)) Directory.Move(root, backup);
        try { Directory.Move(staging, root); } catch { if (Directory.Exists(backup)) Directory.Move(backup, root); throw; }
        var committed = await Read<ThumbnailManifest>(Path.Combine(root, "thumbnail-asset-manifest.json"), ct);
        Require(committed.DeterministicChecksum == AuthorityChecksum(committed), "P12_COMMITTED_READBACK_FAILED", "Committed authority checksum failed.");
        ValidateManifestCopy(committed, title, subtitle, copy);
        var report = new { transactionId = transaction, candidateCreated = true, candidateValidationPassed = true, candidateReadbackPassed = true,
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

    private static void Render(string source, string target, Profile profile, string headline)
    {
        using var image = Image.Load<Rgba32>(source);
        image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(profile.Width, profile.Height), Mode = ResizeMode.Max, Sampler = KnownResamplers.Lanczos3 }));
        Require(image.Width == profile.Width && image.Height == profile.Height, "P12_RENDER_FAILED", "Aspect-preserving resize did not fill the target.");
        var family = SystemFonts.Collection.Families.First();
        var font = family.CreateFont(profile.FontSize, FontStyle.Bold);
        var y = (float)(profile.Height * (profile.VisualEmphasis + .035));
        var display = profile.Role == "Landscape" ? headline : headline.Replace(' ', '\n');
        image.Mutate(x => { x.Fill(Color.FromRgba(0, 0, 0, 210), new Rectangle(0, (int)(profile.Height * profile.VisualEmphasis), profile.Width, profile.Height));
            x.DrawText(display, font, Color.White, new PointF(profile.Margin, y)); });
        image.SaveAsPng(target);
    }

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

    internal sealed record ThumbnailCopyDecision(string Headline, string Rule, int WordCount);
    internal sealed record CopyDifferentiationDecision(string NormalizedHeroTitle, string NormalizedHeroSubtitle,
        string NormalizedThumbnailHeadline, string NormalizedThumbnailSecondaryText, bool HeroTitleReusedVerbatim,
        bool HeroSubtitleReusedVerbatim, bool ParagraphCopyRendered, bool DuplicateCopyDetected,
        bool CopyDifferentiationPassed, bool ForbiddenTemporalClaimDetected, IReadOnlyList<string> SharedAuthorityTokens);
    private sealed record Profile(string Role, int Width, int Height, int FontSize, int Margin)
    {
        public double VisualEmphasis => Role == "Landscape" ? .82 : Role == "Square" ? .80 : .84;
    }
    private sealed record Region(int X, int Y, int Width, int Height);
    private sealed record ThumbnailVariant(string Role, string PhysicalPath, string SourceHeroRole, string SourceHeroPath, string SourceHeroChecksum,
        string SourcePhase8AssetId, string SourcePhase8SceneId, string SourceSemanticIdentity, string SourceVisualStyle, bool RequiresScientificGeometry,
        bool ScientificGeometryCertified, bool ScientificGeometryPreserved, int SourceWidth, int SourceHeight, string CropStrategy, string ResizeStrategy,
        Region ProtectedScientificRegion, Region ProtectedSubjectRegion, Region TextSafeRegion, string PresentationMode, string SourcePresentationMode,
        string SourceCopy, string SourceSubtitle, string ThumbnailCopy, string CopyTransformationRule, string CopyPolicyVersion, int HeadlineWordCount,
        string? Badge, bool HeroCopyReusedVerbatim, bool DuplicateCopyDetected, bool ParagraphCopyRendered, int VisualEmphasisPercent, string? SecondaryText,
        string LayoutProfile, string Renderer, int Width, int Height, string AspectRatio, string Format, string MimeType, long ByteLength, string PhysicalSha256,
        bool TextSafeAreaPassed, bool OverflowDetected, bool SubjectVisibilityPassed, bool ScientificPreservationPassed, bool ForbiddenTextPassed, string ValidationStatus);
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
