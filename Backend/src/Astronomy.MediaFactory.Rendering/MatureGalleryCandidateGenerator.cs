using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

/// <summary>
/// Typed adapter between current semantic authority and the retained AstroPulse Gallery planner.
/// It intentionally has no dependency on the abandoned square-gallery role taxonomy.
/// </summary>
internal static class MatureGalleryCandidateGenerator
{
    internal sealed record MatureGallerySemanticContext(
        string EventType, string EventFamily, string Title, string ShortTitle, string Language,
        IReadOnlyList<string> PrimaryObjects, IReadOnlyList<string> SecondaryObjects,
        string? Direction, string? LocalPeakTime, string? BestViewingWindowLocal,
        IReadOnlyList<Phase13GallerySemanticHydrator.GallerySemanticItem> PublicSemantics,
        IReadOnlyList<Phase13GallerySemanticHydrator.GallerySemanticItem> VisualPlanningSemantics);

    internal static IReadOnlyList<Phase13GalleryAuthority.MatureGalleryTopicPlan> BuildPlans(
        Phase13GallerySemanticHydrator.HydrationResult hydration)
    {
        var intelligence = hydration.EventAuthority.Intelligence;
        var semantic = new MatureGallerySemanticContext(
            hydration.EventAuthority.EventIdentity.EventType,
            hydration.EventAuthority.EventIdentity.EventFamily,
            hydration.EventAuthority.EventIdentity.Title,
            intelligence.ShortTitle,
            hydration.EventAuthority.Metadata.Language,
            hydration.Context.PrimaryObjects,
            hydration.Context.SecondaryObjects,
            intelligence.SkyDirectionHint,
            intelligence.LocalPeakTime,
            intelligence.BestViewingWindowLocal,
            hydration.Context.AllItems.Where(x => x.IsPublicationEligible).ToArray(),
            hydration.Context.AllItems.Where(x => x.IsVisualPlanningEligible).ToArray());

        var objects = semantic.PrimaryObjects.Concat(semantic.SecondaryObjects)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var galleryContext = new AstroPulseGalleryService.GalleryContext(
            semantic.EventType, semantic.Title, intelligence.StoryTheme ?? string.Empty,
            intelligence.VisualTheme ?? string.Empty, intelligence.EventDate?.ToString("O") ?? string.Empty,
            semantic.LocalPeakTime ?? string.Empty, semantic.Direction ?? string.Empty,
            semantic.Language, semantic.Language, "UTC",
            EventObjectContextBuilder.FromJsonValues(semantic.EventType, semantic.Title, objects,
                semantic.PrimaryObjects, semantic.SecondaryObjects, intelligence.RequiredVisualObjects ?? []),
            intelligence.ForbiddenTerms, semantic.Title, semantic.EventFamily,
            LocalizedEventTitle: semantic.Title, TitleSource: Phase13GallerySemanticHydrator.Phase2Authority);

        // GalleryContentResolver + BuildTopics remain the actual six-page role planner.
        var contract = AstroPulseGalleryService.GalleryContentResolver.Resolve(galleryContext);
        var topics = AstroPulseGalleryService.BuildTopics(contract);
        if (topics.Count != 6) return [];

        return topics.Select(topic => CreatePlan(topic, semantic)).ToArray();
    }

