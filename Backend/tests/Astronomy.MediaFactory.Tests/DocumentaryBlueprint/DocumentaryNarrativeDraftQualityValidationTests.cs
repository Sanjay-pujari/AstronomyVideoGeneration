using System.Collections;
using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeDraftValidationFixture
{
    internal static DocumentaryNarrativeDraft Valid() =>
        new DocumentaryNarrativeDraftAssembler().Assemble(OrionDocumentaryNarrativeDraftFixture.Request());

    internal static DocumentaryNarrativeDraft EmptyDraft() => Draft(Valid(), []);

    internal static DocumentaryNarrativeDraft DraftWithEmptySection(params (string Id, int Number)[] values)
    {
        var source = Valid().Sections[0];
        return Draft(Valid(), values.Select(x => Section(source, x.Id, x.Number, [], 1)).ToArray());
    }

    internal static DocumentaryNarrativeDraft DraftWithPassageNumber(int value) =>
        ReplacePassage(0, 0, p => Passage(p, passageNumber: value, sourceBeatNumber: value));

    internal static DocumentaryNarrativeDraft DraftWithPassageAndSourceBeatNumbers(int passageNumber, int sourceBeatNumber) =>
        ReplacePassage(0, 0, p => Passage(p, passageNumber: passageNumber, sourceBeatNumber: sourceBeatNumber));

    internal static DocumentaryNarrativeDraft DraftWithDuplicatePassageIds(string firstKey, string secondKey, bool caseVariant = false)
    {
        var d = Valid(); var originals = d.Sections.SelectMany(x => x.Passages).ToArray();
        var ids = caseVariant ? new[] { firstKey, firstKey.ToUpperInvariant(), secondKey } : new[] { secondKey, firstKey, secondKey, firstKey };
        return FourPassageDraft(d, ids.Select((id, i) => Passage(originals[i % originals.Length], passageId: id,
            passageNumber: i + 1, sourceBeatId: $"unique-beat-{i}", sourceBeatNumber: i + 1,
            sourceSceneId: $"scene-{i}", text: $"Unique passage text number {i} remains safely above threshold.",
            type: i == 0 ? DocumentaryNarrativePassageType.Opening : i == ids.Length - 1 ? DocumentaryNarrativePassageType.Closing : DocumentaryNarrativePassageType.Explanation)).ToArray());
    }

    internal static DocumentaryNarrativeDraft DraftWithDuplicateSourceBeatIds(string firstKey, string secondKey, bool caseVariant = false)
    {
        var d = Valid(); var originals = d.Sections.SelectMany(x => x.Passages).ToArray();
        var ids = caseVariant ? new[] { firstKey, firstKey.ToUpperInvariant(), secondKey } : new[] { secondKey, firstKey, secondKey, firstKey };
        return FourPassageDraft(d, ids.Select((id, i) => Passage(originals[i % originals.Length], passageId: $"passage-{i}",
            passageNumber: i + 1, sourceBeatId: id, sourceBeatNumber: i + 1, sourceSceneId: $"scene-{i}",
            text: $"Distinct source beat passage number {i} remains fully valid.",
            type: i == 0 ? DocumentaryNarrativePassageType.Opening : i == ids.Length - 1 ? DocumentaryNarrativePassageType.Closing : DocumentaryNarrativePassageType.Explanation)).ToArray());
    }

    internal static DocumentaryNarrativeDraft DraftWithBlankSourceSceneId()
    {
        var draft = Valid(); var malformed = Passage(draft.Sections[0].Passages[0]);
        typeof(DocumentaryNarrativePassage).GetField("<SourceSceneId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(malformed, " ");
        return ReplacePassage(draft, 0, 0, malformed);
    }

    internal static DocumentaryNarrativeDraft DraftWithPassageText(string text) => ReplacePassage(0, 0, p => Passage(p, text: text));

    internal static DocumentaryNarrativeDraft DraftWithDuplicatePassageTexts(string firstKey, string secondKey)
    {
        var d = Valid(); var originals = d.Sections.SelectMany(x => x.Passages).ToArray();
        var texts = new[] { secondKey, firstKey, secondKey, firstKey };
        return FourPassageDraft(d, texts.Select((text, i) => Passage(originals[i % originals.Length], passageId: $"passage-{i}",
            passageNumber: i + 1, sourceBeatId: $"beat-{i}", sourceBeatNumber: i + 1, sourceSceneId: $"scene-{i}", text: text,
            type: i == 0 ? DocumentaryNarrativePassageType.Opening : i == 3 ? DocumentaryNarrativePassageType.Closing : DocumentaryNarrativePassageType.Explanation)).ToArray());
    }

    internal static DocumentaryNarrativeDraft DraftWithConsecutiveTitles(string first, string second) =>
        ReplacePassage(Valid(), 0, 0, p => Passage(p, title: first), 1, 0, p => Passage(p, title: second));

    internal static DocumentaryNarrativeDraft DraftWithOpeningType(DocumentaryNarrativePassageType type) =>
        ReplacePassage(0, 0, p => Passage(p, type: type));

    internal static DocumentaryNarrativeDraft DraftWithClosingType(DocumentaryNarrativePassageType type)
    {
        var d = Valid(); var si = d.Sections.Count - 1; var pi = d.Sections[si].Passages.Count - 1;
        return ReplacePassage(d, si, pi, Passage(d.Sections[si].Passages[pi], type: type));
    }

    internal static DocumentaryNarrativeDraft DraftWithSectionDurations(params int[] durations)
    {
        var d = Valid();
        return Draft(d, d.Sections.Select((s, i) => Section(s, duration: durations[i])).ToArray());
    }

    internal static DocumentaryNarrativeDraft DraftWithPassageDuration(int duration) =>
        ReplacePassage(0, 0, p => Passage(p, duration: duration));

    internal static DocumentaryNarrativeDraft ReplacePassage(int section, int passage, Func<DocumentaryNarrativePassage, DocumentaryNarrativePassage> change) =>
        ReplacePassage(Valid(), section, passage, change(Valid().Sections[section].Passages[passage]));

    internal static DocumentaryNarrativeDraft ReplacePassage(DocumentaryNarrativeDraft draft, int section, int passage, DocumentaryNarrativePassage value)
    {
        var sections = draft.Sections.Select((s, si) => si != section ? s : Section(s, passages: s.Passages.Select((p, pi) => pi == passage ? value : p).ToArray())).ToArray();
        return Draft(draft, sections);
    }

    internal static DocumentaryNarrativeDraft ReplacePassage(DocumentaryNarrativeDraft draft,
        int s1, int p1, Func<DocumentaryNarrativePassage, DocumentaryNarrativePassage> c1,
        int s2, int p2, Func<DocumentaryNarrativePassage, DocumentaryNarrativePassage> c2) =>
        ReplacePassage(ReplacePassage(draft, s1, p1, c1(draft.Sections[s1].Passages[p1])), s2, p2, c2(draft.Sections[s2].Passages[p2]));

    internal static DocumentaryNarrativePassage Passage(DocumentaryNarrativePassage p, string? passageId = null, int? passageNumber = null,
        string? sourceBeatId = null, int? sourceBeatNumber = null, string? sourceSceneId = null, string? title = null,
        string? text = null, DocumentaryNarrativePassageType? type = null, int? duration = null) => new(
        passageId ?? p.PassageId, passageNumber ?? p.PassageNumber, sourceBeatId ?? p.SourceBeatId, sourceBeatNumber ?? p.SourceBeatNumber,
        sourceSceneId ?? p.SourceSceneId, p.SourceSceneNumber, title ?? p.Title, type ?? p.PassageType, p.NarrativeStage, p.SceneRole,
        p.ViewerQuestion, p.Purpose, text ?? p.Text, p.KnowledgeReferences, p.VisualOpportunities, p.Transition, p.EditorialOutcome,
        duration ?? p.EstimatedDurationSeconds);

    internal static DocumentaryNarrativeDraftSection Section(DocumentaryNarrativeDraftSection s, string? id = null, int? number = null,
        IReadOnlyList<DocumentaryNarrativePassage>? passages = null, int? duration = null) => new(
        id ?? s.SectionId, number ?? s.SectionNumber, s.SourceCompositionSectionId, s.Title, s.Purpose, s.NarrativeStage, s.SectionRole,
        passages ?? s.Passages, duration ?? s.EstimatedDurationSeconds);

    internal static DocumentaryNarrativeDraft Draft(DocumentaryNarrativeDraft d, IReadOnlyList<DocumentaryNarrativeDraftSection> sections) => new(
        d.DraftId, d.CompositionId, d.BlueprintId, d.KnowledgeId, d.SubjectId, d.SubjectName, d.PublicationFormat,
        d.PrimaryLanguage, d.Version, d.Metadata, sections);

    private static DocumentaryNarrativeDraft FourPassageDraft(DocumentaryNarrativeDraft d, DocumentaryNarrativePassage[] passages) => Draft(d,
        passages.Select((p, i) => Section(d.Sections[i % d.Sections.Count], $"section-{i}", i + 1, [p], 10)).ToArray());
}

internal static class DraftValidationAssertions
{
    internal static DocumentaryNarrativeDraftValidationFinding Only(DocumentaryNarrativeDraft draft, string code)
    {
        var finding = Assert.Single(new DocumentaryNarrativeDraftValidator().Validate(draft).Findings.Where(x => x.RuleCode == code));
        Assert.Equal(draft.DraftId, finding.DraftId); Assert.False(string.IsNullOrWhiteSpace(finding.Message));
        return finding;
    }

    internal static void DraftScope(DocumentaryNarrativeDraftValidationFinding f, string draftId)
    {
        Assert.Equal(draftId, f.DraftId); Assert.Null(f.SectionId); Assert.Null(f.SectionNumber);
        Assert.Null(f.PassageId); Assert.Null(f.PassageNumber); Assert.Null(f.FieldName); Assert.False(string.IsNullOrWhiteSpace(f.Message));
    }

    internal static void PassageScope(DocumentaryNarrativeDraftValidationFinding f, DocumentaryNarrativeDraft d, int si, int pi, string field)
    {
        var s=d.Sections[si]; var p=s.Passages[pi]; Assert.Equal(d.DraftId,f.DraftId); Assert.Equal(s.SectionId,f.SectionId);
        Assert.Equal(s.SectionNumber,f.SectionNumber); Assert.Equal(p.PassageId,f.PassageId); Assert.Equal(p.PassageNumber,f.PassageNumber);
        Assert.Equal(field,f.FieldName); Assert.False(string.IsNullOrWhiteSpace(f.Message));
    }
}

public sealed class DocumentaryNarrativeDraftQualityRuleTests
{
    [Fact] public void Rule001_sections_required_has_exact_draft_scope(){var d=OrionDocumentaryNarrativeDraftValidationFixture.EmptyDraft();var f=DraftValidationAssertions.Only(d,DocumentaryNarrativeDraftRuleCodes.SectionsRequired);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.DraftScope(f,d.DraftId);}

    [Fact] public void Rule002_empty_sections_are_ordered_by_number_then_ordinal_id()
    {var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithEmptySection(("z",2),("b",1),("a",3));var fs=new DocumentaryNarrativeDraftValidator().Validate(d).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.PassagesRequired).ToArray();Assert.Equal(new[]{"b","z","a"},fs.Select(x=>x.SectionId));Assert.All(fs,f=>{Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);Assert.Equal(d.DraftId,f.DraftId);Assert.NotNull(f.SectionId);Assert.NotNull(f.SectionNumber);Assert.Null(f.PassageId);Assert.Null(f.PassageNumber);Assert.Null(f.FieldName);Assert.False(string.IsNullOrWhiteSpace(f.Message));});}

    [Theory][InlineData(0)][InlineData(-1)] public void Rule003_nonpositive_passage_number_has_exact_scope(int number)
    {var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageNumber(number);var f=DraftValidationAssertions.Only(d,DocumentaryNarrativeDraftRuleCodes.PositivePassageNumbers);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.PassageScope(f,d,0,0,"PassageNumber");Assert.DoesNotContain(new DocumentaryNarrativeDraftValidator().Validate(d).Findings,x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.PassageNumberMatchesBeat);}

    [Fact] public void Rule004_passage_number_must_match_source_beat_number(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageAndSourceBeatNumbers(1,2);var f=DraftValidationAssertions.Only(d,DocumentaryNarrativeDraftRuleCodes.PassageNumberMatchesBeat);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.PassageScope(f,d,0,0,"PassageNumber");}

    [Fact] public void Rule005_duplicate_passage_id_groups_use_exact_ordinal_key_order()
    {var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithDuplicatePassageIds("a-id","z-id");var fs=new DocumentaryNarrativeDraftValidator().Validate(d).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.UniquePassageIds).ToArray();Assert.Equal(2,fs.Length);Assert.Contains("'a-id'",fs[0].Message);Assert.Contains("'z-id'",fs[1].Message);Assert.All(fs,f=>{Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.DraftScope(f,d.DraftId);});var exact=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithDuplicatePassageIds("case","unused",true);Assert.Empty(new DocumentaryNarrativeDraftValidator().Validate(exact).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.UniquePassageIds));}

    [Fact] public void Rule006_duplicate_source_beat_groups_use_exact_ordinal_key_order()
    {var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithDuplicateSourceBeatIds("a-beat","z-beat");var fs=new DocumentaryNarrativeDraftValidator().Validate(d).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.UniqueSourceBeatIds).ToArray();Assert.Equal(2,fs.Length);Assert.Contains("'a-beat'",fs[0].Message);Assert.Contains("'z-beat'",fs[1].Message);Assert.All(fs,f=>{Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.DraftScope(f,d.DraftId);});var exact=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithDuplicateSourceBeatIds("case","unused",true);Assert.Empty(new DocumentaryNarrativeDraftValidator().Validate(exact).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.UniqueSourceBeatIds));}

    [Fact] public void Rule007_blank_source_scene_id_is_certified_without_weakening_constructor(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithBlankSourceSceneId();var f=DraftValidationAssertions.Only(d,DocumentaryNarrativeDraftRuleCodes.SourceSceneIdsRequired);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.PassageScope(f,d,0,0,"SourceSceneId");}

    [Fact] public void Rule008_two_words_errors_and_does_not_emit_rule009(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageText("Orion shines.");var r=new DocumentaryNarrativeDraftValidator().Validate(d);var f=Assert.Single(r.Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.MinimumThreeWords));Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.PassageScope(f,d,0,0,"Text");Assert.DoesNotContain(r.Findings,x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.RecommendedEightWords);}

    [Theory][InlineData("Orion shines brightly.")][InlineData("Orion shines brightly over the winter sky.")] public void Rule009_three_through_seven_words_warn(string text){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageText(text);var f=DraftValidationAssertions.Only(d,DocumentaryNarrativeDraftRuleCodes.RecommendedEightWords);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Warning,f.Severity);DraftValidationAssertions.PassageScope(f,d,0,0,"Text");Assert.True(new DocumentaryNarrativeDraftValidator().Validate(d).IsValid);}
    [Fact] public void Rule009_eight_words_does_not_warn(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageText("Orion shines brightly over the eastern winter horizon.");Assert.DoesNotContain(new DocumentaryNarrativeDraftValidator().Validate(d).Findings,x=>x.RuleCode is DocumentaryNarrativeDraftRuleCodes.MinimumThreeWords or DocumentaryNarrativeDraftRuleCodes.RecommendedEightWords);}

    [Fact] public void Rule010_boundary_is_120_allowed_and_121_error(){var d120=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageText(Words(120));Assert.DoesNotContain(new DocumentaryNarrativeDraftValidator().Validate(d120).Findings,x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.Maximum120Words);var d121=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageText(Words(121));var f=DraftValidationAssertions.Only(d121,DocumentaryNarrativeDraftRuleCodes.Maximum120Words);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.PassageScope(f,d121,0,0,"Text");}

    [Theory][InlineData("orion begins above the eastern winter horizon tonight.",true)][InlineData("  orion begins above the eastern winter horizon tonight.",true)][InlineData("\"orion begins above the eastern winter horizon tonight.",true)][InlineData("...orion begins above the eastern winter horizon tonight.",true)][InlineData("Étoile glows above the eastern winter horizon tonight.",false)][InlineData("1234 -- 5678 !! 9012 ?? 3456.",false)]
    public void Rule011_only_lowercase_first_unicode_letter_warns(string text,bool warns){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageText(text);var fs=new DocumentaryNarrativeDraftValidator().Validate(d).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.UppercaseOpening).ToArray();Assert.Equal(warns?1:0,fs.Length);if(warns){Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Warning,fs[0].Severity);DraftValidationAssertions.PassageScope(fs[0],d,0,0,"Text");}}

    [Theory][InlineData("Orion remains clearly visible above the eastern horizon.",false)][InlineData("Orion remains clearly visible above the eastern horizon?",false)][InlineData("Orion remains clearly visible above the eastern horizon!",false)][InlineData("Orion remains clearly visible above the eastern horizon.  ",false)][InlineData("Orion remains clearly visible above the eastern horizon,",true)][InlineData("Orion remains clearly visible above the eastern horizon:",true)][InlineData("Orion remains clearly visible above the eastern horizon;",true)][InlineData("Orion remains clearly visible above the eastern horizon",true)]
    public void Rule012_terminal_punctuation_boundary(string text,bool warns){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageText(text);var fs=new DocumentaryNarrativeDraftValidator().Validate(d).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.TerminalPunctuation).ToArray();Assert.Equal(warns?1:0,fs.Length);if(warns){Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Warning,fs[0].Severity);DraftValidationAssertions.PassageScope(fs[0],d,0,0,"Text");}}

    [Fact] public void Rule013_duplicate_text_groups_use_exact_ordinal_key_order_and_comparison(){const string a="Alpha text remains unique until copied across separate source beats.";const string z="Zulu text remains unique until copied across separate source beats.";var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithDuplicatePassageTexts(a,z);var fs=new DocumentaryNarrativeDraftValidator().Validate(d).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.UniquePassageText).ToArray();Assert.Equal(2,fs.Length);Assert.All(fs,f=>{Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.DraftScope(f,d.DraftId);});var valid=OrionDocumentaryNarrativeDraftValidationFixture.Valid();var caseDraft=OrionDocumentaryNarrativeDraftValidationFixture.ReplacePassage(valid,0,0,p=>OrionDocumentaryNarrativeDraftValidationFixture.Passage(p,text:"Orion shines brightly above the eastern horizon tonight."),1,0,p=>OrionDocumentaryNarrativeDraftValidationFixture.Passage(p,text:"orion shines brightly above the eastern horizon tonight."));Assert.Empty(new DocumentaryNarrativeDraftValidator().Validate(caseDraft).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.UniquePassageText));var spaceDraft=OrionDocumentaryNarrativeDraftValidationFixture.ReplacePassage(valid,0,0,p=>OrionDocumentaryNarrativeDraftValidationFixture.Passage(p,text:"Orion shines brightly above the eastern horizon tonight."),1,0,p=>OrionDocumentaryNarrativeDraftValidationFixture.Passage(p,text:"Orion shines brightly above the eastern horizon tonight. "));Assert.Empty(new DocumentaryNarrativeDraftValidator().Validate(spaceDraft).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.UniquePassageText));}

    [Fact] public void Rule014_identical_consecutive_titles_warn_on_later_passage_only(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithConsecutiveTitles("Same","Same");var f=DraftValidationAssertions.Only(d,DocumentaryNarrativeDraftRuleCodes.ConsecutiveTitles);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Warning,f.Severity);DraftValidationAssertions.PassageScope(f,d,1,0,"Title");}
    [Fact] public void Rule014_comparison_is_consecutive_case_sensitive_and_ordinal(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithConsecutiveTitles("Same","same");Assert.Empty(new DocumentaryNarrativeDraftValidator().Validate(d).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.ConsecutiveTitles));var valid=OrionDocumentaryNarrativeDraftValidationFixture.Valid();var nonconsecutive=OrionDocumentaryNarrativeDraftValidationFixture.ReplacePassage(valid,0,0,p=>OrionDocumentaryNarrativeDraftValidationFixture.Passage(p,title:"Repeated"),2,0,p=>OrionDocumentaryNarrativeDraftValidationFixture.Passage(p,title:"Repeated"));Assert.Empty(new DocumentaryNarrativeDraftValidator().Validate(nonconsecutive).Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.ConsecutiveTitles));}

    [Fact] public void Rule015_first_passage_must_be_opening(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithOpeningType(DocumentaryNarrativePassageType.Explanation);var f=DraftValidationAssertions.Only(d,DocumentaryNarrativeDraftRuleCodes.OpeningType);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.PassageScope(f,d,0,0,"PassageType");}
    [Fact] public void Rule016_last_passage_must_be_closing(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithClosingType(DocumentaryNarrativePassageType.Explanation);var f=DraftValidationAssertions.Only(d,DocumentaryNarrativeDraftRuleCodes.ClosingType);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.PassageScope(f,d,d.Sections.Count-1,0,"PassageType");}
    [Fact] public void Rule017_total_duration_must_be_positive(){var valid=OrionDocumentaryNarrativeDraftValidationFixture.Valid();var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithSectionDurations(valid.Sections.Select(_=>0).ToArray());var f=DraftValidationAssertions.Only(d,DocumentaryNarrativeDraftRuleCodes.PositiveTotalDuration);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);DraftValidationAssertions.DraftScope(f,d.DraftId);Assert.DoesNotContain(new DocumentaryNarrativeDraftValidator().Validate(valid).Findings,x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.PositiveTotalDuration);}
    [Fact] public void Rule018_zero_passage_duration_is_warning_and_draft_remains_valid(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageDuration(0);var r=new DocumentaryNarrativeDraftValidator().Validate(d);var f=Assert.Single(r.Findings);Assert.Equal(DocumentaryNarrativeDraftRuleCodes.PositivePassageDuration,f.RuleCode);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Warning,f.Severity);DraftValidationAssertions.PassageScope(f,d,0,0,"EstimatedDurationSeconds");Assert.True(r.IsValid);}

    private static string Words(int count)=>string.Join(' ',Enumerable.Range(1,count).Select(i=>i==count?$"word{i}.":$"word{i}"));
}

