using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Core.Constellations;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.NarrationCertification;

public sealed record CertificationFact(string Key, string Text, string SourceField);
public sealed record GroundedNarrationCase(Guid PlanId, string Language, string AuthorityLanguage,
    string Source, string EvidenceSha256, IReadOnlyList<CertificationFact> Facts, IReadOnlyList<string> Constraints);
public sealed record CertificationValidation(bool AutomatedGatesPassed, bool RequiresScientificReview,
    int WordCount, double EstimatedSpeechSeconds, string TimingBasis, IReadOnlyList<string> Errors);

// An isolated, closed-corpus test fixture. This is not a new RC2 authority or general factual oracle.
public static class GroundedNarrationCases
{
    public const string Source = "Backend/src/Astronomy.MediaFactory.Core/Constellations/ConstellationFamily.cs#OrionConstellationKnowledgeProvider.GetOrion";

    public static GroundedNarrationCase Create(string language)
    {
        var profile = LanguageProfileResolver.Resolve(language);
        var catalog = new OrionConstellationKnowledgeProvider().GetOrion();
        var nebula = catalog.NotableDeepSkyObjects.Single(x => x.CatalogId == "M42");
        if (nebula.ObjectType != "emission nebula") throw new InvalidOperationException("Reviewed fixture taxonomy changed; review evidence before running.");
        var separation = catalog.EducationalLearningPoints.Single(x => x.Contains("not physically adjacent", StringComparison.Ordinal));
        // Hindi is an independent fixture localization of catalog facts, never an English PlanId/artifact lookup.
        CertificationFact[] facts = profile.LanguageCode == "hi"
            ? [new("classification", "ओरायन एक तारामंडल है। तारामंडल आकाश का एक आधिकारिक क्षेत्र और पहचानने योग्य आकृति है।", "Entity.Classification; EducationalLearningPoints"),
               new("separation", "तारामंडल के तारे आकाश में पास दिखाई देते हैं, लेकिन वे भौतिक रूप से एक ही दूरी पर पास-पास स्थित नहीं होते।", "EducationalLearningPoints"),
               new("nebula", "ओरायन नीहारिका का कैटलॉग नाम M42 है। यह एक उत्सर्जन नीहारिका है, आकाशगंगा नहीं।", "NotableDeepSkyObjects[M42].ObjectType")]
            : [new("classification", "Orion is a constellation: an official sky region and a recognizable pattern.", "Entity.Classification; EducationalLearningPoints"),
               new("separation", separation, "EducationalLearningPoints"),
               new("nebula", $"The {nebula.Name} ({nebula.CatalogId}) is an {nebula.ObjectType}, not a galaxy.", "NotableDeepSkyObjects[M42].ObjectType")];
        var constraints = new[]
        {
            "Preserve all three supplied scientific facts and their qualifications.",
            "No distance, age, size, dates, visibility, brightness, weather or other quantitative evidence is supplied; do not invent it.",
            "Do not turn apparent proximity into physical proximity or identify Orion or M42 as a galaxy.",
            "Write 45 to 100 words in complete sentences. Do not add other scientific claims or objects."
        };
        var evidence = JsonSerializer.Serialize(new { catalog.Entity, catalog.EducationalLearningPoints, nebula, language = profile.LanguageCode, facts, constraints });
        return new(Guid.NewGuid(), profile.LanguageCode, profile.LanguageCode, Source, Hash(evidence), facts, constraints);
    }

    public static NarrationProviderCall BuildRequest(GroundedNarrationCase fixture)
    {
        if (fixture.Language != fixture.AuthorityLanguage || fixture.PlanId == Guid.Empty)
            throw new InvalidOperationException("The fixture authority must belong to its requested language and disposable PlanId.");
        if (fixture.Facts.Count != 3 || fixture.Facts.Any(x => string.IsNullOrWhiteSpace(x.Text) || string.IsNullOrWhiteSpace(x.SourceField))
            || string.IsNullOrWhiteSpace(fixture.Source) || string.IsNullOrWhiteSpace(fixture.EvidenceSha256))
            throw new InvalidOperationException("Verified fixture evidence is missing; do not ask the model to fill gaps.");
        var profile = LanguageProfileResolver.Resolve(fixture.Language);
        var sceneId = $"isolated-{fixture.Language}-{fixture.PlanId:N}";
        var facts = fixture.Facts.Select(x => new NarrationVerifiedFact(x.Key, x.Text, "Scientific explanation")).ToArray();
        var beat = new NarrationContextBeat("ScientificExplanation", "Explain the supplied Orion classification and apparent stellar relationships accurately.",
            "Natural documentary narration", facts, fixture.Constraints, null, "Conclude the explanation naturally.", "warm and precise", "calm",
            ["All supplied facts preserved", "No unsupported claims"], null, sceneId, 1, "short", 35);
        var realization = new NarrationRealizationResult("short", sceneId, "ScientificExplanation", "Constellation", "ScientificExplainer",
            "ScientificExplanation", beat.AudienceOutcome,
            fixture.Facts.Select(x => new RealizedSemanticFact("ScientificFact", "ScientificFact", x.Key, x.Text, SourceFactKey: x.Key)).ToArray(),
            fixture.Constraints, [], null, beat.Tone, beat.NarrativeRhythm, 70, null, null, [], "Begin with the supplied classification.");
        var projection = ProviderSemanticProjection.Project(realization);
        if (!ProviderSemanticProjection.AssessMeaningfulContext(projection, realization).Passed || projection.FactualStatements.Count != fixture.Facts.Count)
            throw new InvalidOperationException("Production semantic projection did not preserve all fixture facts.");
        var context = new NarrationContextDocument("isolated-certification/1.0", "RC2", [new("short", [beat])]);
        var composed = new NarrationPromptComposer().Compose(new(context, [], "", "", LanguageProfile: profile, Realizations: [realization]));
        const string shape = "Generate the Short narration independently. Return exactly one scene, no other text: {\"variant\":\"Short\",\"scenes\":[{\"sceneNumber\":1,\"narrationText\":\"complete narration\"}]}. Use only the supplied evidence. Preserve all required facts and qualifications; no extra claims. Write 45 to 100 words.";
        return new($"isolated-{Guid.NewGuid():N}", $"{fixture.Language}-{fixture.PlanId:N}", "short", composed.PromptPreviewMarkdown,
            shape, Hash(shape), 1, (composed.PromptPreviewMarkdown.Length + shape.Length + 3) / 4, 768);
    }