    private static Phase13GalleryAuthority.MatureGalleryTopicPlan CreatePlan(
        AstroPulseGalleryService.GalleryTopic topic, MatureGallerySemanticContext context)
    {
        var primary = context.PrimaryObjects.FirstOrDefault() ?? context.Title;
        var publicFacts = context.PublicSemantics.Select(x => x.Text)
            .Where(x => !string.IsNullOrWhiteSpace(x) && IsViewerSemantic(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var shortTitle = string.IsNullOrWhiteSpace(context.ShortTitle) ? context.Title : context.ShortTitle;
        var isConstellation = context.EventType.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase)
            || context.EventFamily.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase);
        var recognition = publicFacts.FirstOrDefault(IsRecognitionFact);
        var deepSky = publicFacts.FirstOrDefault(IsDeepSkyFact);
        var keyObjectEvaluation = EvaluateGalleryKeyObjects(context.EventFamily, primary, context.SecondaryObjects,
            publicFacts, publicFacts, publicFacts, 5);
        var keyObjects = keyObjectEvaluation.Selected;
        var objectSummary = CompactObjectPresentation(keyObjects, primary);
        var (treatment, reason, headline, detail, facts, visualTreatmentId, promptPurpose, visualTreatment) = topic.Purpose switch
        {
            "Opening view" => ("EventIdentity", (string?)null, $"MEET {DisplayIdentity(primary)}",
                NaturalSentence(publicFacts.FirstOrDefault(IsIdentityFact) ?? shortTitle, primary), new[] { primary },
                "OpeningCinematicWide", "Introduce the event in a wide documentary opening",
                "wide cinematic night sky with the complete subject and a natural horizon"),
            "What happens" when isConstellation => ("ConstellationRecognition", "The canonical mechanism role adapts to constellation pattern recognition.",
                recognition?.Contains("belt", StringComparison.OrdinalIgnoreCase) == true ? "START WITH THE BELT" : $"SPOT {Possessive(primary)} PATTERN",
                recognition is null ? $"Look for the recognizable shape of {NaturalName(primary)}." : Concise(recognition),
                recognition is null ? new[] { primary } : new[] { recognition }, "RecognitionPattern",
                "Explain the constellation's recognizable geometry", "tight educational pattern study emphasizing certified geometry rather than a generic star field"),
            "What happens" => ("EventMechanism", (string?)null, $"HOW {DisplayIdentity(primary)} UNFOLDS",
                Concise(publicFacts.FirstOrDefault() ?? shortTitle), publicFacts.Take(2).ToArray(), "EventMechanismDiagram",
                "Explain the event mechanism visually", "cinematic educational mechanism view with clear spatial relationships"),
            "Where to look" when !string.IsNullOrWhiteSpace(context.Direction) => ("CertifiedDirection", (string?)null,
                $"FIND {DisplayIdentity(primary)} IN THE SKY", context.Direction!, new[] { primary }, "ObserverDirectionalContext",
                "Place the event in its certified sky direction", "observer and horizon context emphasizing the certified direction"),
            "Where to look" when isConstellation => ("ConstellationFindingContext",
                "No certified direction is available; the canonical location role adapts to recognition context.",
                $"TRACE {DisplayIdentity(primary)} FROM THE GROUND", recognition is null
                    ? $"Let the shape of {NaturalName(primary)} guide your eye." : $"Use this clue to find it: {Concise(recognition)}",
                keyObjects.Take(3).Select(x => x.DisplayValue).ToArray(), "ObserverFindingContext",
                "Show how an observer can recognize the constellation without compass claims",
                "ground-based observer framing with horizon and recognition cue; visibly different from the opening"),
            "Where to look" => ("IdentityFindingContext", "No certified direction is available; no direction is asserted.",
                $"RECOGNIZE {DisplayIdentity(primary)}", $"Use the event's visible identity as the locating cue.", new[] { primary },
                "ObserverRecognitionContext", "Show recognition context without inventing direction", "observer-scale horizon composition without directional claims"),
            "When to observe" when !string.IsNullOrWhiteSpace(context.BestViewingWindowLocal) => ("CertifiedViewingWindow", (string?)null,
                $"PLAN FOR {DisplayIdentity(primary)}", context.BestViewingWindowLocal!, Array.Empty<string>(), "TimingProgression",
                "Visualize the certified viewing window", "cinematic time-progression composition grounded in the certified viewing window"),
            "When to observe" when !string.IsNullOrWhiteSpace(context.LocalPeakTime) => ("CertifiedPeakTime", (string?)null,
                $"CATCH {DisplayIdentity(primary)} AT PEAK", context.LocalPeakTime!, Array.Empty<string>(), "PeakTimeProgression",
                "Visualize the certified peak time", "cinematic progression toward the certified peak"),
            "When to observe" when isConstellation && deepSky is not null => ("ConstellationDeepSkyHighlight",
                "No certified timing is available; the canonical timing role adapts to a certified deep-sky highlight.",
                deepSky.Contains("m42", StringComparison.OrdinalIgnoreCase) ? "DISCOVER M42" : $"LOOK DEEPER INTO {DisplayIdentity(primary)}",
                Concise(deepSky), new[] { deepSky }, "DeepSkyHighlight",
                "Reveal a certified deep-sky highlight in close detail", "cinematic deep-sky close view centered on the certified nebula or object, not the full constellation"),
            "When to observe" => ("EvergreenObservationHighlight",
                "No certified local time or viewing window is available; the role adapts without inventing timing.",
                $"NOTICE WHAT MAKES {DisplayIdentity(primary)} DISTINCT", Concise(publicFacts.Skip(1).FirstOrDefault() ?? $"Notice the features that distinguish {NaturalName(primary)}."),
                publicFacts.Skip(1).Take(1).ToArray(), "ObservationHighlight", "Present an evergreen certified observation highlight",
                "educational cinematic close treatment rather than a generic wide field"),
            "Key objects" => ("KeyObjectDetail", (string?)null, $"EXPLORE {Possessive(primary)} KEY OBJECTS",
                objectSummary.Detail, objectSummary.Facts, "KeyObjectCloseup", "Present the certified key objects in a compact portrait",
                "close detail-oriented celestial portrait featuring only certified objects"),
            _ => ("ViewerTakeaway", (string?)null, $"YOUR {DisplayIdentity(primary)} SKY CHECK",
                recognition is null ? $"Take away the shape and standout objects of {NaturalName(primary)}." : $"One last clue: {Concise(recognition)}",
                objectSummary.Facts.Take(2).ToArray(), "ViewerChecklistContext", "Conclude with a practical recognition takeaway",
                "observer-centric cinematic horizon with a person beneath the visible subject; no unverified equipment")
        };

        var authorities = AuthoritiesFor(detail, facts, context).ToArray();
        var visualReferences = context.VisualPlanningSemantics.Take(3).Select(Reference).ToArray();
        var prompt = Phase13GalleryAuthority.BuildMatureGalleryPrompt(new(
            context.Title, context.PrimaryObjects.Concat(context.SecondaryObjects).ToArray(), facts,
            $"{visualTreatmentId}: {visualTreatment}", promptPurpose, context.EventFamily,
            "Large role-specific subject, coherent dark-sky lighting, lower-third negative space"));
        return new(topic.Number, topic.Purpose, treatment, reason, topic.LocalizedEducationalRole,
            headline, detail, facts, context.PrimaryObjects.Concat(context.SecondaryObjects).ToArray(),
            visualTreatmentId, promptPurpose, visualTreatment, authorities, visualReferences, prompt,
            topic.Purpose == "Key objects" ? keyObjectEvaluation.Candidates : [],
            topic.Purpose == "Key objects" ? keyObjectEvaluation.SelectedCategoryCount : 0,
            topic.Purpose == "Key objects" ? keyObjectEvaluation.AvailableCategoryCount : 0,
            topic.Purpose != "Key objects" || keyObjectEvaluation.DiversityPassed);
    }