public sealed class DocumentaryNarrativeDraftValidatorValidityTests
{
    [Fact] public void Valid_draft_has_exact_derived_counts(){var r=new DocumentaryNarrativeDraftValidator().Validate(OrionDocumentaryNarrativeDraftValidationFixture.Valid());Assert.True(r.IsValid);Assert.Equal(0,r.ErrorCount);Assert.Equal(0,r.WarningCount);Assert.Empty(r.Findings);}
    [Fact] public void Warning_only_draft_remains_valid(){var r=new DocumentaryNarrativeDraftValidator().Validate(OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageDuration(0));Assert.True(r.IsValid);Assert.Equal(0,r.ErrorCount);Assert.Equal(1,r.WarningCount);}
    [Fact] public void Error_only_draft_is_invalid(){var r=new DocumentaryNarrativeDraftValidator().Validate(OrionDocumentaryNarrativeDraftValidationFixture.DraftWithOpeningType(DocumentaryNarrativePassageType.Explanation));Assert.False(r.IsValid);Assert.Equal(1,r.ErrorCount);Assert.Equal(0,r.WarningCount);}
    [Fact] public void Mixed_draft_has_exact_derived_counts(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageDuration(0);d=OrionDocumentaryNarrativeDraftValidationFixture.ReplacePassage(d,0,0,OrionDocumentaryNarrativeDraftValidationFixture.Passage(d.Sections[0].Passages[0],type:DocumentaryNarrativePassageType.Explanation,duration:0));var r=new DocumentaryNarrativeDraftValidator().Validate(d);Assert.False(r.IsValid);Assert.Equal(1,r.ErrorCount);Assert.Equal(1,r.WarningCount);}
    [Fact] public void Null_draft_is_rejected()=>Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraftValidator().Validate(null!));
}

