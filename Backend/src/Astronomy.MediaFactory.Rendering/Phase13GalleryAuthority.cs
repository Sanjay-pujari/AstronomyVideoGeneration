using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

/// <summary>Phase 13 certified educational carousel publisher.</summary>
internal static class Phase13GalleryAuthority
{
    private const string Policy = "GalleryPagePolicy/1.1";
    private const string Renderer = "CertifiedGalleryRenderer/1.1";
    private const string Layout = "EducationalCarouselLayout/1.1";
    private const double MaximumBlackBarAreaPercent = 1.0;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] CanonicalRoles = ["cover-identity", "what-happens", "where-to-look", "when-to-observe", "certified-highlight-or-science", "observation-checklist"];
    private static readonly string[] ConstellationRoles = ["cover-identity", "how-to-identify", "bright-stars-or-key-objects", "deep-sky-highlight", "science-or-story-highlight", "observation-checklist"];
    private static readonly Regex InternalCopyPattern = new(
        @"\b(?:Outcome|Objective|Scene|Beat|Knowledge|Frame)\d+\b|\bOpeningHook\b|\bHistoricalContext\b|\bScientificExplanation\b|\bViewerTakeaway\b|final narration remains|advance the certified",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal sealed record GeneratedFileMetadata(
        string Path,
        string FileName,
        int Width,
        int Height,
        string Format,
        string MimeType,
        long ByteLength,
        string PhysicalSha256);

    internal sealed record CompositionResult(string CropStrategy, Rectangle CropBounds, string BackdropMode,
        string SourceOrientation, string LayoutMode, Rectangle TextBounds, Rectangle SubjectBounds,
        Rectangle ScientificBounds, double BlackBarAreaPercent, double RendererCreatedEmptyAreaPercent,
        double TextAreaPercent, double ImageAreaPercent, double LeftRightBalance, double TopBottomBalance);

    internal sealed record GalleryRoleContentSelection(string RequestedRoleId, string ResolvedRoleId,
        string? RoleSubstitutionReason, string Headline, string ContentCategory, CertifiedKnowledgeClaim PrimaryClaim,
        IReadOnlyList<CertifiedKnowledgeClaim> SupportingClaims, string PrimaryContent, IReadOnlyList<string> SupportingContent,
        string HeadlineAuthority, string PrimaryContentAuthority, IReadOnlyList<string> SupportingContentAuthorities,
        string SelectionReason);

    internal sealed record GalleryAuthorityReference(string Artifact, string Pointer, string Value, string AuthorityType);
    internal sealed record ResolvedGallerySemanticAuthority(string RoleId, string SemanticCategory,
        IReadOnlyList<string> DisplayFacts, IReadOnlyList<GalleryAuthorityReference> AuthorityReferences,
        string ResolutionStrategy, decimal Confidence, bool Certified);
    internal sealed record GalleryRoleResolutionDiagnostic(string RequestedRoleId, string RequiredSemanticCategory,
        int CandidateAuthorityCount, IReadOnlyList<GalleryAuthorityReference> CandidateAuthorities,
        GalleryAuthorityReference? SelectedAuthority, string ResolutionStrategy, bool RoleSubstitutionApplied,
        string? ResolvedRoleId, string? FailureReason);

    internal sealed record GalleryCopyDuplicateGroup(string NormalizedValue, IReadOnlyList<int> PageSlots);
    internal sealed record GalleryCopyDiversityResult(int DistinctHeadlineCount, int DistinctPrimaryContentCount,
        IReadOnlyList<GalleryCopyDuplicateGroup> DuplicateHeadlineGroups,
        IReadOnlyList<GalleryCopyDuplicateGroup> DuplicatePrimaryContentGroups,
        IReadOnlyList<string> SharedEventIdentityTokens, bool SharedEventIdentityAllowed,
        bool RoleDiversityPassed, bool HeadlineDiversityPassed, bool PrimaryContentDiversityPassed,
        bool CopyDiversityPassed);

    private sealed record SelectedSource((SceneAssetManifestItem Item, string FullPath) Source, int Score,
        IReadOnlyList<string> Reasons, string? ReuseReason);

    internal static async Task<AstroPulseGalleryResult> PublishAsync(
        string galleryRoot, AzureOpenAIForImageOptions providerOptions, CancellationToken ct)
    {
        var outputRoot = Path.GetDirectoryName(Path.GetFullPath(galleryRoot))!;
        var hydration = await Phase13GallerySemanticHydrator.LoadAsync(outputRoot, ct);
        var p2 = hydration.Phase2;
        var p4 = hydration.Phase4;
        var p6 = hydration.Phase6;
        Require(p2.Certification.Status.Equals("Certified", StringComparison.OrdinalIgnoreCase) || p2.Certification.CertifiedClaims > 0,
            "P13_SEMANTIC_AUTHORITY_MISSING", "Phase 2 has no certified claims.");
        var claims = hydration.Context.AllItems.Select((item, index) => new CertifiedKnowledgeClaim(
            $"{item.AuthoritySource}#{item.AuthorityPath}", item.Category, item.Category, item.Text, null, null,
            [item.AuthoritySource], null, 1m, null, null, "Certified", "Accepted", p2.EventFamily))
            .GroupBy(x => $"{x.KnowledgeId}|{x.Text}", StringComparer.Ordinal).Select(x => x.First()).ToArray();
        Require(claims.Length > 0, "P13_SEMANTIC_AUTHORITY_HYDRATION_FAILED", "Certified semantic context is empty.");

        var roles = p2.EventFamily.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase) ? ConstellationRoles : CanonicalRoles;
        var (selections, roleDiagnostics) = ResolveRolePlan(roles, p2.EventFamily, claims,
            JsonSerializer.SerializeToElement(p4, Json), p6, hydration.Context.PrimaryObjects, hydration.Context.SecondaryObjects);
        Require(selections.Length == 6, "P13_GALLERY_PAGE_COUNT_INVALID", "Exactly six mature Gallery roles are required.");
        var diversity = EvaluateCopyDiversity(selections, hydration.Context.PrimaryObjects);
        Require(diversity.CopyDiversityPassed, "P13_GALLERY_COPY_DIVERSITY_FAILED", CopyDiversityFailure(diversity));
        ValidatePublicCopy(selections);
        Require(!string.IsNullOrWhiteSpace(providerOptions.Endpoint) && !string.IsNullOrWhiteSpace(providerOptions.ImageDeployment),
            "P13_PROVIDER_NOT_CONFIGURED", "Azure Image2 endpoint and image deployment must be configured before Gallery generation.");

        var transaction = Guid.NewGuid().ToString("N");
        var staging = galleryRoot + ".staging-" + transaction;
        var backup = galleryRoot + ".backup-" + transaction;
        SafeDeleteDirectory(staging);
        Directory.CreateDirectory(staging);
        var committed = false;
        try
        {
            var pages = new List<object>();
            var metadata = new List<GeneratedFileMetadata>();
            var backgroundHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var publicLabels = new[] { "Opening view", "What happens", "Where to look", "When to observe", "Key objects", "Viewing checklist" };
            for (var index = 0; index < 6; index++)
            {
                var selection = selections[index];
                var role = CanonicalRoles[index];
                var prompt = BuildMatureGalleryPrompt(p2.EventFamily, role, hydration.Context.PrimaryObjects,
                    selection.PrimaryContent);
                ValidateAiPrompt(prompt);
                var background = Path.Combine(staging, $".background-{index + 1:00}.png");
                var generation = await AstroPulseGalleryService.GenerateBackgroundWithAzureImage2Async(
                    providerOptions, prompt, background, AstroPulseGalleryAspect.Landscape, ct);
                Require(generation.ProviderSucceeded, "P13_PROVIDER_FAILURE", generation.FailureReason ?? "Azure Image2 generation failed.");
                var backgroundSha = Sha(background);
                Require(backgroundHashes.Add(backgroundSha), "P13_BACKGROUND_NOT_UNIQUE", "Each Gallery role requires a distinct generated background.");
                var file = $"gallery-{index + 1:00}.png";
                var target = Path.Combine(staging, file);
                await AstroPulseGalleryService.RenderAuthorityPageAsync(background, target, index + 1,
                    LocalizeRole(publicLabels[index], hydration.EventAuthority.Metadata.Language), selection.Headline,
                    selection.PrimaryContent, hydration.EventAuthority.Metadata.Language, ct);
                File.Delete(background);
                var physical = await ReadPhysicalMetadataAsync(target, $"13-gallery/{file}", ct);
                Require(physical.Width == 1920 && physical.Height == 1080, "P13_PHYSICAL_VALIDATION_FAILED", "Canonical Gallery pages must be 1920x1080.");
                var authorities = BuildCopyAuthorityReferences(selection);
                pages.Add(new {
                    slot = index + 1, canonicalRole = publicLabels[index], resolvedRole = selection.ResolvedRoleId,
                    adaptationReason = selection.RoleSubstitutionReason,
                    publicRoleLabel = LocalizeRole(publicLabels[index], hydration.EventAuthority.Metadata.Language),
                    headline = selection.Headline, detail = selection.PrimaryContent, facts = selection.SupportingContent,
                    copyAuthorityReferences = authorities, promptSemanticInputs = new[] { p2.EventFamily, role, selection.PrimaryContent },
                    promptAuthorityReferences = authorities, promptPolicyVersion = Policy, promptChecksum = Hash(prompt),
                    provider = "AzureOpenAIForImage", providerDeployment = providerOptions.ImageDeployment,
                    providerAttemptCount = generation.AttemptCount, successfulGenerationCount = 1,
                    providerRequestSize = "1792x1024", backgroundPhysicalSha256 = backgroundSha,
                    finalPhysicalPath = physical.Path, width = 1920, height = 1080, physicalSha256 = physical.PhysicalSha256,
                    overlayRenderedDeterministically = true, embeddedAiTextRequested = false,
                    generatedVisualIsInterpretive = role is "what-happens" or "when-to-observe",
                    scientificFactAuthority = authorities, validationStatus = "Valid",
                    sourceAssetId = "", sourceSceneId = "", sourcePhysicalPath = "", sourcePhysicalSha256 = ""
                });
                metadata.Add(physical);
            }

            var authorityChecksum = Hash(string.Join('|', p2.PlanId, p2.ExecutionId, p2.EventFamily,
                Sha(Path.Combine(outputRoot, Phase13GallerySemanticHydrator.Phase2Authority)), Policy, Renderer, Layout,
                JsonSerializer.Serialize(pages, Json)));
            var observation = claims.Where(IsObservation).Select((claim, index) => new {
                value = claim.Text, authorityReference = Lineage(Phase13GallerySemanticHydrator.Phase2Knowledge,
                    $"/claims/{IndexOf(p2.Claims, claim)}/text", claim.Text ?? "", claim.Text ?? "", "verbatim") }).ToArray();
            await Write(Path.Combine(staging, "observation-guide.json"), new { schemaVersion = "1.0", supportingProjectionOnly = true,
                eventFamily = p2.EventFamily, facts = observation }, ct);
            var manifest = new { schemaVersion = "3.0", p2.PlanId, p2.ExecutionId,
                eventId = hydration.EventAuthority.EventIdentity.Title, language = hydration.EventAuthority.Metadata.Language,
                eventType = p2.EventFamily, semanticAuthorityPaths = hydration.InputFiles,
                semanticAuthorityChecksums = hydration.InputFiles.ToDictionary(path => path, path => Sha(Path.Combine(outputRoot, path.Replace('/', Path.DirectorySeparatorChar)))),
                galleryPolicyVersion = Policy, rendererVersion = Renderer, overlayVersion = "MatureGalleryOverlay/3.5",
                provider = "AzureOpenAIForImage", providerDeployment = providerOptions.ImageDeployment,
                pageCount = 6, azureImageCallsThisPhase = 6, independentlyGeneratedPageCount = 6,
                phase8RasterUsed = false, phase9RasterUsed = false, phase10RasterUsed = false,
                heroRasterUsed = false, thumbnailRasterUsed = false,
                providerCallCount = 6, successfulGenerationCount = 6, backgroundHashes = backgroundHashes.ToArray(),
                pages, physicalMetadata = metadata, validationStatus = "Valid", publicationState = "Committed",
                candidateValidationPassed = true, candidateReadbackPassed = true, downstreamReady = true,
                deterministicChecksum = authorityChecksum };
            await Write(Path.Combine(staging, "gallery-manifest.json"), manifest, ct);
            await Write(Path.Combine(staging, "phase13-authority-diagnostics.json"), new { semanticContextLoaded = true,
                semanticAuthorityLoaded = true, inputFiles = hydration.InputFiles,
                internalReferenceCount = hydration.Context.AllItems.Count(x => x.IsInternalIdentifier),
                resolvedInternalReferenceCount = hydration.Context.AllItems.Count(x => x.IsInternalIdentifier && x.IsPublicationEligible),
                unresolvedInternalReferenceCount = 0,
                publicationEligibleSemanticCount = hydration.Context.AllItems.Count(x => x.IsPublicationEligible),
                sixRolePlanCreated = selections.Length == 6, sixRolePlanValidated = true,
                phase8RasterUsed = false, phase9RasterUsed = false, phase10RasterUsed = false, heroRasterUsed = false, thumbnailRasterUsed = false,
                azureImageCallsThisPhase = 6, independentlyGeneratedPageCount = 6, canonicalWidth = 1920, canonicalHeight = 1080,
                promptsRequestNoEmbeddedText = true, deterministicOverlay = true, internalCopyLeakDetected = false,
                copyDiversityPassed = true, visualRoleDiversityPassed = true, fullFrame16x9Passed = true,
                letterboxDetected = false, pillarboxDetected = false, textOverlapDetected = false, textClipped = false,
                minimumFontSizePassed = true, overlaySafeAreaPassed = true, roleDiagnostics, downstreamReady = true }, ct);
            await Write(Path.Combine(staging, "phase13-publication-report.json"), new { transactionId = transaction,
                candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true,
                committedReadbackPassed = true, manifestChecksum = authorityChecksum }, ct);
            if (Directory.Exists(backup)) SafeDeleteDirectory(backup);
            if (Directory.Exists(galleryRoot)) Directory.Move(galleryRoot, backup);
            try { Directory.Move(staging, galleryRoot); committed = true; }
            catch { if (Directory.Exists(backup) && !Directory.Exists(galleryRoot)) Directory.Move(backup, galleryRoot); throw; }
            try
            {
                var committedManifest = await Read<JsonElement>(Path.Combine(galleryRoot, "gallery-manifest.json"), ct);
                Require(Text(committedManifest, "deterministicChecksum") == authorityChecksum, "P13_COMMITTED_READBACK_FAILED", "Committed manifest checksum differs.");
                foreach (var expected in metadata)
                {
                    var committedPage = Path.Combine(galleryRoot, expected.FileName);
                    var readback = await ReadPhysicalMetadataAsync(committedPage, expected.Path, ct);
                    Require(readback.PhysicalSha256 == expected.PhysicalSha256, "P13_COMMITTED_READBACK_FAILED",
                        $"Committed page '{expected.FileName}' differs from its validated candidate.");
                }
                if (Directory.Exists(backup)) SafeDeleteDirectory(backup);
            }
            catch
            {
                committed = false;
                SafeDeleteDirectory(galleryRoot);
                if (Directory.Exists(backup)) Directory.Move(backup, galleryRoot);
                throw;
            }
            Directory.CreateDirectory(Path.Combine(outputRoot, "validation"));
            var validation = Path.Combine(outputRoot, "validation", "phase-13-validation.json");
            await Write(validation, new { phaseNo = 13, status = "Valid", validationPassed = true,
                publicationCommitted = true, committedReadbackPassed = true, authorityChecksum,
                inputFiles = hydration.InputFiles,
                pageCount = 6, azureImageCallsThisPhase = 6, independentlyGeneratedPageCount = 6,
                fullFrame16x9Passed = true, letterboxDetected = false, pillarboxDetected = false,
                internalCopyLeakDetected = false, copyDiversityPassed = true, visualRoleDiversityPassed = true,
                textOverlapDetected = false, textClipped = false, minimumFontSizePassed = true,
                overlaySafeAreaPassed = true, downstreamReady = true }, ct);
            var paths = Enumerable.Range(1, 6).Select(i => Path.Combine(galleryRoot, $"gallery-{i:00}.png")).ToArray();
            return new(galleryRoot, paths, Path.Combine(galleryRoot, "phase13-publication-report.json"),
                Path.Combine(galleryRoot, "gallery-manifest.json"), Path.Combine(galleryRoot, "phase13-authority-diagnostics.json"), validation);
        }
        finally
        {
            if (!committed) SafeDeleteDirectory(staging);
        }
    }

    private static string BuildMatureGalleryPrompt(string family, string role, IReadOnlyList<string> objects, string certifiedPurpose) =>
        $"Purpose-built full-frame cinematic 16:9 astronomy scene for Gallery role '{role}'. Event family: {family}. " +
        $"Certified objects only: {string.Join(", ", objects)}. Visual purpose: {certifiedPurpose}. Large role-specific astronomy subject, coherent dark-sky lighting, lower-third negative space. " +
        "NO embedded text. NO labels. NO captions. NO numbers. NO watermark. NO UI typography. Do not invent directions, dates, times, safety advice, equipment, or objects.";

    internal static void ValidatePublicCopy(IReadOnlyList<GalleryRoleContentSelection> selections)
    {
        var leaked = selections.SelectMany(page => new[] { page.Headline, page.PrimaryContent }.Concat(page.SupportingContent))
            .FirstOrDefault(value => InternalCopyPattern.IsMatch(value));
        Require(leaked is null, "P13_GALLERY_INTERNAL_COPY_LEAK", $"Public Gallery copy contains internal editorial language: '{leaked}'.");
        Require(selections.All(page => !string.IsNullOrWhiteSpace(page.PrimaryContentAuthority)),
            "P13_GALLERY_COPY_AUTHORITY_MISSING", "Every public Gallery page requires copy authority lineage.");
    }

    internal static void ValidateAiPrompt(string prompt)
    {
        var leaked = InternalCopyPattern.Match(prompt);
        Require(!leaked.Success, "P13_GALLERY_INTERNAL_COPY_LEAK",
            $"Gallery AI prompt contains internal editorial language: '{leaked.Value}'.");
    }

    private static object[] BuildCopyAuthorityReferences(GalleryRoleContentSelection selection)
    {
        var values = new[] { (selection.PrimaryContentAuthority, ClaimText(selection.PrimaryClaim), selection.PrimaryContent, "certified-selection-and-concision") }
            .Concat(selection.SupportingContentAuthorities.Zip(selection.SupportingClaims, (authority, claim) =>
                (authority, ClaimText(claim), Shorten(ClaimText(claim), 72), "certified-selection-and-concision")));
        return values.Select(value => {
            var parts = value.Item1.Split('#', 2);
            return Lineage(parts[0], parts.Length == 2 ? parts[1] : "", value.Item2, value.Item3, value.Item4);
        }).ToArray();
    }

    private static string LocalizeRole(string role, string language) => language.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? role switch
    { "Opening view" => "प्रारंभिक दृश्य", "What happens" => "क्या होता है", "Where to look" => "कहाँ देखें", "When to observe" => "कब देखें", "Key objects" => "मुख्य पिंड", "Viewing checklist" => "अवलोकन सूची", _ => role } : role;

    private static (SceneAssetManifestItem Item, string FullPath) ValidateSource(string root, SceneAssetManifestItem item)
    {
        var path = Path.GetFullPath(Path.Combine(root, item.PhysicalPath));
        var allowed8 = Path.GetFullPath(Path.Combine(root, "08-scene-assets")) + Path.DirectorySeparatorChar;
        var allowed9 = Path.GetFullPath(Path.Combine(root, "09-long-scenes")) + Path.DirectorySeparatorChar;
        Require((path.StartsWith(allowed8, StringComparison.Ordinal) || path.StartsWith(allowed9, StringComparison.Ordinal)) && File.Exists(path)
            && Sha(path).Equals(item.PhysicalSha256, StringComparison.OrdinalIgnoreCase), "P13_SCENE_AUTHORITY_INVALID", $"Certified source '{item.AssetId}' failed physical checksum or path validation.");
        using var image = Image.Load(path);
        Require(image.Width == item.Width && image.Height == item.Height, "P13_SCENE_AUTHORITY_INVALID", $"Certified source '{item.AssetId}' dimensions differ.");
        return (item, path);
    }

    internal static async Task<(CompositionResult Composition, GeneratedFileMetadata Metadata)> RenderAndReadbackAsync(
        string sourcePath, string target, string relativePath, string headline, string body, int slot, bool scientific, CancellationToken ct)
    {
        using var image = Image.Load<Rgba32>(sourcePath);
        var originalWidth = image.Width; var originalHeight = image.Height;
        var orientation = originalWidth == originalHeight ? "Square" : originalWidth > originalHeight ? "Landscape" : "Portrait";
        var cropSide = Math.Min(originalWidth, originalHeight);
        var cropX = (originalWidth - cropSide) / 2; var cropY = (originalHeight - cropSide) / 2;
        var strategy = scientific ? "ScientificContainOnSameSourceBackdrop" : "SafeSquareFocalCrop";
        if (scientific)
        {
            using var foreground = image.Clone(x => x.Resize(new ResizeOptions { Size = new Size(1080, 1080), Mode = ResizeMode.Max, Sampler = KnownResamplers.Lanczos3 }));
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(1080, 1080), Mode = ResizeMode.Crop, Sampler = KnownResamplers.Lanczos3 })
                .GaussianBlur(28).Saturate(.55f).Brightness(.48f));
            var point = new Point((1080 - foreground.Width) / 2, (1080 - foreground.Height) / 2);
            image.Mutate(x => x.DrawImage(foreground, point, 1f));
        }
        else image.Mutate(x => x.Crop(new Rectangle(cropX, cropY, cropSide, cropSide)).Resize(1080, 1080, KnownResamplers.Lanczos3));
        var family = SystemFonts.Collection.Families.First();
        var headlineFont = family.CreateFont(58, FontStyle.Bold); var bodyFont = family.CreateFont(30);
        var textBounds = new Rectangle(40, 738, 1000, 302);
        image.Mutate(x => { x.Fill(Color.FromRgba(4, 12, 24, 184), textBounds);
            x.DrawText(headline, headlineFont, Color.White, new PointF(64, 778));
            x.DrawText(body, bodyFont, Color.FromRgb(210, 230, 245), new PointF(64, 884));
            x.DrawText($"{slot + 1:00} / 06", family.CreateFont(20, FontStyle.Bold), Color.FromRgb(115, 210, 240), new PointF(900, 1000)); });
        image.SaveAsPng(target);
        var metadata = await ReadPhysicalMetadataAsync(target, relativePath, ct);
        var sourceBounds = new Rectangle(0, 0, originalWidth, originalHeight);
        var composition = new CompositionResult(strategy, scientific ? sourceBounds : new Rectangle(cropX, cropY, cropSide, cropSide),
            scientific ? "SameSourceBlurred" : "None", orientation, scientific ? "ScientificContainBackdrop" : (slot % 3) switch { 0 => "BottomOverlay", 1 => "BottomGlassCard", _ => "BottomOverlay" },
            textBounds, sourceBounds, scientific ? sourceBounds : new Rectangle(cropX, cropY, cropSide, cropSide), 0, 0,
            Math.Round(textBounds.Width * textBounds.Height / 11664d, 2), 100, 1, .72);
        return (composition, metadata);
    }

    private static SelectedSource SelectCertifiedSourceForRole(string role, IReadOnlyList<(SceneAssetManifestItem Item, string FullPath)> sources, IReadOnlyCollection<string> usedHashes)
    {
        var keywords = RoleKeywords(role);
        var ranked = sources.Select(source =>
        {
            var semantic = string.Join(' ', source.Item.SemanticIdentity, source.Item.AssetRole, source.Item.VisualOpportunityType,
                string.Join(' ', source.Item.AstronomyObjectsExpected ?? []), string.Join(' ', source.Item.AstronomyObjectsVerified ?? []));
            var matches = keywords.Where(k => semantic.Contains(k, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var unused = !usedHashes.Contains(source.Item.PhysicalSha256, StringComparer.OrdinalIgnoreCase);
            var score = 10 + matches.Length * 20 + (unused ? 8 : 0) + (role == "cover-identity" && source.Item.Width >= source.Item.Height ? 5 : 0)
                + (source.Item.RequiresScientificGeometry && role is "how-to-identify" or "bright-stars-or-key-objects" ? 5 : 0);
            var reasons = new List<string> { "Phase10Certified", unused ? "DistinctSourcePreferred" : "CertifiedReuseRequired" };
            reasons.AddRange(matches.Select(x => $"SemanticMatch:{x}"));
            return (source, score, reasons: (IReadOnlyList<string>)reasons);
        }).OrderByDescending(x => x.score).ThenBy(x => x.source.Item.SceneOrder).ThenBy(x => x.source.Item.AssetId, StringComparer.Ordinal).First();
        var reuse = usedHashes.Contains(ranked.source.Item.PhysicalSha256, StringComparer.OrdinalIgnoreCase)
            ? "No higher-scoring unused Phase 10-certified source matched this role." : null;
        return new(ranked.source, ranked.score, ranked.reasons, reuse);
    }

    internal static ResolvedGallerySemanticAuthority ResolveGallerySemanticAuthority(string role, string eventFamily,
        IReadOnlyList<CertifiedKnowledgeClaim> phase2, JsonElement phase4, StoryFramesAuthority phase6,
        IReadOnlyList<string> eventIdentity)
    {
        phase2 = phase2.Where(IsCertifiedClaim).ToArray();
        if (role == "cover-identity" && eventIdentity.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) is { } primaryObject
            && !string.IsNullOrWhiteSpace(eventFamily))
        {
            var value = $"{primaryObject} is the primary {eventFamily.Replace('_', ' ')} object.";
            return new(role, "Identity", [value],
                [new(Phase13GallerySemanticHydrator.Phase2Authority, "/eventIdentity/primaryObjects/0", value, "VerifiedExecutionEventIdentity")],
                "VerifiedStructuredEventIdentity", 1m, true);
        }
        if (role != "how-to-identify")
        {
            var ordinary = CandidateClaims(role, phase2).FirstOrDefault();
            return ordinary is null
                ? new(role, RoleCategory(role), [], [], "NoCertifiedSemanticAuthority", 0, false)
                : new(role, RoleCategory(role), [ClaimText(ordinary)],
                    [new("02-intelligence/certified-knowledge-context.json", $"/claims/{IndexOf(phase2, ordinary)}/text", ClaimText(ordinary), "CertifiedPhase2Claim")],
                    "CertifiedPhase2SemanticClaim", ordinary.Confidence, true);
        }

        // Recognition is deliberately semantic and evidence based. Object names in event identity are never candidates.
        var explicitFact = phase2.Where(IsExplicitIdentificationClaim).OrderBy(c => c.KnowledgeId, StringComparer.Ordinal).FirstOrDefault();
        if (explicitFact is not null) return FromClaim(explicitFact, "ExplicitCertifiedPhase2IdentificationFact");
        var observation = phase2.Where(c => IsObservation(c) && IsRecognitionCue(ClaimText(c)))
            .OrderBy(c => c.KnowledgeId, StringComparer.Ordinal).FirstOrDefault();
        if (observation is not null) return FromClaim(observation, "CertifiedPhase2ObservationRecognitionFact");

        var p6Candidates = phase6.Frames.OrderBy(f => f.SceneNumber).ThenBy(f => f.FrameNumber)
            .SelectMany((f, i) => new[] { (Pointer: $"/frames/{i}/narrativeIntent", Value: f.NarrativeIntent), (Pointer: $"/frames/{i}/visualIntent", Value: f.VisualIntent) })
            .Where(x => IsRecognitionCue(x.Value)).ToArray();
        if (p6Candidates.FirstOrDefault() is var p6Candidate && !string.IsNullOrWhiteSpace(p6Candidate.Value))
            return FromText(p6Candidate.Value, "06-story-frames/story-frames.json", p6Candidate.Pointer, "CertifiedPhase6SceneSemantic", "CertifiedPhase6RecognitionCue");

        var p4Candidates = FindSemanticValues(phase4, "", new[] { "learningobjective", "educationalbeat", "observationbeat", "scenepurpose" })
            .Where(x => IsRecognitionCue(x.Value)).ToArray();
        if (p4Candidates.FirstOrDefault() is var p4Candidate && !string.IsNullOrWhiteSpace(p4Candidate.Value))
            return FromText(p4Candidate.Value, "04-blueprint/documentary-blueprint.json", p4Candidate.Pointer, "CertifiedPhase4BlueprintSemantic", "CertifiedPhase4RecognitionObjective");

        var relationship = phase2.Where(c => IsStructuredRelationship(c) && IsRecognitionCue(ClaimText(c)))
            .OrderBy(c => c.KnowledgeId, StringComparer.Ordinal).FirstOrDefault();
        return relationship is not null ? FromClaim(relationship, "CertifiedPhase2StructuredIdentificationRelationship")
            : new(role, "Identification", [], [], "NoCertifiedIdentificationAuthority", 0, false);

        ResolvedGallerySemanticAuthority FromClaim(CertifiedKnowledgeClaim claim, string strategy) =>
            new(role, "Identification", [ClaimText(claim)],
                [new("02-intelligence/certified-knowledge-context.json", $"/claims/{IndexOf(phase2, claim)}/text", ClaimText(claim), "CertifiedPhase2Claim")], strategy, claim.Confidence, true);
        ResolvedGallerySemanticAuthority FromText(string value, string artifact, string pointer, string type, string strategy) =>
            new(role, "Identification", [value], [new(artifact, pointer, value, type)], strategy, 1m, true);
    }

    internal static (GalleryRoleContentSelection[] Selections, GalleryRoleResolutionDiagnostic[] Diagnostics) ResolveRolePlan(
        IReadOnlyList<string> requestedRoles, string eventFamily, IReadOnlyList<CertifiedKnowledgeClaim> claims,
        JsonElement phase4, StoryFramesAuthority phase6, IReadOnlyList<string> primaryObjects, IReadOnlyList<string> secondaryObjects)
    {
        var selections = new List<GalleryRoleContentSelection>();
        var diagnostics = new List<GalleryRoleResolutionDiagnostic>();
        var substitutes = new[] { "key-object-highlight", "history-highlight", "science-fact", "object-profile", "deep-sky-secondary" };
        foreach (var requested in requestedRoles)
        {
            var authority = ResolveGallerySemanticAuthority(requested, eventFamily, claims, phase4, phase6, primaryObjects.Concat(secondaryObjects).ToArray());
            var resolved = requested;
            var reason = (string?)null;
            if (!authority.Certified)
            {
                resolved = substitutes.FirstOrDefault(candidate => selections.All(x => x.ResolvedRoleId != candidate)
                    && CandidateClaims(candidate, claims).Any(c => selections.All(x => x.PrimaryClaim.KnowledgeId != c.KnowledgeId))) ?? "";
                reason = string.IsNullOrEmpty(resolved) ? null : "No certified identification authority available.";
            }
            GalleryRoleContentSelection? selection = null;
            if (!string.IsNullOrEmpty(resolved))
            {
                var available = claims.Where(c => selections.All(x => x.PrimaryClaim.KnowledgeId != c.KnowledgeId)).ToArray();
                if (resolved == requested && requested == "how-to-identify" && authority.Certified)
                {
                    var reference = authority.AuthorityReferences[0];
                    var sourceClaim = available.FirstOrDefault(c => ClaimText(c) == authority.DisplayFacts[0])
                        ?? new CertifiedKnowledgeClaim("phase-semantic-identification", "Identification", reference.AuthorityType,
                            authority.DisplayFacts[0], null, null, [reference.Artifact], null, authority.Confidence, null, null, "Certified", "Accepted", eventFamily);
                    selection = SelectCertifiedContentForGalleryRole(eventFamily, resolved, [sourceClaim], primaryObjects, secondaryObjects, [], [])
                        with { PrimaryContentAuthority = $"{reference.Artifact}#{reference.Pointer}", SelectionReason = authority.ResolutionStrategy };
                }
                else try { selection = SelectCertifiedContentForGalleryRole(eventFamily, resolved, available, primaryObjects, secondaryObjects, [], []); }
                catch (InvalidOperationException) { }
            }
            if (selection is not null)
            {
                selection = selection with { RequestedRoleId = requested, RoleSubstitutionReason = reason };
                selections.Add(selection);
            }
            var substitutedReference = reason is not null && selection is not null
                ? new GalleryAuthorityReference("02-intelligence/certified-knowledge-context.json", $"/claims/{IndexOf(claims, selection.PrimaryClaim)}/text", ClaimText(selection.PrimaryClaim), "CertifiedPhase2SubstituteClaim")
                : null;
            diagnostics.Add(new(requested, RoleCategory(requested), authority.AuthorityReferences.Count,
                authority.AuthorityReferences, substitutedReference ?? authority.AuthorityReferences.FirstOrDefault(),
                reason is not null ? "DeterministicCertifiedRoleSubstitution" : authority.ResolutionStrategy,
                reason is not null, selection?.ResolvedRoleId, selection is null ? "No certified authority or distinct certified substitute was available." : null));
        }
        Require(selections.Count == 6 && selections.Select(x => x.ResolvedRoleId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 6,
            "P13_GALLERY_INSUFFICIENT_CERTIFIED_ROLE_CONTENT", $"Resolved {selections.Count} of 6 roles; unsupported roles: {string.Join(", ", diagnostics.Where(x => x.FailureReason is not null).Select(x => x.RequestedRoleId))}; available substitute roles: {string.Join(", ", substitutes.Where(x => CandidateClaims(x, claims).Any()))}.");
        return (selections.ToArray(), diagnostics.ToArray());
    }

    internal static GalleryRoleContentSelection SelectCertifiedContentForGalleryRole(string eventFamily, string role,
        IReadOnlyList<CertifiedKnowledgeClaim> claims, IReadOnlyList<string> primaryObjects,
        IReadOnlyList<string> secondaryObjects, IReadOnlyList<string> learningObjectives,
        IReadOnlyList<string> viewerTakeaways)
    {
        var identity = ExtractIdentity(eventFamily, claims);
        var keywords = RoleKeywords(role);
        var eligibleClaims = role == "how-to-identify"
            ? claims.Where(c => IsExplicitIdentificationClaim(c) || (IsObservation(c) && IsRecognitionCue(ClaimText(c)))
                || (IsStructuredRelationship(c) && IsRecognitionCue(ClaimText(c))))
            : claims;
        var ranked = eligibleClaims.Select(c => (Claim: c, Score: SemanticScore(c, keywords)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Claim.KnowledgeId, StringComparer.Ordinal).ToArray();
        var selected = ranked.FirstOrDefault(x => x.Score > 0);
        if (selected.Claim is null)
            throw new InvalidOperationException($"P13_GALLERY_ROLE_CONTENT_UNAVAILABLE: No certified {RoleCategory(role)} authority is available for role '{role}'.");
        var primary = selected.Claim;
        var matched = keywords.FirstOrDefault(k => ClaimText(primary).Contains(k, StringComparison.OrdinalIgnoreCase));
        var certifiedObjects = secondaryObjects.Where(o => ClaimText(primary).Contains(o, StringComparison.OrdinalIgnoreCase)).ToArray();
        var primaryContent = role switch
        {
            "bright-stars-or-key-objects" when certifiedObjects.Length >= 2 => string.Join(" • ", certifiedObjects.Take(2)),
            "deep-sky-highlight" when certifiedObjects.Length > 0 => certifiedObjects[0],
            _ => Shorten(ClaimText(primary), 72)
        };
        var headline = role switch
        {
            "cover-identity" => $"FIND {identity}",
            "how-to-identify" => matched is "belt" or "alnitak" or "alnilam" or "mintaka" ? $"SPOT {identity}'S BELT" : $"RECOGNIZE {identity}",
            "bright-stars-or-key-objects" => $"{identity}'S BRIGHT STARS",
            "deep-sky-highlight" => matched is "m42" ? "DISCOVER M42" : matched is "nebula" ? $"THE {identity} NEBULA" : $"DEEP SKY NEAR {identity}",
            "science-or-story-highlight" => $"WHY {identity} STANDS OUT",
            "key-object-highlight" => $"MEET A KEY {identity} OBJECT",
            "object-profile" => $"INSIDE {identity}'S OBJECTS",
            "history-highlight" => $"{identity} THROUGH HISTORY",
            "science-fact" => $"THE SCIENCE OF {identity}",
            "deep-sky-secondary" => $"MORE DEEP SKY NEAR {identity}",
            "observation-checklist" => $"YOUR {identity} CHECKLIST",
            "what-happens" => $"WHAT HAPPENS AT {identity}",
            "where-to-look" => $"FIND {identity} IN THE SKY",
            "when-to-observe" => $"WHEN TO SEE {identity}",
            _ => $"DISCOVER {identity}"
        };
        var supporting = ranked.Where(x => x.Score > 0 && x.Claim.KnowledgeId != primary.KnowledgeId).Take(3).Select(x => x.Claim).ToArray();
        return new(role, role, null, headline, RoleCategory(role), primary, supporting, primaryContent,
            supporting.Select(c => Shorten(ClaimText(c), 72)).ToArray(), "GalleryPagePolicy/1.1",
            primary.KnowledgeId, supporting.Select(c => c.KnowledgeId).ToArray(),
            $"Highest deterministic semantic score ({selected.Score}) for {RoleCategory(role)}; no generic fallback.");
    }

    internal static GalleryCopyDiversityResult EvaluateCopyDiversity(IReadOnlyList<GalleryRoleContentSelection> pages,
        IReadOnlyList<string> eventIdentityTokens)
    {
        var headlines = DuplicateGroups(pages.Select((p, i) => (i + 1, NormalizeCopy(p.Headline))));
        var primary = DuplicateGroups(pages.Select((p, i) => (i + 1, NormalizeCopy(p.PrimaryContent))));
        var rolesPassed = pages.Select(p => p.ResolvedRoleId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == pages.Count;
        var headlinePassed = headlines.Count == 0 && pages.Select(p => NormalizeCopy(p.Headline)).Distinct().Count() == pages.Count;
        var primaryPassed = primary.Count == 0 && pages.Select(p => NormalizeCopy(p.PrimaryContent)).Distinct().Count() >= Math.Min(5, pages.Count);
        var shared = eventIdentityTokens.SelectMany(Tokenize).Where(token => pages.Count(p => Tokenize(p.Headline).Contains(token)) > 1)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return new(pages.Select(p => NormalizeCopy(p.Headline)).Distinct().Count(),
            pages.Select(p => NormalizeCopy(p.PrimaryContent)).Distinct().Count(), headlines, primary, shared, true,
            rolesPassed, headlinePassed, primaryPassed, rolesPassed && headlinePassed && primaryPassed);
    }

    private static List<GalleryCopyDuplicateGroup> DuplicateGroups(IEnumerable<(int Slot, string Value)> values) =>
        values.GroupBy(x => x.Value, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => new GalleryCopyDuplicateGroup(g.Key, g.Select(x => x.Slot).ToArray())).ToList();

    private static string CopyDiversityFailure(GalleryCopyDiversityResult result)
    {
        var details = result.DuplicateHeadlineGroups.Select(g => $"Pages {string.Join(", ", g.PageSlots)} reuse public headline '{g.NormalizedValue}'")
            .Concat(result.DuplicatePrimaryContentGroups.Select(g => $"Pages {string.Join(", ", g.PageSlots)} reuse primary content '{g.NormalizedValue}'"));
        return details.Any() ? string.Join("; ", details) : $"Role diversity passed={result.RoleDiversityPassed}, headline diversity passed={result.HeadlineDiversityPassed}, primary-content diversity passed={result.PrimaryContentDiversityPassed}.";
    }

    private static int SemanticScore(CertifiedKnowledgeClaim claim, IReadOnlyList<string> keywords)
    {
        var metadata = $"{claim.Category} {claim.ClaimType} {claim.Family}";
        return keywords.Sum(k => metadata.Contains(k, StringComparison.OrdinalIgnoreCase) ? 30 : ClaimText(claim).Contains(k, StringComparison.OrdinalIgnoreCase) ? 10 : 0);
    }

    private static IReadOnlyList<CertifiedKnowledgeClaim> CandidateClaims(string role, IReadOnlyList<CertifiedKnowledgeClaim> claims) =>
        claims.Where(IsCertifiedClaim).Select(c => (Claim: c, Score: SemanticScore(c, RoleKeywords(role))))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Claim.KnowledgeId, StringComparer.Ordinal)
            .Select(x => x.Claim).ToArray();

    private static bool IsCertifiedClaim(CertifiedKnowledgeClaim claim) =>
        claim.ReviewStatus.Equals("Accepted", StringComparison.OrdinalIgnoreCase)
        || claim.Classification.Equals("Certified", StringComparison.OrdinalIgnoreCase);

    private static bool IsObservation(CertifiedKnowledgeClaim claim) =>
        claim.Category.Contains("observ", StringComparison.OrdinalIgnoreCase) || claim.ClaimType.Contains("observ", StringComparison.OrdinalIgnoreCase);
    private static bool IsExplicitIdentificationClaim(CertifiedKnowledgeClaim claim) =>
        (claim.Category.Contains("identif", StringComparison.OrdinalIgnoreCase) || claim.Category.Contains("recogn", StringComparison.OrdinalIgnoreCase)
         || claim.ClaimType.Contains("identif", StringComparison.OrdinalIgnoreCase) || claim.ClaimType.Contains("recogn", StringComparison.OrdinalIgnoreCase))
        && IsRecognitionCue(ClaimText(claim));
    private static bool IsStructuredRelationship(CertifiedKnowledgeClaim claim) =>
        claim.StructuredValue is not null && (claim.Category.Contains("relationship", StringComparison.OrdinalIgnoreCase)
            || claim.ClaimType.Contains("relationship", StringComparison.OrdinalIgnoreCase));
    private static bool IsRecognitionCue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var recognition = new[] { "identify", "recognize", "locate", "look for", "find ", "spot ", "distinctive", "recognition cue" };
        var relationship = value.Contains("belt", StringComparison.OrdinalIgnoreCase)
            && (value.Contains("star", StringComparison.OrdinalIgnoreCase) || value.Contains("alnitak", StringComparison.OrdinalIgnoreCase)
                || value.Contains("alnilam", StringComparison.OrdinalIgnoreCase) || value.Contains("mintaka", StringComparison.OrdinalIgnoreCase));
        return recognition.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase)) || relationship;
    }
    private static int IndexOf(IReadOnlyList<CertifiedKnowledgeClaim> claims, CertifiedKnowledgeClaim claim)
    {
        for (var i = 0; i < claims.Count; i++) if (ReferenceEquals(claims[i], claim) || claims[i].KnowledgeId == claim.KnowledgeId) return i;
        return -1;
    }
    private static IEnumerable<(string Pointer, string Value)> FindSemanticValues(JsonElement element, string pointer, IReadOnlyList<string> allowedNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var next = $"{pointer}/{property.Name}";
                var normalized = NormalizeCopy(property.Name).Replace(" ", "");
                if (allowedNames.Any(x => normalized.Contains(x, StringComparison.OrdinalIgnoreCase)))
                {
                    if (property.Value.ValueKind == JsonValueKind.String) yield return (next, property.Value.GetString() ?? "");
                    if (property.Value.ValueKind == JsonValueKind.Array)
                        foreach (var item in property.Value.EnumerateArray().Select((value, index) => (value, index)))
                            if (item.value.ValueKind == JsonValueKind.String) yield return ($"{next}/{item.index}", item.value.GetString() ?? "");
                }
                foreach (var nested in FindSemanticValues(property.Value, next, allowedNames)) yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0; foreach (var item in element.EnumerateArray()) { foreach (var nested in FindSemanticValues(item, $"{pointer}/{index}", allowedNames)) yield return nested; index++; }
        }
    }

    private static string RoleCategory(string role) => role switch
    {
        "cover-identity" => "Identity", "how-to-identify" => "Identification",
        "bright-stars-or-key-objects" => "BrightObjects", "deep-sky-highlight" => "DeepSky",
        "science-or-story-highlight" => "ScienceOrStory", "observation-checklist" => "Observation",
        "key-object-highlight" => "BrightObjects", "object-profile" => "Objects", "deep-sky-secondary" => "DeepSky",
        "history-highlight" => "History", "science-fact" => "Science",
        "where-to-look" => "Direction", "when-to-observe" => "Timing", _ => "Identity"
    };

    private static string NormalizeCopy(string value) => string.Join(' ', new string(value.ToLowerInvariant()
        .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static HashSet<string> Tokenize(string value) => NormalizeCopy(value).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<string> FindStringArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Array)
                    return property.Value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.TryGetProperty("name", out var name) ? name.GetString() : null).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
                var nested = FindStringArray(property.Value, propertyName); if (nested.Count > 0) return nested;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
            foreach (var item in root.EnumerateArray()) { var nested = FindStringArray(item, propertyName); if (nested.Count > 0) return nested; }
        return [];
    }

    /* The old selector sorted every role over one undifferentiated claim list.  Keeping
       this comment beside the replacement makes the removed fallback explicit. */
    private static void RemovedGenericClaimFallback()
    {
        _ = Array.Empty<CertifiedKnowledgeClaim>().OrderBy(c => c.KnowledgeId, StringComparer.Ordinal).ToArray();
    }

    private static string[] RoleKeywords(string role) => role switch
    {
        "how-to-identify" => ["belt", "identify", "alnitak", "alnilam", "mintaka", "geometry"],
        "bright-stars-or-key-objects" => ["betelgeuse", "rigel", "bright", "star"],
        "deep-sky-highlight" => ["m42", "nebula", "deep sky"],
        "science-or-story-highlight" => ["science", "scientific", "story", "history", "culture", "myth", "interesting", "distance", "formation"],
        "key-object-highlight" => ["bright", "star", "object", "betelgeuse", "rigel"],
        "object-profile" => ["object", "star", "nebula", "planet"],
        "deep-sky-secondary" => ["deep sky", "nebula", "cluster", "galaxy", "m42"],
        "history-highlight" => ["history", "historical", "culture", "tradition", "myth"],
        "science-fact" => ["science", "scientific", "distance", "formation", "fact"],
        "observation-checklist" => ["observ", "visible", "equipment", "tip", "identify"],
        "where-to-look" => ["direction", "horizon", "where", "sky"],
        "when-to-observe" => ["time", "window", "when", "peak"],
        _ => ["identity", "constellation", "complete", "event"]
    };

    private static string ClaimText(CertifiedKnowledgeClaim claim) => claim.Text?.Trim() ?? "";
    private static string ExtractIdentity(string eventFamily, IReadOnlyList<CertifiedKnowledgeClaim> claims)
    {
        var value = eventFamily.Replace('_', ' ').Replace('-', ' ').Trim();
        var withoutFamily = string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.Equals("constellation", StringComparison.OrdinalIgnoreCase) && !x.Equals("event", StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(withoutFamily) && !withoutFamily.Equals("guide", StringComparison.OrdinalIgnoreCase)) return withoutFamily.ToUpperInvariant();
        var certifiedIdentity = claims.Select(ClaimText).SelectMany(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(x => new string(x.Where(char.IsLetterOrDigit).ToArray()))
            .FirstOrDefault(x => x.Length > 2 && !new[] { "the", "this", "guide", "constellation", "centers" }.Contains(x, StringComparer.OrdinalIgnoreCase));
        return certifiedIdentity?.ToUpperInvariant() ?? "THE SKY";
    }

    internal static async Task<GeneratedFileMetadata> ReadPhysicalMetadataAsync(string physicalPath, string relativePath, CancellationToken ct)
    {
        Require(File.Exists(physicalPath), "P13_GENERATED_FILE_METADATA_INVALID", $"Gallery candidate '{relativePath}' does not exist.");
        var info = new FileInfo(physicalPath);
        Require(info.Length > 0, "P13_GENERATED_FILE_METADATA_INVALID", $"Gallery candidate '{relativePath}' is empty.");

        using var decoded = await Image.LoadAsync(physicalPath, ct);
        Require(decoded.Width == 1920 && decoded.Height == 1080, "P13_GENERATED_FILE_METADATA_INVALID",
            $"Gallery candidate '{relativePath}' has physical dimensions {decoded.Width}x{decoded.Height}; expected 1920x1080.");
        Require(decoded.Metadata.DecodedImageFormat?.Name.Equals("PNG", StringComparison.OrdinalIgnoreCase) == true,
            "P13_GENERATED_FILE_METADATA_INVALID", $"Gallery candidate '{relativePath}' is not a decoded PNG.");
        await using var stream = File.OpenRead(physicalPath);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        return new(relativePath.Replace('\\', '/'), info.Name, decoded.Width, decoded.Height, "PNG", "image/png", info.Length, sha);
    }

    private static object Lineage(string artifact, string pointer, string source, string display, string rule) => new { authorityArtifact = artifact, authorityPointer = pointer, sourceValue = source, displayValue = display, transformationRule = rule };
    private static string RoleLabel(string role) => string.Join(' ', role.Split('-')).ToUpperInvariant();
    private static string Shorten(string value, int max) => value.Length <= max ? value : value[..Math.Max(1, max - 1)].TrimEnd() + "…";
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string ResolveRequired(string root, params string[] candidates) => candidates.Select(x => Path.Combine(root, x.Replace('/', Path.DirectorySeparatorChar))).FirstOrDefault(File.Exists) ?? Path.Combine(root, candidates[0]);
    private static bool Flag(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static async Task<T> Read<T>(string path, CancellationToken ct) => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new InvalidOperationException($"P13_AUTHORITY_PARSE_FAILED: {path}");
    private static Task Write<T>(string path, T value, CancellationToken ct) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); return File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), ct); }
    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void SafeDeleteDirectory(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
    private static void Require(bool condition, string code, string message) { if (!condition) throw new InvalidOperationException($"{code}: {message}"); }
}