    private static bool IsRecognitionFact(string value) => new[] { "recogn", "identify", "spot", "find", "belt", "pattern", "shape" }
        .Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool IsDeepSkyFact(string value) => new[] { "m42", "nebula", "deep sky", "cluster", "galaxy" }
        .Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool IsIdentityFact(string value) => new[] { "constellation", "asterism", "nebula", "galaxy", "cluster" }
        .Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool IsViewerSemantic(string value) => !new[]
        { "eventType", "primaryObjects", "secondaryObjects", "ProductionEventIntelligence", " is the primary CONSTELLATION object" }
        .Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static string DisplayIdentity(string value) => value.Trim().ToUpperInvariant();
    private static string NaturalName(string value) => System.Globalization.CultureInfo.InvariantCulture.TextInfo
        .ToTitleCase(value.Trim().ToLowerInvariant());
    private static string Possessive(string value) => DisplayIdentity(value) + (value.EndsWith('s') ? "'" : "'S");
    private static string Concise(string value) => value.Length <= 96 ? value.Trim() : value[..95].TrimEnd() + "…";
    private static string NaturalSentence(string value, string primary)
    {
        var text = Concise(value).Replace(primary.ToUpperInvariant(), NaturalName(primary), StringComparison.Ordinal);
        return text.EndsWith('.') ? text : text + ".";
    }