public sealed class DocumentaryNarrativeDraftWordCountTests
{
    public static TheoryData<string,int> Cases=>new(){ {"",0},{"   ",0},{"Orion",1},{"Orion shines.",2},{"Orion shines brightly.",3},{"  Orion   shines  ",2},{"Orion\tshines",2},{"Orion\r\nshines",2},{"  Orion shines  ",2},{"Orion\u00a0shines",2},{"Orion\u2003shines",2},{"Orion,",1} };
    [Theory][MemberData(nameof(Cases))] public void CountWords_uses_deterministic_unicode_whitespace_tokens(string text,int expected){var method=typeof(DocumentaryNarrativeDraftValidator).GetMethod("CountWords",BindingFlags.Static|BindingFlags.NonPublic)!;Assert.Equal(expected,method.Invoke(null,[text]));}
}

public sealed class DocumentaryNarrativeDraftValidationFindingTests
{
    [Fact] public void Preserves_every_property(){var f=F(section:"section",sectionNumber:2,passage:"passage",passageNumber:3,field:"Text");Assert.Equal("rule",f.RuleCode);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,f.Severity);Assert.Equal("message",f.Message);Assert.Equal("draft",f.DraftId);Assert.Equal("section",f.SectionId);Assert.Equal(2,f.SectionNumber);Assert.Equal("passage",f.PassageId);Assert.Equal(3,f.PassageNumber);Assert.Equal("Text",f.FieldName);}
    [Fact] public void Rejects_blank_rule_code()=>Assert.Throws<ArgumentException>(()=>F(rule:" "));
    [Fact] public void Rejects_blank_message()=>Assert.Throws<ArgumentException>(()=>F(message:" "));
    [Fact] public void Rejects_blank_draft_id()=>Assert.Throws<ArgumentException>(()=>F(draft:" "));
    [Fact] public void Rejects_undefined_severity()=>Assert.Throws<ArgumentOutOfRangeException>(()=>F(severity:(DocumentaryNarrativeDraftValidationSeverity)42));
    [Fact] public void Rejects_blank_optional_section_id()=>Assert.Throws<ArgumentException>(()=>F(section:" "));
    [Fact] public void Rejects_blank_optional_passage_id()=>Assert.Throws<ArgumentException>(()=>F(passage:" "));
    [Fact] public void Rejects_blank_optional_field_name()=>Assert.Throws<ArgumentException>(()=>F(field:" "));
    internal static DocumentaryNarrativeDraftValidationFinding F(string rule="rule",DocumentaryNarrativeDraftValidationSeverity severity=DocumentaryNarrativeDraftValidationSeverity.Error,string message="message",string draft="draft",string? section=null,int? sectionNumber=null,string? passage=null,int? passageNumber=null,string? field=null)=>new(rule,severity,message,draft,section,sectionNumber,passage,passageNumber,field);
}

