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
        var objectSummary = CompactObjectPresentation(context.SecondaryObjects, publicFacts, primary);
        var (treatment, reason, headline, detail, facts, visualTreatmentId, promptPurpose, visualTreatment) = topic.Purpose switch
        {
            "Opening view" => ("EventIdentity", (string?)null, $"MEET {DisplayIdentity(primary)}",
                $"A wide-sky introduction to {DisplayIdentity(primary)}.", new[] { primary },
                "OpeningCinematicWide", "Introduce the event in a wide documentary opening",
                "wide cinematic night sky with the complete subject and a natural horizon"),
            "What happens" when isConstellation => ("ConstellationRecognition", "The canonical mechanism role adapts to constellation pattern recognition.",
                recognition?.Contains("belt", StringComparison.OrdinalIgnoreCase) == true ? "START WITH THE BELT" : $"SPOT {Possessive(primary)} PATTERN",
                recognition is null ? $"Recognize the defining pattern of {DisplayIdentity(primary)}." : $"Pattern cue: {Concise(recognition)}",
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
                    ? $"Use its recognizable shape as the locating cue." : $"Finding cue: {Concise(recognition)}",
                context.SecondaryObjects.Take(3).ToArray(), "ObserverFindingContext",
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
                $"NOTICE WHAT MAKES {DisplayIdentity(primary)} DISTINCT", Concise(publicFacts.Skip(1).FirstOrDefault() ?? $"Explore the defining features of {DisplayIdentity(primary)}."),
                publicFacts.Skip(1).Take(1).ToArray(), "ObservationHighlight", "Present an evergreen certified observation highlight",
                "educational cinematic close treatment rather than a generic wide field"),
            "Key objects" => ("KeyObjectDetail", (string?)null, $"EXPLORE {Possessive(primary)} KEY OBJECTS",
                objectSummary.Detail, objectSummary.Facts, "KeyObjectCloseup", "Present the certified key objects in a compact portrait",
                "close detail-oriented celestial portrait featuring only certified objects"),
            _ => ("ViewerTakeaway", (string?)null, $"YOUR {DisplayIdentity(primary)} SKY CHECK",
                recognition is null ? $"Remember the defining shape and key highlights of {DisplayIdentity(primary)}." : $"Remember this cue: {Concise(recognition)}",
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
            visualTreatmentId, promptPurpose, visualTreatment, authorities, visualReferences, prompt);
    }

    private static bool IsRecognitionFact(string value) => new[] { "recogn", "identify", "spot", "find", "belt", "pattern", "shape" }
        .Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool IsDeepSkyFact(string value) => new[] { "m42", "nebula", "deep sky", "cluster", "galaxy" }
        .Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool IsViewerSemantic(string value) => !new[]
        { "eventType", "primaryObjects", "secondaryObjects", "ProductionEventIntelligence", " is the primary CONSTELLATION object" }
        .Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static string DisplayIdentity(string value) => value.Trim().ToUpperInvariant();
    private static string Possessive(string value) => DisplayIdentity(value) + (value.EndsWith('s') ? "'" : "'S");
    private static string Concise(string value) => value.Length <= 96 ? value.Trim() : value[..95].TrimEnd() + "…";

    private static (string Detail, IReadOnlyList<string> Facts) CompactObjectPresentation(
        IReadOnlyList<string> objects, IReadOnlyList<string> certifiedFacts, string fallback)
    {
        var values = objects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0) return ($"KEY OBJECT • {fallback.ToUpperInvariant()}", new[] { fallback });
        var groups = new List<string>();
        AddCertifiedGroup("BELT", ["alnitak", "alnilam", "mintaka"], "belt");
        AddCertifiedGroup("BRIGHT STARS", ["betelgeuse", "rigel"], "bright star");
        AddCertifiedGroup("DEEP SKY", ["m42", "nebula"], "nebula");
        var groupedNames = groups.SelectMany(group => values.Where(value => group.Contains(value, StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = values.Where(x => !groupedNames.Contains(x)).Take(4).Select(x => x.ToUpperInvariant()).ToArray();
        if (remaining.Length > 0) groups.Add(string.Join(" • ", remaining));
        return (string.Join("   |   ", groups.Take(3)), groups.Take(3).ToArray());

        void AddCertifiedGroup(string label, IReadOnlyList<string> names, string relation)
        {
            var members = values.Where(value => names.Any(name => value.Contains(name, StringComparison.OrdinalIgnoreCase))).ToArray();
            var authorityProvesGroup = certifiedFacts.Any(fact => fact.Contains(relation, StringComparison.OrdinalIgnoreCase)
                && members.Count(member => fact.Contains(member.Split('/')[0].Trim(), StringComparison.OrdinalIgnoreCase)) > 0);
            if (authorityProvesGroup && members.Length > 0) groups.Add($"{label}  {string.Join(" • ", members.Select(x => x.ToUpperInvariant()))}");
        }
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
