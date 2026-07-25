using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeDraftValidationFixture
{
    internal static DocumentaryNarrativeDraft Valid() => new DocumentaryNarrativeDraftAssembler().Assemble(OrionDocumentaryNarrativeDraftFixture.Request());
    internal static DocumentaryNarrativeDraft Empty() { var d=Valid(); return Copy(d,[]); }
    internal static DocumentaryNarrativeDraft Copy(DocumentaryNarrativeDraft d,IReadOnlyList<DocumentaryNarrativeDraftSection> sections) =>
        new(d.DraftId,d.CompositionId,d.BlueprintId,d.KnowledgeId,d.SubjectId,d.SubjectName,d.PublicationFormat,d.PrimaryLanguage,d.Version,d.Metadata,sections);
}

public sealed class DocumentaryNarrativeDraftValidatorTests
{
    [Fact] public void Valid_orion_draft_has_no_findings(){var d=OrionDocumentaryNarrativeDraftValidationFixture.Valid();var r=new DocumentaryNarrativeDraftValidator().Validate(d);Assert.Equal(d.DraftId,r.DraftId);Assert.True(r.IsValid);Assert.Equal(0,r.ErrorCount);Assert.Equal(0,r.WarningCount);Assert.Empty(r.Findings);}
    [Fact] public void Null_is_rejected()=>Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraftValidator().Validate(null!));
    [Fact] public void Empty_draft_is_invalid(){var r=new DocumentaryNarrativeDraftValidator().Validate(OrionDocumentaryNarrativeDraftValidationFixture.Empty());Assert.False(r.IsValid);Assert.Equal(2,r.ErrorCount);Assert.Contains(r.Findings,x=>x.RuleCode==DocumentaryNarrativeDraftRuleCodes.SectionsRequired);}
}

public sealed class DocumentaryNarrativeDraftQualityRuleInventoryTests
{
    [Fact] public void Exact_inventory_is_certified()
    {
        var expected=Enumerable.Range(1,18).Select(i=>$"DND-QUALITY-{i:000}");
        Assert.Equal(expected,DocumentaryNarrativeDraftRuleCodes.Inventory.Select(x=>x.Code)); Assert.Equal(18,DocumentaryNarrativeDraftRuleCodes.Inventory.Select(x=>x.Code).Distinct().Count());
        Assert.Equal(new[]{E,E,E,E,E,E,E,E,W,E,W,W,E,W,E,E,E,W},DocumentaryNarrativeDraftRuleCodes.Inventory.Select(x=>x.Severity));
    }
    private const DocumentaryNarrativeDraftValidationSeverity E=DocumentaryNarrativeDraftValidationSeverity.Error,W=DocumentaryNarrativeDraftValidationSeverity.Warning;
}

public sealed class DocumentaryNarrativeDraftValidationResultTests
{
    [Fact] public void Finding_validates_required_optional_and_enum_values(){Assert.Throws<ArgumentException>(()=>F(rule:" "));Assert.Throws<ArgumentException>(()=>F(message:" "));Assert.Throws<ArgumentException>(()=>F(draft:" "));Assert.Throws<ArgumentException>(()=>F(section:" "));Assert.Throws<ArgumentException>(()=>F(passage:" "));Assert.Throws<ArgumentException>(()=>F(field:" "));Assert.Throws<ArgumentOutOfRangeException>(()=>F(severity:(DocumentaryNarrativeDraftValidationSeverity)42));}
    [Fact] public void Result_validates_and_defensively_copies(){var list=new List<DocumentaryNarrativeDraftValidationFinding>{F()};var r=new DocumentaryNarrativeDraftValidationResult("draft",list);list.Clear();Assert.Single(r.Findings);Assert.False(r.IsValid);Assert.Throws<NotSupportedException>(()=>((IList<DocumentaryNarrativeDraftValidationFinding>)r.Findings).Clear());Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraftValidationResult("draft",null!));Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftValidationResult("draft",[null!]));Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftValidationResult("draft",[F(draft:"other")]));}
    [Fact] public void Warnings_alone_remain_valid(){var r=new DocumentaryNarrativeDraftValidationResult("draft",[F(severity:DocumentaryNarrativeDraftValidationSeverity.Warning)]);Assert.True(r.IsValid);Assert.Equal(0,r.ErrorCount);Assert.Equal(1,r.WarningCount);}
    private static DocumentaryNarrativeDraftValidationFinding F(string rule="rule",string message="message",string draft="draft",string? section=null,string? passage=null,string? field=null,DocumentaryNarrativeDraftValidationSeverity severity=DocumentaryNarrativeDraftValidationSeverity.Error)=>new(rule,severity,message,draft,section,null,passage,null,field);
}