public sealed class DocumentaryNarrativeDraftValidationResultTests
{
    private static DocumentaryNarrativeDraftValidationFinding E(string code="error")=>DocumentaryNarrativeDraftValidationFindingTests.F(rule:code);
    private static DocumentaryNarrativeDraftValidationFinding W(string code="warning")=>DocumentaryNarrativeDraftValidationFindingTests.F(rule:code,severity:DocumentaryNarrativeDraftValidationSeverity.Warning);
    [Fact] public void Rejects_blank_draft_id()=>Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftValidationResult(" ",[]));
    [Fact] public void Rejects_null_collection()=>Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraftValidationResult("draft",null!));
    [Fact] public void Rejects_null_element()=>Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftValidationResult("draft",[null!]));
    [Fact] public void Rejects_mismatched_finding_draft()=>Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftValidationResult("other",[E()]));
    [Fact] public void Defensively_copies_caller_list(){var list=new List<DocumentaryNarrativeDraftValidationFinding>{E()};var r=new DocumentaryNarrativeDraftValidationResult("draft",list);list.Clear();Assert.Single(r.Findings);}
    [Fact] public void Exposes_immutable_finding_collection(){var r=new DocumentaryNarrativeDraftValidationResult("draft",[E()]);Assert.Throws<NotSupportedException>(()=>((IList)r.Findings).Clear());}
    [Fact] public void Preserves_caller_order(){var r=new DocumentaryNarrativeDraftValidationResult("draft",[W("z"),E("a")]);Assert.Equal(new[]{"z","a"},r.Findings.Select(x=>x.RuleCode));}
    [Fact] public void Empty_findings_are_valid(){var r=new DocumentaryNarrativeDraftValidationResult("draft",[]);Assert.True(r.IsValid);Assert.Equal(0,r.ErrorCount);Assert.Equal(0,r.WarningCount);}
    [Fact] public void Warnings_only_are_valid(){var r=new DocumentaryNarrativeDraftValidationResult("draft",[W()]);Assert.True(r.IsValid);Assert.Equal(0,r.ErrorCount);Assert.Equal(1,r.WarningCount);}
    [Fact] public void Error_is_invalid(){var r=new DocumentaryNarrativeDraftValidationResult("draft",[E()]);Assert.False(r.IsValid);Assert.Equal(1,r.ErrorCount);Assert.Equal(0,r.WarningCount);}
    [Fact] public void Mixed_counts_are_derived(){var r=new DocumentaryNarrativeDraftValidationResult("draft",[W("w1"),E(),W("w2")]);Assert.False(r.IsValid);Assert.Equal(1,r.ErrorCount);Assert.Equal(2,r.WarningCount);}
}

