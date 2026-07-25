namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Stateless deterministic inspection of the approved O2.6 draft-quality rules.</summary>
public sealed class DocumentaryNarrativeDraftValidator
{
    public DocumentaryNarrativeDraftValidationResult Validate(DocumentaryNarrativeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var findings=new List<DocumentaryNarrativeDraftValidationFinding>();
        var sections=draft.Sections.OrderBy(s=>s.SectionNumber).ThenBy(s=>s.SectionId,StringComparer.Ordinal).ToArray();
        var passages=draft.Sections.SelectMany(s=>s.Passages.Select(p=>(Section:s,Passage:p))).ToArray();
        void Add(string code, DocumentaryNarrativeDraftValidationSeverity severity, string message,
            (DocumentaryNarrativeDraftSection Section, DocumentaryNarrativePassage Passage)? item=null,
            DocumentaryNarrativeDraftSection? section=null, string? field=null) => findings.Add(new(code,severity,message,draft.DraftId,
                item?.Section.SectionId ?? section?.SectionId,item?.Section.SectionNumber ?? section?.SectionNumber,
                item?.Passage.PassageId,item?.Passage.PassageNumber,field));

        if(draft.Sections.Count==0) Add(C.SectionsRequired,E,"Draft must contain at least one section.");
        foreach(var s in sections.Where(s=>s.Passages.Count==0)) Add(C.PassagesRequired,E,"Section must contain at least one passage.",section:s);
        foreach(var x in passages.Where(x=>x.Passage.PassageNumber<=0)) Add(C.PositivePassageNumbers,E,"Passage number must be positive.",x,field:nameof(x.Passage.PassageNumber));
        foreach(var x in passages.Where(x=>x.Passage.PassageNumber!=x.Passage.SourceBeatNumber)) Add(C.PassageNumberMatchesBeat,E,"Passage number must match its source beat number.",x,field:nameof(x.Passage.PassageNumber));
        foreach(var g in passages.GroupBy(x=>x.Passage.PassageId,StringComparer.Ordinal).Where(g=>g.Count()>1).OrderBy(g=>g.Key,StringComparer.Ordinal)) Add(C.UniquePassageIds,E,$"Passage ID is repeated across the draft: '{g.Key}'.");
        foreach(var g in passages.GroupBy(x=>x.Passage.SourceBeatId,StringComparer.Ordinal).Where(g=>g.Count()>1).OrderBy(g=>g.Key,StringComparer.Ordinal)) Add(C.UniqueSourceBeatIds,E,$"Source beat ID is repeated across the draft: '{g.Key}'.");
        foreach(var x in passages.Where(x=>string.IsNullOrWhiteSpace(x.Passage.SourceSceneId))) Add(C.SourceSceneIdsRequired,E,"Source scene ID must be present.",x,field:nameof(x.Passage.SourceSceneId));
        foreach(var x in passages.Where(x=>CountWords(x.Passage.Text)<3)) Add(C.MinimumThreeWords,E,"Passage text must contain at least three words.",x,field:nameof(x.Passage.Text));
        foreach(var x in passages.Where(x=>CountWords(x.Passage.Text) is >=3 and <8)) Add(C.RecommendedEightWords,W,"Passage text should contain at least eight words.",x,field:nameof(x.Passage.Text));
        foreach(var x in passages.Where(x=>CountWords(x.Passage.Text)>120)) Add(C.Maximum120Words,E,"Passage text must not exceed 120 words.",x,field:nameof(x.Passage.Text));
        foreach(var x in passages.Where(x=>x.Passage.PassageType==DocumentaryNarrativePassageType.Opening && FirstLetter(x.Passage.Text) is char ch && char.IsLower(ch))) Add(C.UppercaseOpening,W,"Opening passage should not begin with a lowercase letter.",x,field:nameof(x.Passage.Text));
        foreach(var x in passages.Where(x=>!HasTerminalPunctuation(x.Passage.Text))) Add(C.TerminalPunctuation,W,"Passage text should end with terminal punctuation.",x,field:nameof(x.Passage.Text));
        foreach(var g in passages.GroupBy(x=>x.Passage.Text,StringComparer.Ordinal).Where(g=>g.Select(x=>x.Passage.SourceBeatId).Distinct(StringComparer.Ordinal).Count()>1).OrderBy(g=>g.Key,StringComparer.Ordinal)) Add(C.UniquePassageText,E,"Passage text must not be repeated exactly across different beats.");
        for(var i=1;i<passages.Length;i++) if(string.Equals(passages[i-1].Passage.Title,passages[i].Passage.Title,StringComparison.Ordinal)) Add(C.ConsecutiveTitles,W,"Consecutive passages should not have identical titles.",passages[i],field:nameof(DocumentaryNarrativePassage.Title));
        if(passages.Length>0 && passages[0].Passage.PassageType!=DocumentaryNarrativePassageType.Opening) Add(C.OpeningType,E,"First passage must use the Opening type.",passages[0],field:nameof(DocumentaryNarrativePassage.PassageType));
        if(passages.Length>0 && passages[^1].Passage.PassageType!=DocumentaryNarrativePassageType.Closing) Add(C.ClosingType,E,"Last passage must use the Closing type.",passages[^1],field:nameof(DocumentaryNarrativePassage.PassageType));
        long duration=draft.Sections.Sum(s=>(long)s.EstimatedDurationSeconds); if(duration<=0) Add(C.PositiveTotalDuration,E,"Estimated draft duration must be positive.");
        foreach(var x in passages.Where(x=>x.Passage.EstimatedDurationSeconds==0)) Add(C.PositivePassageDuration,W,"Passage estimated duration should be positive.",x,field:nameof(x.Passage.EstimatedDurationSeconds));
        return new(draft.DraftId,findings);
    }