    internal sealed record GalleryKeyObjectSelection(string SourceValue, string DisplayValue, string AuthorityPath,
        string TransformationRule, string Category, int BaseScore, int DiversityBonus, int FinalScore,
        bool Selected, string SelectionReason, IReadOnlyList<string> RankingReasons)
    {
        // Retained for callers of the previous diagnostic contract.
        public int RankScore => FinalScore;
    }

    internal sealed record GalleryKeyObjectEvaluation(IReadOnlyList<GalleryKeyObjectSelection> Candidates,
        IReadOnlyList<GalleryKeyObjectSelection> Selected, int SelectedCategoryCount,
        int AvailableCategoryCount, bool DiversityPassed);

    internal static IReadOnlyList<GalleryKeyObjectSelection> SelectGalleryKeyObjects(string eventFamily,
        string primaryObject, IReadOnlyList<string> certifiedSecondaryObjects,
        IReadOnlyList<string> certifiedObjectClassifications, IReadOnlyList<string> certifiedRelationships,
        IReadOnlyList<string> certifiedDeepSkyIdentities, int pageCapacity)
    {
        return EvaluateGalleryKeyObjects(eventFamily, primaryObject, certifiedSecondaryObjects,
            certifiedObjectClassifications, certifiedRelationships, certifiedDeepSkyIdentities, pageCapacity).Selected;
    }