public sealed class DocumentaryNarrativeDraftValidationSerializationTests
{
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
    [Fact] public void Finding_with_complete_scope_round_trips_every_property_deterministically(){var value=DocumentaryNarrativeDraftValidationFindingTests.F(section:"section",sectionNumber:2,passage:"passage",passageNumber:3,field:"Text");AssertFindingRoundTrip(value);}
    [Fact] public void Draft_scope_finding_round_trips_null_scope_deterministically(){var value=DocumentaryNarrativeDraftValidationFindingTests.F();var copy=AssertFindingRoundTrip(value);Assert.Null(copy.SectionId);Assert.Null(copy.SectionNumber);Assert.Null(copy.PassageId);Assert.Null(copy.PassageNumber);Assert.Null(copy.FieldName);}
    [Fact] public void Mixed_result_round_trips_complete_findings_order_counts_and_json(){var values=new[]{DocumentaryNarrativeDraftValidationFindingTests.F(rule:"draft-error"),DocumentaryNarrativeDraftValidationFindingTests.F(rule:"passage-warning",severity:DocumentaryNarrativeDraftValidationSeverity.Warning,section:"section",sectionNumber:2,passage:"passage",passageNumber:3,field:"Text")};var value=new DocumentaryNarrativeDraftValidationResult("draft",values);var json=JsonSerializer.Serialize(value,Options);var copy=JsonSerializer.Deserialize<DocumentaryNarrativeDraftValidationResult>(json,Options)!;Assert.Equal(value.DraftId,copy.DraftId);Assert.Equal(value.IsValid,copy.IsValid);Assert.Equal(value.ErrorCount,copy.ErrorCount);Assert.Equal(value.WarningCount,copy.WarningCount);Assert.Equal(values.Length,copy.Findings.Count);for(var i=0;i<values.Length;i++)AssertFinding(values[i],copy.Findings[i]);Assert.Equal(json,JsonSerializer.Serialize(copy,Options));}
    private static DocumentaryNarrativeDraftValidationFinding AssertFindingRoundTrip(DocumentaryNarrativeDraftValidationFinding value){var json=JsonSerializer.Serialize(value,Options);var copy=JsonSerializer.Deserialize<DocumentaryNarrativeDraftValidationFinding>(json,Options)!;AssertFinding(value,copy);Assert.Equal(json,JsonSerializer.Serialize(copy,Options));return copy;}
    private static void AssertFinding(DocumentaryNarrativeDraftValidationFinding a,DocumentaryNarrativeDraftValidationFinding b){Assert.Equal(a.RuleCode,b.RuleCode);Assert.Equal(a.Severity,b.Severity);Assert.Equal(a.Message,b.Message);Assert.Equal(a.DraftId,b.DraftId);Assert.Equal(a.SectionId,b.SectionId);Assert.Equal(a.SectionNumber,b.SectionNumber);Assert.Equal(a.PassageId,b.PassageId);Assert.Equal(a.PassageNumber,b.PassageNumber);Assert.Equal(a.FieldName,b.FieldName);}
}