    internal static int CountWords(string text)
    {
        ArgumentNullException.ThrowIfNull(text); var count=0; var inWord=false;
        foreach(var character in text) { if(char.IsWhiteSpace(character)) inWord=false; else if(!inWord) { count++; inWord=true; } }
        return count;
    }
    private static char? FirstLetter(string text) { foreach(var character in text) if(char.IsLetter(character)) return character; return null; }
    private static bool HasTerminalPunctuation(string text) { for(var i=text.Length-1;i>=0;i--) if(!char.IsWhiteSpace(text[i])) return text[i] is '.' or '?' or '!'; return false; }
    private static class C { public const string SectionsRequired=DocumentaryNarrativeDraftRuleCodes.SectionsRequired,PassagesRequired=DocumentaryNarrativeDraftRuleCodes.PassagesRequired,PositivePassageNumbers=DocumentaryNarrativeDraftRuleCodes.PositivePassageNumbers,PassageNumberMatchesBeat=DocumentaryNarrativeDraftRuleCodes.PassageNumberMatchesBeat,UniquePassageIds=DocumentaryNarrativeDraftRuleCodes.UniquePassageIds,UniqueSourceBeatIds=DocumentaryNarrativeDraftRuleCodes.UniqueSourceBeatIds,SourceSceneIdsRequired=DocumentaryNarrativeDraftRuleCodes.SourceSceneIdsRequired,MinimumThreeWords=DocumentaryNarrativeDraftRuleCodes.MinimumThreeWords,RecommendedEightWords=DocumentaryNarrativeDraftRuleCodes.RecommendedEightWords,Maximum120Words=DocumentaryNarrativeDraftRuleCodes.Maximum120Words,UppercaseOpening=DocumentaryNarrativeDraftRuleCodes.UppercaseOpening,TerminalPunctuation=DocumentaryNarrativeDraftRuleCodes.TerminalPunctuation,UniquePassageText=DocumentaryNarrativeDraftRuleCodes.UniquePassageText,ConsecutiveTitles=DocumentaryNarrativeDraftRuleCodes.ConsecutiveTitles,OpeningType=DocumentaryNarrativeDraftRuleCodes.OpeningType,ClosingType=DocumentaryNarrativeDraftRuleCodes.ClosingType,PositiveTotalDuration=DocumentaryNarrativeDraftRuleCodes.PositiveTotalDuration,PositivePassageDuration=DocumentaryNarrativeDraftRuleCodes.PositivePassageDuration; }
    private const DocumentaryNarrativeDraftValidationSeverity E=DocumentaryNarrativeDraftValidationSeverity.Error,W=DocumentaryNarrativeDraftValidationSeverity.Warning;
}