    public static CertificationValidation Validate(GroundedNarrationCase fixture, string response)
    {
        var errors = new List<string>();
        string text;
        try
        {
            using var json = JsonDocument.Parse(response);
            var root = json.RootElement;
            var scenes = root.GetProperty("scenes");
            if (!Exact(root, "variant", "scenes") || root.GetProperty("variant").GetString() != "Short"
                || scenes.GetArrayLength() != 1 || !Exact(scenes[0], "sceneNumber", "narrationText") || scenes[0].GetProperty("sceneNumber").GetInt32() != 1)
                throw new JsonException("Existing scene contract not satisfied.");
            text = scenes[0].GetProperty("narrationText").GetString() ?? "";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return new(false, true, 0, 0, "Not measured", ["Invalid structured scene contract"]);
        }

        var profile = LanguageProfileResolver.Resolve(fixture.Language);
        if (fixture.Language != fixture.AuthorityLanguage || fixture.Facts.Count != 3) errors.Add("Evidence/language ownership invalid");
        if (!LanguageOutputValidator.Validate(text, profile).Passed) errors.Add("Existing language validation failed");
        if (GeneratedNarrationValidator.Validate(text, "short").Count != 0) errors.Add("Existing provider prose validation failed");
        if (!Regex.IsMatch(text.TrimEnd(), @"[.!?।]$")) errors.Add("Incomplete terminal sentence");
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words is < 45 or > 100) errors.Add("Outside isolated 45–100-word target");
        var withoutCatalogId = Regex.Replace(text, @"M\s*42|एम\s*42|एम\s*४२", "", RegexOptions.IgnoreCase);
        if (Regex.IsMatch(withoutCatalogId, @"\p{Nd}|million|billion|trillion|light[- ]?years?|kilomet|parsec|करोड़|लाख|हजार|प्रकाश.?वर्ष|किलोमीटर|पारसेक", RegexOptions.IgnoreCase))
            errors.Add("Unsupported quantitative claim or distance unit in a fixture with no quantitative evidence");
        if (fixture.Language == "hi")
        {
            if (!Regex.IsMatch(text, "ओरायन|ओरियन") || !text.Contains("तारामंडल")) errors.Add("Missing Hindi constellation classification/terminology");
            if (!text.Contains("नीहारिका") || !Regex.IsMatch(text, @"M\s*42|एम\s*42|एम\s*४२", RegexOptions.IgnoreCase)
                || !text.Contains("उत्सर्जन") || !Regex.IsMatch(text, @"आकाशगंगा\s+नहीं")) errors.Add("M42 nebula classification/galaxy distinction not demonstrated");
            if (!Regex.IsMatch(text, @"दूरी.{0,35}(नहीं|न होते)|अलग.{0,25}दूर", RegexOptions.Singleline)) errors.Add("Stellar distance qualification not demonstrated");
        }
        else
        {
            if (!text.Contains("Orion", StringComparison.OrdinalIgnoreCase) || !text.Contains("constellation", StringComparison.OrdinalIgnoreCase)) errors.Add("Missing constellation classification");
            if (!text.Contains("M42", StringComparison.OrdinalIgnoreCase) || !text.Contains("emission nebula", StringComparison.OrdinalIgnoreCase)
                || !Regex.IsMatch(text, @"not (?:a |an )?galaxy", RegexOptions.IgnoreCase)) errors.Add("M42 nebula classification/galaxy distinction not demonstrated");
            if (!Regex.IsMatch(text, @"not.{0,65}(same|one|adjacent)|different.{0,20}distances", RegexOptions.IgnoreCase)) errors.Add("Stellar distance qualification not demonstrated");
        }
        // Matching required terms is not general entailment. Explicitly reject obvious contradictions,
        // and require human review of every output even when these bounded checks pass.
        if (Regex.IsMatch(text, @"Orion\s+is\s+(?:a\s+)?galaxy|M42\s+is\s+(?:a\s+)?galaxy|ओरायन\s+(?:एक\s+)?आकाशगंगा\s+है", RegexOptions.IgnoreCase))
            errors.Add("Contradictory galaxy classification");
        var estimated = Math.Round(words * 60d / (fixture.Language == "hi" ? 125 : 140), 2);
        return new(errors.Count == 0, true, words, estimated, "Reading-rate estimate only; no TTS or measured audio", errors);
    }

    private static bool Exact(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var actual = value.EnumerateObject().Select(x => x.Name).ToArray();
        return actual.Length == fields.Length && actual.Distinct().Count() == fields.Length && actual.All(fields.Contains);
    }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