public sealed class DocumentaryNarrativeDraftValidatorDeterminismTests
{
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
    [Fact] public void Same_and_independently_reconstructed_complex_drafts_produce_identical_findings_and_json(){var a=Complex();var b=Complex();var validator=new DocumentaryNarrativeDraftValidator();var first=validator.Validate(a);var repeated=validator.Validate(a);var equivalent=validator.Validate(b);Assert.Equal(first.Findings,repeated.Findings);Assert.Equal(first.Findings,equivalent.Findings);var json=JsonSerializer.Serialize(first,Options);Assert.Equal(json,JsonSerializer.Serialize(repeated,Options));Assert.Equal(json,JsonSerializer.Serialize(equivalent,Options));}
    [Fact] public void Findings_are_in_numeric_rule_order_and_passage_rules_preserve_stored_order(){var r=new DocumentaryNarrativeDraftValidator().Validate(Complex());var numbers=r.Findings.Select(x=>int.Parse(x.RuleCode[^3..])).ToArray();Assert.Equal(numbers.OrderBy(x=>x),numbers);var rule18=r.Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.PositivePassageDuration).ToArray();Assert.Equal(r.Findings.Where(x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.PositivePassageDuration).Select(x=>x.PassageId),rule18.Select(x=>x.PassageId));}
    private static DocumentaryNarrativeDraft Complex(){var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageText("orion shines");return OrionDocumentaryNarrativeDraftValidationFixture.ReplacePassage(d,0,0,OrionDocumentaryNarrativeDraftValidationFixture.Passage(d.Sections[0].Passages[0],text:"orion shines",duration:0,type:DocumentaryNarrativePassageType.Explanation));}
}

