using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

/// <summary>Typed adapter between verified authority and the retained six-slot AstroPulse planner.</summary>
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
            hydration.EventAuthority.EventIdentity.EventType, hydration.EventAuthority.EventIdentity.EventFamily,
            hydration.EventAuthority.EventIdentity.Title, intelligence.ShortTitle, hydration.EventAuthority.Metadata.Language,
            hydration.Context.PrimaryObjects, hydration.Context.SecondaryObjects, intelligence.SkyDirectionHint,
            intelligence.LocalPeakTime, intelligence.BestViewingWindowLocal,
            hydration.Context.AllItems.Where(x => x.IsPublicationEligible).ToArray(),
            hydration.Context.AllItems.Where(x => x.IsVisualPlanningEligible).ToArray());
        var objects = semantic.PrimaryObjects.Concat(semantic.SecondaryObjects).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var galleryContext = new AstroPulseGalleryService.GalleryContext(
            semantic.EventType, semantic.Title, intelligence.StoryTheme ?? "", intelligence.VisualTheme ?? "",
            intelligence.EventDate?.ToString("O") ?? "", semantic.LocalPeakTime ?? "", semantic.Direction ?? "",
            semantic.Language, semantic.Language, "UTC",
            EventObjectContextBuilder.FromJsonValues(semantic.EventType, semantic.Title, objects,
                semantic.PrimaryObjects, semantic.SecondaryObjects, intelligence.RequiredVisualObjects ?? []),
            intelligence.ForbiddenTerms, semantic.Title, semantic.EventFamily, LocalizedEventTitle: semantic.Title,
            TitleSource: Phase13GallerySemanticHydrator.Phase2Authority);

        // The mature planner remains authoritative for the canonical slot sequence.
        var contract = AstroPulseGalleryService.GalleryContentResolver.Resolve(galleryContext);
        var topics = AstroPulseGalleryService.BuildTopics(contract);
        return topics.Count == 6 ? topics.Select(topic => CreatePlan(topic, semantic)).ToArray() : [];
    }

    private static Phase13GalleryAuthority.MatureGalleryTopicPlan CreatePlan(
        AstroPulseGalleryService.GalleryTopic topic, MatureGallerySemanticContext context)
    {
        var primary = context.PrimaryObjects.FirstOrDefault() ?? context.Title;
        var facts = context.PublicSemantics.Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(Phase13GalleryAuthority.IsPublicationQualityCopy)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var isConstellation = context.EventFamily.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase)
            || context.EventType.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase);
        var recognition = FindFact(facts, "recogn", "identify", "belt", "pattern", "locat", "distinctive");
        var deepSky = FindFact(facts, "nebula", "m42", "deep sky", "cluster", "galaxy");
        var observation = FindFact(facts, "observ", "notice", "look for", "spot", "view");

        var policy = isConstellation
            ? ResolveConstellation(topic.Purpose, primary, context, recognition, deepSky, observation)
            : ResolveStandard(topic.Purpose, primary, context, facts);
        var authorities = AuthoritiesFor(policy.Detail, policy.Facts, context).ToArray();
        var visualReferences = context.VisualPlanningSemantics.Take(3).Select(Reference).ToArray();
        var prompt = Phase13GalleryAuthority.BuildMatureGalleryPrompt(new(context.Title,
            context.PrimaryObjects.Concat(context.SecondaryObjects).ToArray(), policy.Facts,
            policy.VisualTreatmentId, policy.PromptPurpose, context.EventFamily, policy.Composition));
        return new(topic.Number, topic.Purpose, policy.ResolvedTreatment, policy.Reason,
            topic.LocalizedEducationalRole, policy.Headline, policy.Detail, policy.Facts,
            context.PrimaryObjects.Concat(context.SecondaryObjects).ToArray(), policy.VisualTreatmentId,
            policy.PromptPurpose, policy.TransformationRule, authorities, visualReferences, prompt);
    }

    private sealed record PagePolicy(string ResolvedTreatment, string? Reason, string Headline, string Detail,
        IReadOnlyList<string> Facts, string VisualTreatmentId, string PromptPurpose, string Composition,
        string TransformationRule = "Certified semantics transformed through deterministic role-copy policy; no authority sentence copied as structured contract prose.");

    private static PagePolicy ResolveConstellation(string role, string primary, MatureGallerySemanticContext context,
        string? recognition, string? deepSky, string? observation)
    {
        var objects = CompactObjects(context.SecondaryObjects, primary);
        return role switch
        {
            "Opening view" => new("ConstellationIdentity", null, $"MEET {primary}", $"Trace the full shape of {primary} across the night sky.", [primary],
                "OpeningCinematicWide", "Introduce the complete constellation in a documentary opening view",
                "Wide cinematic night sky, recognizable full constellation, natural horizon, grand documentary scale"),
            "What happens" => new("ConstellationRecognition", "Canonical mechanism role adapted to constellation pattern recognition.",
                recognition?.Contains("belt", StringComparison.OrdinalIgnoreCase) == true ? "START WITH THE BELT" : $"SPOT {primary}'S PATTERN",
                recognition ?? $"Look for the distinctive pattern that defines {primary}.", recognition is null ? [primary] : [recognition],
                "RecognitionPattern", "Explain the constellation's certified recognition pattern",
                "Educational close framing on the recognizable geometry and pattern; emphasize certified recognition features, not a generic star field"),
            "Where to look" when !string.IsNullOrWhiteSpace(context.Direction) => new("CertifiedDirectionalFinding", null,
                $"FIND {primary} IN THE SKY", context.Direction!, [primary], "ObserverDirectionalContext",
                "Place the constellation in its certified observer direction", "Observer at a natural horizon with directional sky context and the constellation visibly framed"),
            "Where to look" => new("ConstellationFindingContext", "No certified direction is available; adapted to recognition without compass claims.",
                $"FIND THE SHAPE OF {primary}", recognition is null ? $"Use {primary}'s recognizable shape as your guide." : $"Finding cue: {recognition}",
                recognition is null ? [primary] : [recognition], "ObserverFindingContext", "Show how an observer recognizes the constellation without invented direction",
                "Observer and cinematic horizon, constellation recognition framing, distinct foreground depth; no compass direction"),
            "When to observe" when !string.IsNullOrWhiteSpace(context.BestViewingWindowLocal) => new("CertifiedViewingWindow", null,
                $"PLAN YOUR {primary} VIEW", context.BestViewingWindowLocal!, [], "TimedObservationProgression",
                "Visualize the certified viewing window", "Time-progression observing scene grounded only in the supplied certified window"),
            "When to observe" when !string.IsNullOrWhiteSpace(context.LocalPeakTime) => new("CertifiedPeakTime", null,
                $"WATCH {primary} AT ITS PEAK", context.LocalPeakTime!, [], "TimedObservationPeak",
                "Visualize the certified peak observation time", "Cinematic observing progression grounded only in the supplied certified time"),
            "When to observe" when deepSky is not null => new("ConstellationDeepSkyHighlight", "No certified timing exists; adapted to a certified deep-sky highlight.",
                deepSky.Contains("m42", StringComparison.OrdinalIgnoreCase) ? "DISCOVER M42" : $"EXPLORE DEEP SKY IN {primary}",
                deepSky, [deepSky], "DeepSkyHighlightClose", "Reveal a certified deep-sky highlight within the constellation",
                "Cinematic close view of the certified deep-sky object, rich nebular detail, not a full constellation field"),
            "When to observe" => new("ConstellationObservationHighlight", "No certified timing exists; adapted to an evergreen certified observation concept.",
                $"NOTICE MORE IN {primary}", observation is not null ? $"Observation highlight: {observation}" : recognition is not null ? $"What to notice: {recognition}" : $"Study the features that make {primary} recognizable.",
                TakePresent(observation, recognition, primary), "ObservationHighlightClose", "Highlight a certified evergreen observation concept",
                "Educational cinematic close treatment with observer context, visibly different from the opening wide field"),
            "Key objects" => new("ConstellationKeyObjects", null, $"EXPLORE {primary}'S KEY OBJECTS", objects.Detail, objects.Facts,
                "KeyObjectCloseup", "Present the constellation's certified objects as a compact visual tour",
                "Close detail-oriented celestial object composition with several distinct certified subjects and strong depth"),
            _ => new("ConstellationViewerTakeaway", "Checklist uses only certified recognition and observation authority.",
                $"YOUR {primary} SKY CHECKLIST", observation is not null ? $"Viewer takeaway: {observation}" : recognition is not null ? $"Recognition recap: {recognition}" : $"Recognize {primary}, then notice its certified key objects.",
                TakePresent(recognition, deepSky, context.SecondaryObjects.FirstOrDefault()), "ViewerChecklistContext",
                "Give the viewer a practical certified recognition recap", "Person under a cinematic night sky and horizon, constellation visible; observer-centric takeaway composition")
        };
    }

    private static PagePolicy ResolveStandard(string role, string primary, MatureGallerySemanticContext context, IReadOnlyList<string> facts)
    {
        var first = facts.FirstOrDefault() ?? context.ShortTitle;
        return role switch
        {
            "Opening view" => new("EventIdentity", null, $"MEET {primary}", $"A cinematic introduction to {primary}.", [primary], "OpeningEventWide", "Introduce the astronomical event", "Wide cinematic documentary opening"),
            "What happens" => new("EventMechanism", null, $"HOW {primary} UNFOLDS", first, facts.Take(2).ToArray(), "EventMechanismDiagrammatic", "Explain the certified event mechanism", "Educational cinematic event mechanism with clear spatial relationships"),
            "Where to look" => new("DirectionalFinding", null, $"WHERE TO FIND {primary}", context.Direction ?? $"Finding context: {first}", [primary], "ObserverDirectionalHorizon", "Show where to look using certified authority", "Observer and horizon with role-specific spatial framing"),
            "When to observe" => new("ObservationTiming", null, $"WHEN TO WATCH {primary}", context.BestViewingWindowLocal ?? context.LocalPeakTime ?? $"Observation context: {first}", [], "ObservationTimeProgression", "Show certified timing or observation progression", "Cinematic temporal progression distinct from other pages"),
            "Key objects" => new("KeyObjects", null, $"OBJECTS TO WATCH", CompactObjects(context.SecondaryObjects, primary).Detail, CompactObjects(context.SecondaryObjects, primary).Facts, "KeyObjectCloseup", "Present certified key objects", "Close object portrait composition"),
            _ => new("ViewerChecklist", null, $"YOUR {primary} CHECKLIST", $"Viewer takeaway: {facts.LastOrDefault() ?? context.ShortTitle}", facts.TakeLast(2).ToArray(), "ViewerChecklistContext", "Recap certified viewing guidance", "Observer-centric cinematic horizon and practical viewing context")
        };
    }

    private static (string Detail, IReadOnlyList<string> Facts) CompactObjects(IReadOnlyList<string> objects, string fallback)
    {
        var values = objects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        if (values.Length == 0) values = [fallback];
        var chunks = values.Chunk(3).Select(chunk => string.Join(" • ", chunk.Select(x => x.Replace(" / ", " • ")))).ToArray();
        return (string.Join("   |   ", chunks), chunks);
    }

    private static string? FindFact(IEnumerable<string> facts, params string[] terms) =>
        facts.FirstOrDefault(f => terms.Any(term => f.Contains(term, StringComparison.OrdinalIgnoreCase)));
    private static IReadOnlyList<string> TakePresent(params string?[] values) => values.Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).Cast<string>().ToArray();

    private static IEnumerable<Phase13GalleryAuthority.GalleryAuthorityReference> AuthoritiesFor(string detail,
        IReadOnlyList<string> facts, MatureGallerySemanticContext context)
    {
        foreach (var value in new[] { detail }.Concat(facts).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item = context.PublicSemantics.FirstOrDefault(x => x.Text.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (item is not null) { yield return Reference(item); continue; }
            var pointer = value == context.Title ? "/eventIdentity/title" : value == context.ShortTitle ? "/intelligence/shortTitle"
                : context.SecondaryObjects.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase)) ? "/intelligence/secondaryObjects"
                : "/eventIdentity/primaryObjects";
            yield return new(Phase13GallerySemanticHydrator.Phase2Authority, pointer, value, "deterministic-publication-transformation");
        }
    }

    private static Phase13GalleryAuthority.GalleryAuthorityReference Reference(Phase13GallerySemanticHydrator.GallerySemanticItem item) =>
        new(item.AuthoritySource, item.AuthorityPath, item.Text, item.Usage.ToString());
}