    internal static GalleryKeyObjectEvaluation EvaluateGalleryKeyObjects(string eventFamily,
        string primaryObject, IReadOnlyList<string> certifiedSecondaryObjects,
        IReadOnlyList<string> certifiedObjectClassifications, IReadOnlyList<string> certifiedRelationships,
        IReadOnlyList<string> certifiedDeepSkyIdentities, int pageCapacity)
    {
        if (pageCapacity <= 0) return new([], [], 0, 0, true);
        var candidates = certifiedSecondaryObjects.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(value =>
            {
                var name = value.Split('/')[0].Trim();
                var classifications = certifiedObjectClassifications
                    .Where(f => f.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                var relationships = certifiedRelationships
                    .Where(f => f.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                var deepSkyIdentities = certifiedDeepSkyIdentities
                    .Where(f => f.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                var mentions = classifications.Concat(relationships).Concat(deepSkyIdentities).ToArray();
                var reasons = new List<string>();
                var score = 20;
                var isDeepSky = IsDeepSkyFact(value) || deepSkyIdentities.Any(IsDeepSkyFact);
                var isRecognition = relationships.Any(f => new[] { "recogn", "identify", "anchor", "belt", "pattern", "shape" }
                    .Any(k => f.Contains(k, StringComparison.OrdinalIgnoreCase)));
                var isProminentStar = classifications.Any(f => f.Contains("star", StringComparison.OrdinalIgnoreCase)
                    && new[] { "bright", "prominent", "major", "key", "important" }.Any(k => f.Contains(k, StringComparison.OrdinalIgnoreCase)));
                var isStar = classifications.Any(f => f.Contains("star", StringComparison.OrdinalIgnoreCase))
                    || relationships.Any(f => f.Contains("star", StringComparison.OrdinalIgnoreCase));
                var category = isDeepSky ? "DeepSkyObject" : isRecognition ? "RecognitionAnchor"
                    : isProminentStar || isStar ? "ProminentOrKeyStar" : "OtherDistinctiveObject";
                if (isProminentStar) { score += 70; reasons.Add("CertifiedProminentOrKeyStar"); }
                else if (isStar) { score += 25; reasons.Add("CertifiedStar"); }
                if (isRecognition) { score += 60; reasons.Add("CertifiedRecognitionAnchor"); }
                if (isDeepSky) { score += 65; reasons.Add("CertifiedDeepSkyIdentity"); }
                if (mentions.Length > 0) { score += 10; reasons.Add("CertifiedAuthorityMention"); }
                if (value.Contains('/')) { score += 5; reasons.Add("AudienceFriendlyCertifiedAlias"); }
                if (reasons.Count == 0) reasons.Add("CertifiedMemberObject");
                var display = value.Contains('/') ? string.Join(" • ", value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(DisplayIdentity)) : DisplayIdentity(value);
                return new GalleryKeyObjectSelection(value, display, "/intelligence/secondaryObjects",
                    value.Contains('/') ? "AstronomyObjectAlias.DisplayNamePlusCatalogId" : "StructuredObjectList.ToRankedGalleryHighlights",
                    category, score, 0, score, false, "Not selected: outside display capacity.", reasons);
            }).ToArray();

        var capacity = Math.Min(pageCapacity, 5);
        var availableCategories = candidates.Select(x => x.Category).Distinct(StringComparer.Ordinal).Count();
        var selected = new List<GalleryKeyObjectSelection>();
        var remaining = candidates.ToList();
        while (selected.Count < capacity && remaining.Count > 0)
        {
            var selectedCategories = selected.Select(x => x.Category).ToHashSet(StringComparer.Ordinal);
            var ranked = remaining.Select(candidate =>
            {
                // A marginal bonus makes a certified category representative outrank a redundant,
                // similarly valuable member without allowing weak uncertified semantics to win.
                var bonus = selectedCategories.Contains(candidate.Category) ? 0 : 35;
                if (candidate.Category == "DeepSkyObject" && !selectedCategories.Contains(candidate.Category)) bonus += 20;
                return candidate with { DiversityBonus = bonus, FinalScore = candidate.BaseScore + bonus };
            }).OrderByDescending(x => x.FinalScore)
              .ThenByDescending(x => x.BaseScore)
              .ThenBy(x => x.SourceValue, StringComparer.OrdinalIgnoreCase).ToArray();
            var winner = ranked[0] with { Selected = true,
                SelectionReason = ranked[0].DiversityBonus > 0
                    ? "Selected by authority score plus category-diversity contribution."
                    : "Selected by certified authority score within display capacity." };
            selected.Add(winner);
            remaining.RemoveAll(x => x.SourceValue.Equals(winner.SourceValue, StringComparison.OrdinalIgnoreCase));
        }

        var selectedMap = selected.ToDictionary(x => x.SourceValue, StringComparer.OrdinalIgnoreCase);
        var diagnostics = candidates.Select(candidate => selectedMap.TryGetValue(candidate.SourceValue, out var item)
            ? item : candidate).OrderByDescending(x => x.FinalScore)
            .ThenBy(x => x.SourceValue, StringComparer.OrdinalIgnoreCase).ToArray();
        var selectedCategoryCount = selected.Select(x => x.Category).Distinct(StringComparer.Ordinal).Count();
        var diversityPassed = selected.Count == 0 || selectedCategoryCount == Math.Min(availableCategories, selected.Count);
        return new(diagnostics, selected, selectedCategoryCount, availableCategories, diversityPassed);
    }

    private static (string Detail, IReadOnlyList<string> Facts) CompactObjectPresentation(
        IReadOnlyList<GalleryKeyObjectSelection> objects, string fallback)
    {
        var values = objects.Select(x => x.DisplayValue).ToArray();
        if (values.Length == 0) return ($"KEY OBJECT • {DisplayIdentity(fallback)}", new[] { fallback });
        return (string.Join(" • ", values), values);
    }

    private static IEnumerable<Phase13GalleryAuthority.GalleryAuthorityReference> AuthoritiesFor(string detail,
        IReadOnlyList<string> facts, MatureGallerySemanticContext context)
    {
        var values = new[] { detail }.Concat(facts).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var item = context.PublicSemantics.FirstOrDefault(x => x.Text.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (item is not null) { yield return Reference(item); continue; }
            var pointer = value == context.Title ? "/eventIdentity/title"
                : value == context.ShortTitle ? "/intelligence/shortTitle"
                : context.SecondaryObjects.Contains(value, StringComparer.OrdinalIgnoreCase) ? "/intelligence/secondaryObjects"
                : "/eventIdentity/primaryObjects";
            yield return new(Phase13GallerySemanticHydrator.Phase2Authority, pointer, value, "verified-event-intelligence");
        }
    }

    private static Phase13GalleryAuthority.GalleryAuthorityReference Reference(
        Phase13GallerySemanticHydrator.GallerySemanticItem item) =>
        new(item.AuthoritySource, item.AuthorityPath, item.Text, item.Usage.ToString());
}