public sealed class DocumentaryNarrativeDraftValidatorImmutabilityTests
{
    [Fact] public void Validation_does_not_mutate_any_draft_structure_or_value(){var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var d=OrionDocumentaryNarrativeDraftValidationFixture.DraftWithPassageText("orion shines");var json=JsonSerializer.Serialize(d,options);var metadata=(d.Metadata.CreatedUtc,d.Metadata.CreatedBy,d.Metadata.NarrativeModelVersion,d.Metadata.CompositionVersion,d.Metadata.CompositionSchemaVersion,d.Metadata.DraftSchemaVersion,d.Metadata.CorrelationId);var sections=d.Sections.Select(s=>(s.SectionId,s.SectionNumber,s.EstimatedDurationSeconds)).ToArray();var passages=d.Sections.Select(s=>s.Passages.Select(p=>(p.PassageId,p.PassageNumber,p.SourceBeatId,p.SourceSceneId,p.Text,p.EstimatedDurationSeconds,Knowledge:p.KnowledgeReferences.Select(k=>k.KnowledgeEntryId).ToArray(),Visuals:p.VisualOpportunities.Select(v=>v.Description).ToArray())).ToArray()).ToArray();_=new DocumentaryNarrativeDraftValidator().Validate(d);Assert.Equal(json,JsonSerializer.Serialize(d,options));Assert.Equal(metadata,(d.Metadata.CreatedUtc,d.Metadata.CreatedBy,d.Metadata.NarrativeModelVersion,d.Metadata.CompositionVersion,d.Metadata.CompositionSchemaVersion,d.Metadata.DraftSchemaVersion,d.Metadata.CorrelationId));Assert.Equal(sections,d.Sections.Select(s=>(s.SectionId,s.SectionNumber,s.EstimatedDurationSeconds)));for(var si=0;si<passages.Length;si++)for(var pi=0;pi<passages[si].Length;pi++){var before=passages[si][pi];var p=d.Sections[si].Passages[pi];Assert.Equal(before.PassageId,p.PassageId);Assert.Equal(before.PassageNumber,p.PassageNumber);Assert.Equal(before.SourceBeatId,p.SourceBeatId);Assert.Equal(before.SourceSceneId,p.SourceSceneId);Assert.Equal(before.Text,p.Text);Assert.Equal(before.EstimatedDurationSeconds,p.EstimatedDurationSeconds);Assert.Equal(before.Knowledge,p.KnowledgeReferences.Select(k=>k.KnowledgeEntryId));Assert.Equal(before.Visuals,p.VisualOpportunities.Select(v=>v.Description));}}
}

