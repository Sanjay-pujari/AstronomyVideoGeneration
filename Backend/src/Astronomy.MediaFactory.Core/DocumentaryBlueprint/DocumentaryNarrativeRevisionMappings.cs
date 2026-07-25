namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryNarrativeRevisionMappings
{
    public static DocumentaryNarrativeRevisionAction Action(string ruleCode)=>ruleCode switch
    {
        "DND-QUALITY-001" or "DND-QUALITY-002"=>DocumentaryNarrativeRevisionAction.ReviewDraftStructure,
        "DND-QUALITY-003" or "DND-QUALITY-004"=>DocumentaryNarrativeRevisionAction.CorrectPassageNumber,
        "DND-QUALITY-005" or "DND-QUALITY-006" or "DND-QUALITY-007"=>DocumentaryNarrativeRevisionAction.CorrectSourceIdentity,
        "DND-QUALITY-008" or "DND-QUALITY-009" or "DND-QUALITY-010"=>DocumentaryNarrativeRevisionAction.RevisePassageText,
        "DND-QUALITY-011"=>DocumentaryNarrativeRevisionAction.RevisePassageOpening,
        "DND-QUALITY-012"=>DocumentaryNarrativeRevisionAction.AddTerminalPunctuation,
        "DND-QUALITY-013"=>DocumentaryNarrativeRevisionAction.DifferentiatePassageText,
        "DND-QUALITY-014"=>DocumentaryNarrativeRevisionAction.DifferentiatePassageTitle,
        "DND-QUALITY-015" or "DND-QUALITY-016"=>DocumentaryNarrativeRevisionAction.CorrectPassageType,
        "DND-QUALITY-017" or "DND-QUALITY-018"=>DocumentaryNarrativeRevisionAction.ReviewDuration,
        _=>throw new ArgumentOutOfRangeException(nameof(ruleCode),ruleCode,"Unknown narrative draft quality rule code.")
    };
    public static bool RequiresPassageText(DocumentaryNarrativeRevisionAction action)=>action switch
    { DocumentaryNarrativeRevisionAction.RevisePassageText or DocumentaryNarrativeRevisionAction.RevisePassageOpening or DocumentaryNarrativeRevisionAction.AddTerminalPunctuation or DocumentaryNarrativeRevisionAction.DifferentiatePassageText=>true,
      DocumentaryNarrativeRevisionAction.ReviewDraftStructure or DocumentaryNarrativeRevisionAction.DifferentiatePassageTitle or DocumentaryNarrativeRevisionAction.CorrectPassageType or DocumentaryNarrativeRevisionAction.CorrectPassageNumber or DocumentaryNarrativeRevisionAction.CorrectSourceIdentity or DocumentaryNarrativeRevisionAction.ReviewDuration=>false,
      _=>throw new ArgumentOutOfRangeException(nameof(action)) };
}