public sealed class DocumentaryNarrativeDraftWordCountTests
{
    [Theory][InlineData("Orion shines brightly.",3)][InlineData("  Orion   shines   brightly.  ",3)][InlineData("Orion\tshines\nbrightly.",3)]
    public void Unicode_whitespace_tokens_are_counted(string text,int expected){var method=typeof(DocumentaryNarrativeDraftValidator).GetMethod("CountWords",BindingFlags.Static|BindingFlags.NonPublic)!;Assert.Equal(expected,method.Invoke(null,[text]));}
}

public sealed class DocumentaryNarrativeDraftValidatorDeterminismTests
{
    [Fact] public void Equivalent_runs_have_identical_json(){var v=new DocumentaryNarrativeDraftValidator();var d=OrionDocumentaryNarrativeDraftValidationFixture.Empty();Assert.Equal(JsonSerializer.Serialize(v.Validate(d)),JsonSerializer.Serialize(v.Validate(d)));}
}

public sealed class DocumentaryNarrativeDraftValidatorImmutabilityTests
{
    [Fact] public void Validation_does_not_mutate_draft(){var d=OrionDocumentaryNarrativeDraftValidationFixture.Valid();var before=JsonSerializer.Serialize(d);_=new DocumentaryNarrativeDraftValidator().Validate(d);Assert.Equal(before,JsonSerializer.Serialize(d));}
}

public sealed class DocumentaryNarrativeDraftValidationSerializationTests
{
    [Fact] public void Result_and_findings_round_trip_deterministically(){var value=new DocumentaryNarrativeDraftValidator().Validate(OrionDocumentaryNarrativeDraftValidationFixture.Empty());var json=JsonSerializer.Serialize(value);var copy=JsonSerializer.Deserialize<DocumentaryNarrativeDraftValidationResult>(json)!;Assert.Equal(value.DraftId,copy.DraftId);Assert.Equal(value.Findings.Select(x=>x.RuleCode),copy.Findings.Select(x=>x.RuleCode));Assert.Equal(json,JsonSerializer.Serialize(copy));}
}

public sealed class DocumentaryNarrativeDraftValidatorArchitectureTests
{
    [Fact] public void Validator_boundary_is_exact(){var t=typeof(DocumentaryNarrativeDraftValidator);Assert.True(t.IsSealed);Assert.Empty(Assert.Single(t.GetConstructors()).GetParameters());var m=Assert.Single(t.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly));Assert.Equal("Validate",m.Name);Assert.Equal(typeof(DocumentaryNarrativeDraftValidationResult),m.ReturnType);Assert.Equal(typeof(DocumentaryNarrativeDraft),Assert.Single(m.GetParameters()).ParameterType);Assert.False(typeof(Task).IsAssignableFrom(m.ReturnType));}
    [Fact] public void Contracts_are_read_only_and_have_no_forbidden_properties(){var types=new[]{typeof(DocumentaryNarrativeDraftValidationFinding),typeof(DocumentaryNarrativeDraftValidationResult)};var forbidden=new[]{"ReplacementText","SuggestedText","CorrectedText","AutoFix","Prompt","PromptText","SystemPrompt","UserPrompt","LlmResponse","ModelRequest","ModelParameters","Temperature","TopP","Ssml","Audio","AudioUrl","VoiceId","Subtitle","Srt","Vtt"};Assert.All(types.SelectMany(t=>t.GetProperties()),p=>Assert.False(p.SetMethod?.IsPublic??false));Assert.Empty(types.SelectMany(t=>t.GetProperties()).Where(p=>forbidden.Contains(p.Name,StringComparer.Ordinal)));}
}

public sealed class DocumentaryNarrativeDraftQualityRuleTests
{
    [Fact] public void Empty_draft_reports_rules_in_numeric_order(){var findings=new DocumentaryNarrativeDraftValidator().Validate(OrionDocumentaryNarrativeDraftValidationFixture.Empty()).Findings;Assert.Equal(new[]{DocumentaryNarrativeDraftRuleCodes.SectionsRequired,DocumentaryNarrativeDraftRuleCodes.PositiveTotalDuration},findings.Select(x=>x.RuleCode));Assert.All(findings,x=>{Assert.Equal("narrative-draft.orion.long.v1",x.DraftId);Assert.Equal(DocumentaryNarrativeDraftValidationSeverity.Error,x.Severity);Assert.False(string.IsNullOrWhiteSpace(x.Message));Assert.Null(x.SectionId);Assert.Null(x.PassageId);});}
}