public sealed class DocumentaryNarrativeDraftQualityRuleInventoryTests
{
    [Fact] public void Exact_constants_values_inventory_order_and_severities_are_certified(){var fields=typeof(DocumentaryNarrativeDraftRuleCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.IsLiteral).OrderBy(f=>f.MetadataToken).ToArray();var names=new[]{"SectionsRequired","PassagesRequired","PositivePassageNumbers","PassageNumberMatchesBeat","UniquePassageIds","UniqueSourceBeatIds","SourceSceneIdsRequired","MinimumThreeWords","RecommendedEightWords","Maximum120Words","UppercaseOpening","TerminalPunctuation","UniquePassageText","ConsecutiveTitles","OpeningType","ClosingType","PositiveTotalDuration","PositivePassageDuration"};var codes=Enumerable.Range(1,18).Select(i=>$"DND-QUALITY-{i:000}").ToArray();Assert.Equal(names,fields.Select(f=>f.Name));Assert.Equal(codes,fields.Select(f=>(string)f.GetRawConstantValue()!));Assert.Equal(codes,DocumentaryNarrativeDraftRuleCodes.Inventory.Select(x=>x.Code));Assert.Equal(new[]{E,E,E,E,E,E,E,E,W,E,W,W,E,W,E,E,E,W},DocumentaryNarrativeDraftRuleCodes.Inventory.Select(x=>x.Severity));Assert.Equal(18,DocumentaryNarrativeDraftRuleCodes.Inventory.Count);}
    private const DocumentaryNarrativeDraftValidationSeverity E=DocumentaryNarrativeDraftValidationSeverity.Error,W=DocumentaryNarrativeDraftValidationSeverity.Warning;
}

public sealed class DocumentaryNarrativeDraftValidatorArchitectureTests
{
    [Fact] public void Finding_and_result_property_inventories_and_severity_enum_are_exact(){Assert.Equal(new[]{"RuleCode","Severity","Message","DraftId","SectionId","SectionNumber","PassageId","PassageNumber","FieldName"},DeclaredProperties(typeof(DocumentaryNarrativeDraftValidationFinding)));Assert.Equal(new[]{"DraftId","Findings","IsValid","ErrorCount","WarningCount"},DeclaredProperties(typeof(DocumentaryNarrativeDraftValidationResult)));Assert.Equal(new[]{"Error","Warning"},Enum.GetNames<DocumentaryNarrativeDraftValidationSeverity>());}
    [Fact] public void Validator_boundary_is_sealed_parameterless_synchronous_stateless_and_read_only(){var t=typeof(DocumentaryNarrativeDraftValidator);Assert.True(t.IsSealed);Assert.Empty(Assert.Single(t.GetConstructors()).GetParameters());Assert.Empty(t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic));Assert.Empty(t.GetProperties(BindingFlags.Instance|BindingFlags.Public));var m=Assert.Single(t.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly));Assert.Equal("Validate",m.Name);Assert.Equal(typeof(DocumentaryNarrativeDraftValidationResult),m.ReturnType);Assert.Equal(typeof(DocumentaryNarrativeDraft),Assert.Single(m.GetParameters()).ParameterType);Assert.False(typeof(Task).IsAssignableFrom(m.ReturnType));}
    [Fact] public void Contracts_expose_no_mutability_or_forbidden_capability(){var types=new[]{typeof(DocumentaryNarrativeDraftValidationFinding),typeof(DocumentaryNarrativeDraftValidationResult)};var forbidden=new[]{"ReplacementText","SuggestedText","CorrectedText","AutoFix","Prompt","PromptText","SystemPrompt","UserPrompt","LlmResponse","RawModelResponse","ModelRequest","ModelParameters","Temperature","TopP","TokenCount","Ssml","Audio","AudioUrl","VoiceId","SpeechRate","Subtitle","Srt","Vtt","TtsText"};Assert.All(types.SelectMany(t=>t.GetProperties()),p=>Assert.False(p.SetMethod?.IsPublic??false));Assert.Empty(types.SelectMany(t=>t.GetProperties()).Where(p=>forbidden.Contains(p.Name,StringComparer.Ordinal)));}
    private static string[] DeclaredProperties(Type t)=>t.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).OrderBy(p=>p.MetadataToken).Select(p=>p.Name).ToArray();
}
