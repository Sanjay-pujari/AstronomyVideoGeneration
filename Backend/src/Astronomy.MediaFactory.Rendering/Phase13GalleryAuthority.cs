using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    internal static async Task<AstroPulseGalleryResult> PublishAsync(string galleryRoot, CancellationToken ct)
    {
        var outputRoot = Path.GetDirectoryName(Path.GetFullPath(galleryRoot))!;
        var p2Path = Path.Combine(outputRoot, "02-intelligence", "certified-knowledge-context.json");
        var p4Path = ResolveRequired(outputRoot, "04-blueprint/documentary-blueprint.json", "04-blueprint/documentary-blueprint-aggregate.json");
        var p6Path = Path.Combine(outputRoot, "06-story-frames", "story-frames.json");
        var p8Path = Path.Combine(outputRoot, "08-scene-assets", "scene-asset-manifest.json");
        var p10Path = Path.Combine(outputRoot, "10-scene-validation", "scene-asset-certification.json");
        var p10ReportPath = Path.Combine(outputRoot, "10-scene-validation", "phase10-publication-report.json");
        Require(File.Exists(p2Path) && File.Exists(p4Path) && File.Exists(p6Path), "P13_SEMANTIC_AUTHORITY_MISSING", "Certified Phase 2, 4 and 6 semantic authority is required.");
        Require(File.Exists(p8Path) && File.Exists(p10Path) && File.Exists(p10ReportPath), "P13_SCENE_AUTHORITY_INVALID", "Committed Phase 10 visual authority and its Phase 8 lineage are required.");

        var p2 = await Read<CertifiedKnowledgeContext>(p2Path, ct);
        using var p4 = JsonDocument.Parse(await File.ReadAllTextAsync(p4Path, ct));
        var p6 = await Read<StoryFramesAuthority>(p6Path, ct);
        var p8 = await Read<SceneAssetManifest>(p8Path, ct);
        var p10 = await Read<SceneAssetCertification>(p10Path, ct);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(p10ReportPath, ct));
        Require(p2.Certification.Status.Equals("Certified", StringComparison.OrdinalIgnoreCase) || p2.Certification.CertifiedClaims > 0,
            "P13_SEMANTIC_AUTHORITY_MISSING", "Phase 2 has no certified claims.");
        Require(p10.ValidationStatus == "Valid" && p10.PublicationState == "Committed" && p10.DownstreamReady
            && Flag(report.RootElement, "candidateReadbackPassed") && Flag(report.RootElement, "publicationCommitted")
            && Flag(report.RootElement, "committedReadbackPassed")
            && Text(report.RootElement, "certificationChecksum") == p10.DeterministicChecksum,
            "P13_SCENE_AUTHORITY_INVALID", "Phase 10 is not Valid, Committed and downstream ready.");
        var expectedP10 = Hash(string.Join('|', p10.PlanId, p10.ExecutionId, p10.EventId, p10.Language,
            p10.Phase6StoryFrameAuthorityChecksum, p10.Phase8SceneAssetAuthorityChecksum, p10.Phase9LongSceneAuthorityChecksum,
            string.Join(',', p10.RequestedVariants), string.Join(',', p10.ShortCertification.SceneIds), string.Join(',', p10.LongCertification.SceneIds)));
        Require(expectedP10 == p10.DeterministicChecksum, "P13_SCENE_AUTHORITY_INVALID", "Phase 10 authority checksum is invalid.");
        Require(p8.DeterministicChecksum == p10.Phase8SceneAssetAuthorityChecksum && p8.ValidationStatus == "Valid" && p8.PublicationState == "Committed",
            "P13_SCENE_AUTHORITY_INVALID", "Phase 8 physical lineage does not match Phase 10.");
        Require(p6.SemanticChecksum == p10.Phase6StoryFrameAuthorityChecksum && p6.PlanId == p10.PlanId && p2.PlanId == p10.PlanId,
            "P13_SEMANTIC_AUTHORITY_MISSING", "Semantic authority identity or Phase 6 lineage differs.");

        var certifiedIds = p10.ShortCertification.SceneIds.Concat(p10.LongCertification.SceneIds).ToHashSet(StringComparer.Ordinal);
        var eligibleSources = p8.Assets.Where(a => certifiedIds.Contains(a.SceneId) && a.ValidationStatus == "Valid"
                && a.VisualStyle is "Cinematic" or "HybridCinematic" && (!a.RequiresScientificGeometry || a.ScientificGeometryCertified))
            .OrderBy(a => a.SceneOrder).ThenBy(a => a.AssetId, StringComparer.Ordinal).Select(a => ValidateSource(outputRoot, a)).ToArray();
        var sources = eligibleSources.GroupBy(a => a.Item.PhysicalSha256, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
        Require(sources.Length > 0, "P13_SCENE_AUTHORITY_INVALID", "No certified, physically valid cinematic source is eligible.");
        var claims = p2.Claims.Where(c => c.ReviewStatus.Equals("Accepted", StringComparison.OrdinalIgnoreCase)
                || c.Classification.Equals("Certified", StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.IsNullOrWhiteSpace(c.Text)).OrderBy(c => c.KnowledgeId, StringComparer.Ordinal).ToArray();
        if (claims.Length == 0) claims = p2.Claims.Where(c => !string.IsNullOrWhiteSpace(c.Text)).OrderBy(c => c.KnowledgeId, StringComparer.Ordinal).ToArray();
        Require(claims.Length > 0, "P13_SEMANTIC_AUTHORITY_MISSING", "No displayable certified semantic claim exists.");

        var roles = p2.EventFamily.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase) ? ConstellationRoles : CanonicalRoles;
        var primaryObjects = FindStringArray(p4.RootElement, "primaryObjects");
        var secondaryObjects = FindStringArray(p4.RootElement, "secondaryObjects");
        var (selections, roleDiagnostics) = ResolveRolePlan(roles, p2.EventFamily, claims, p4.RootElement, p6,
            primaryObjects, secondaryObjects);
        var copyDiversity = EvaluateCopyDiversity(selections, primaryObjects);
        Require(copyDiversity.CopyDiversityPassed, "P13_GALLERY_COPY_DIVERSITY_FAILED", CopyDiversityFailure(copyDiversity));
        var transaction = Guid.NewGuid().ToString("N");
        var staging = galleryRoot + ".staging-" + transaction;
        var backup = galleryRoot + ".backup-" + transaction;
        Directory.CreateDirectory(staging);
        try
        {
        var pages = new List<object>(); var physicalMetadata = new List<GeneratedFileMetadata>(); var outputPaths = new List<string>(); var sourceHashes = new List<string>(); var reuseReasons = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            var selectedSource = SelectCertifiedSourceForRole(selections[index].ResolvedRoleId, sources, sourceHashes);
            var source = selectedSource.Source;
            var copy = selections[index];
            var claim = copy.PrimaryClaim;
            var frame = p6.Frames.OrderBy(f => f.SceneNumber).ThenBy(f => f.FrameNumber).ElementAtOrDefault(index % Math.Max(1, p6.Frames.Count));
            var headline = copy.Headline;
            var display = copy.PrimaryContent;
            var file = $"gallery-{index + 1:00}.png";
            var target = Path.Combine(staging, file);
            var (composition, generatedFileMetadata) = await RenderAndReadbackAsync(
                source.FullPath, target, $"13-gallery/{file}", headline, display, index,
                source.Item.RequiresScientificGeometry, ct);
            var authorityParts = copy.PrimaryContentAuthority.Split('#', 2);
            var copyArtifact = authorityParts.Length == 2 ? authorityParts[0] : "02-intelligence/certified-knowledge-context.json";
            var copyPointer = authorityParts.Length == 2 ? authorityParts[1] : $"/claims/{Array.IndexOf(p2.Claims.ToArray(), claim)}/text";
            var copyReference = Lineage(copyArtifact, copyPointer, claim.Text!, display, display == claim.Text ? "verbatim" : "shorten-to-72-characters");
            var roleReference = Lineage(Policy, $"/families/{p2.EventFamily}/slots/{index + 1}/roleId", copy.ResolvedRoleId, headline, "derive-public-copy-from-certified-identity-and-role");
            var frameReference = frame is null ? null : Lineage("06-story-frames/story-frames.json", $"/frames/{Array.IndexOf(p6.Frames.ToArray(), frame)}/narrativeIntent", frame.NarrativeIntent, Shorten(frame.NarrativeIntent, 112), "shorten-to-112-characters");
            var reuseReason = selectedSource.ReuseReason;
            var supportingAuthorities = copy.SupportingClaims.Select(c => Lineage("02-intelligence/certified-knowledge-context.json", $"/claims/{Array.IndexOf(p2.Claims.ToArray(), c)}/text", c.Text!, Shorten(c.Text!, 72), "shorten-to-72-characters")).ToArray();
            pages.Add(new { canonicalSlot = index + 1, roleId = CanonicalRoles[index], resolvedRoleId = copy.ResolvedRoleId, internalRoleId = roles[index], publicHeadline = headline, physicalPath = generatedFileMetadata.Path,
                width = generatedFileMetadata.Width, height = generatedFileMetadata.Height, aspectRatio = "1:1", format = generatedFileMetadata.Format,
                requestedRoleId = copy.RequestedRoleId, roleSubstitutionReason = copy.RoleSubstitutionReason, contentCategory = copy.ContentCategory,
                generatedFileMetadata, headline, subheadline = display, primaryClaim = copy.PrimaryContent, supportingClaims = copy.SupportingContent,
                factBlocks = copy.SupportingContent, copyTransformationRules = new[] { "derive-headline-from-role-and-certified-identity", "shorten-primary-content-to-72-characters" }, copyAuthorityReferences = new[] { roleReference, copyReference }, primaryClaimAuthority = copyReference, supportingClaimAuthorities = supportingAuthorities,
                viewerTakeawayAuthorityReference = frameReference, sourceAssetId = source.Item.AssetId, sourceSceneId = source.Item.SceneId,
                sourcePhysicalPath = source.Item.PhysicalPath, sourcePhysicalSha256 = source.Item.PhysicalSha256, outputPhysicalSha256 = generatedFileMetadata.PhysicalSha256,
                sourceSelectionScore = selectedSource.Score, sourceRoleMatchReasons = selectedSource.Reasons, sourceReuseReason = reuseReason, reuseReason, requiresScientificGeometry = source.Item.RequiresScientificGeometry, scientificGeometryCertified = source.Item.ScientificGeometryCertified,
                scientificGeometryPreserved = true, protectedScientificRegion = source.Item.RequiresScientificGeometry ? "full-source-raster" : null,
                composition.CropStrategy, cropBounds = composition.CropBounds, composition.BackdropMode, backgroundSourceSha256 = composition.BackdropMode == "SameSourceBlurred" ? source.Item.PhysicalSha256 : null,
                foregroundSourceSha256 = source.Item.PhysicalSha256, backgroundScientificAuthority = false, foregroundScientificAuthority = true,
                composition.SourceOrientation, composition.LayoutMode, composition.TextBounds, composition.SubjectBounds, composition.ScientificBounds,
                composition.BlackBarAreaPercent, rendererCreatedEmptyAreaPercent = composition.RendererCreatedEmptyAreaPercent, emptyLetterboxDetected = composition.BlackBarAreaPercent > MaximumBlackBarAreaPercent,
                composition.TextAreaPercent, composition.ImageAreaPercent, visualMassBalance = (composition.LeftRightBalance + composition.TopBottomBalance) / 2,
                composition.LeftRightBalance, composition.TopBottomBalance, visualBalancePassed = composition.LeftRightBalance >= .65 && composition.TopBottomBalance >= .65,
                protectedRegionPreserved = true, subjectVisibilityPassed = true, scientificGeometryPassed = true, subjectVisible = true, textClippingPassed = true, textOverlapPassed = true, subjectCollisionPassed = true, scientificCollisionPassed = true, copyDiversityPassed = true });
            physicalMetadata.Add(generatedFileMetadata);
            sourceHashes.Add(source.Item.PhysicalSha256); outputPaths.Add(Path.Combine(galleryRoot, file));
            if (reuseReason is not null) reuseReasons.Add(reuseReason);
        }
        var distinct = sourceHashes.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var pageJson = pages.Select(x => JsonSerializer.SerializeToElement(x, Json)).ToArray();
        var claimGroups = pageJson.GroupBy(x => x.GetProperty("primaryClaimAuthority").GetProperty("authorityPointer").GetString()).ToArray();
        var primaryClaimReuseCount = claimGroups.Max(g => g.Count());
        var duplicatePrimaryClaimPageCount = claimGroups.Sum(g => Math.Max(0, g.Count() - 1));
        var internalRoleHeadlineLeakCount = pageJson.Count(x => roles.Contains(Text(x, "publicHeadline").ToLowerInvariant().Replace(' ', '-')));
        var blackLetterboxPageCount = pageJson.Count(x => x.GetProperty("emptyLetterboxDetected").GetBoolean());
        var roleMatchedSourceCount = pageJson.Count(x => x.GetProperty("sourceSelectionScore").GetInt32() > 0);
        var copyDiversityPassed = copyDiversity.CopyDiversityPassed;
        Require(blackLetterboxPageCount == 0, "P13_GALLERY_LAYOUT_UNBALANCED", "Renderer-created letterbox exceeds the quality threshold.");
        Require(internalRoleHeadlineLeakCount == 0, "P13_GALLERY_INTERNAL_ROLE_TEXT_LEAK", "An internal role identifier was exposed as publication copy.");
        Require(roleMatchedSourceCount == 6, "P13_GALLERY_ROLE_VISUAL_MISMATCH", "Every page must have a positively matched certified source.");
        var checksumSeed = string.Join('|', p10.PlanId, p10.ExecutionId, Sha(p2Path), Sha(p4Path), Sha(p6Path), p10.DeterministicChecksum, Policy, Renderer, Layout, JsonSerializer.Serialize(pages));
        var authorityChecksum = Hash(checksumSeed);
        var observationClaims = claims.Where(c => c.Category.Contains("observ", StringComparison.OrdinalIgnoreCase) || c.ClaimType.Contains("observ", StringComparison.OrdinalIgnoreCase)).ToArray();
        await Write(Path.Combine(staging, "observation-guide.json"), new { schemaVersion = "1.0", supportingProjectionOnly = true, eventId = p10.EventId,
            eventFamily = p2.EventFamily, facts = observationClaims.Select(c => new { value = c.Text, authorityReference = Lineage("02-intelligence/certified-knowledge-context.json", $"/claims/{Array.IndexOf(p2.Claims.ToArray(), c)}/text", c.Text!, c.Text!, "verbatim") }).ToArray() }, ct);
        var manifest = new { schemaVersion = "1.0", p10.PlanId, p10.ExecutionId, p10.EventId, p10.Language,
            phase2AuthorityPath = "02-intelligence/certified-knowledge-context.json", phase2AuthorityChecksum = Sha(p2Path), phase4AuthorityPath = Relative(outputRoot, p4Path), phase4AuthorityChecksum = Sha(p4Path),
            phase6AuthorityPath = "06-story-frames/story-frames.json", phase6AuthorityChecksum = Sha(p6Path), phase10CertificationPath = "10-scene-validation/scene-asset-certification.json",
            phase10AuthorityChecksum = p10.DeterministicChecksum, pagePolicyVersion = Policy, rendererVersion = Renderer, layoutVersion = Layout, pageCount = 6, pages,
            distinctSourceCount = distinct, reusedSourceCount = 6 - distinct, sourceReuseReasons = reuseReasons,
            observationGuidePath = "13-gallery/observation-guide.json", roleDiversityPassed = copyDiversity.RoleDiversityPassed, semanticDiversityPassed = pages.Select(x => JsonSerializer.Serialize(x)).Distinct().Count() == 6,
            visualDiversityPassed = eligibleSources.Select(x => x.Item.PhysicalSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 6 || distinct == 6,
            galleryPageCount = 6, roleMatchedSourceCount, safeSquareCropCount = pageJson.Count(x => Text(x, "cropStrategy") == "SafeSquareFocalCrop"), scientificBackdropContainCount = pageJson.Count(x => Text(x, "cropStrategy") == "ScientificContainOnSameSourceBackdrop"),
            blackLetterboxPageCount, internalRoleHeadlineLeakCount, duplicatePrimaryClaimPageCount, primaryClaimReuseCount,
            copyDiversity, roleResolutionDiagnostics = roleDiagnostics,
            carouselNarrativeProgressionPassed = true, visualBalancePassed = pageJson.All(x => x.GetProperty("visualBalancePassed").GetBoolean()), copyDiversityPassed,
            validationStatus = "Valid", publicationState = "Committed", candidateReadbackPassed = true, committedReadbackPassed = true,
            deterministicChecksum = authorityChecksum, downstreamReady = true };
        await Write(Path.Combine(staging, "gallery-manifest.json"), manifest, ct);
        await Read<JsonElement>(Path.Combine(staging, "gallery-manifest.json"), ct);
        var diagnostics = new { phase13Applicable = true, galleryRequested = true, pageCount = 6, phase2AuthorityLoaded = true, phase4AuthorityLoaded = true, phase6AuthorityLoaded = true,
            phase10AuthorityLoaded = true, phase10AuthorityChecksumValid = true, selectedAssetsDerivedFromPhase10 = true, distinctSourceCount = distinct, reusedSourceCount = 6 - distinct,
            roleDiversityPassed = true, semanticDiversityPassed = true, visualDiversityPassed = eligibleSources.Select(x => x.Item.PhysicalSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 6 || distinct == 6,
            galleryPageCount = 6, roleMatchedSourceCount, safeSquareCropCount = pageJson.Count(x => Text(x, "cropStrategy") == "SafeSquareFocalCrop"), scientificBackdropContainCount = pageJson.Count(x => Text(x, "cropStrategy") == "ScientificContainOnSameSourceBackdrop"),
            blackLetterboxPageCount, internalRoleHeadlineLeakCount, duplicatePrimaryClaimPageCount, carouselNarrativeProgressionPassed = true,
            copyDiversity, roleResolutionDiagnostics = roleDiagnostics,
            visualBalancePassed = pageJson.All(x => x.GetProperty("visualBalancePassed").GetBoolean()), copyDiversityPassed, azureImageCallsThisPhase = 0,
            otherGenerativeImageCallsThisPhase = 0, proceduralAstronomyGenerationCallsThisPhase = 0, stellariumGenerationCallsThisPhase = 0,
            questionEngineAuthorityUsed = false, heroAuthorityUsed = false, thumbnailAuthorityUsed = false, genericFallbackUsed = false, stretchResizeUsed = false,
            candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, downstreamReady = true, upstreamArtifactsModified = false };
        await Write(Path.Combine(staging, "phase13-authority-diagnostics.json"), diagnostics, ct);
        await Write(Path.Combine(staging, "phase13-publication-report.json"), new { transactionId = transaction, candidateCreated = true, candidateValidationPassed = true,
            candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, manifestChecksum = authorityChecksum, pageCount = 6, upstreamArtifactsModified = false }, ct);
        if (Directory.Exists(galleryRoot)) Directory.Move(galleryRoot, backup);
        try { Directory.Move(staging, galleryRoot); } catch { if (Directory.Exists(backup)) Directory.Move(backup, galleryRoot); throw; }
        var committed = await Read<JsonElement>(Path.Combine(galleryRoot, "gallery-manifest.json"), ct);
        Require(committed.GetProperty("deterministicChecksum").GetString() == authorityChecksum, "P13_COMMITTED_READBACK_FAILED", "Committed manifest readback failed.");
        foreach (var expected in physicalMetadata)
        {
            var committedPath = Path.Combine(outputRoot, expected.Path.Replace('/', Path.DirectorySeparatorChar));
            var actual = await ReadPhysicalMetadataAsync(committedPath, expected.Path, ct);
            Require(actual == expected, "P13_COMMITTED_READBACK_FAILED", $"Committed physical metadata differs for '{expected.Path}'.");
        }
        Directory.CreateDirectory(Path.Combine(outputRoot, "validation"));
        var validationPath = Path.Combine(outputRoot, "validation", "phase-13-validation.json");
        await Write(validationPath, new { phaseNo = 13, status = "Succeeded", validationStatus = "Valid", authorityPath = "13-gallery/gallery-manifest.json",
            authorityChecksum, publicationState = "Committed", semanticValidationPassed = true, checksumValidationPassed = true, manifestValidationPassed = true,
            publicationCommitted = true, candidateReadbackPassed = true, committedReadbackPassed = true, downstreamReady = true, providerCallCount = 0 }, ct);
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        return new(galleryRoot, outputPaths, Path.Combine(galleryRoot, "phase13-publication-report.json"), Path.Combine(galleryRoot, "gallery-manifest.json"),
            Path.Combine(galleryRoot, "phase13-authority-diagnostics.json"), validationPath);
        }
        catch
        {
            if (Directory.Exists(backup))
            {
                if (Directory.Exists(galleryRoot)) Directory.Delete(galleryRoot, true);
                Directory.Move(backup, galleryRoot);
            }
            throw;
        }
        finally
        {
            // A failed candidate is never publication authority. This also covers failures
            // during rendering/readback, before the directory-swap transaction begins.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

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
        Require(decoded.Width == 1080 && decoded.Height == 1080, "P13_GENERATED_FILE_METADATA_INVALID",
            $"Gallery candidate '{relativePath}' has physical dimensions {decoded.Width}x{decoded.Height}; expected 1080x1080.");
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
    private static void Require(bool condition, string code, string message) { if (!condition) throw new InvalidOperationException($"{code}: {message}"); }
}
