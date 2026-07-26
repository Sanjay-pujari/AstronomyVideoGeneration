using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryCertificationHardeningTests
{
    private const string Correlation = "hardening-correlation";

    [Fact]
    public void Forbidden_capability_inspection_can_fail()
    {
        Assert.False(DocumentaryCertificationValidator.ForbiddenCapabilitiesAbsent([typeof(ForbiddenHttpSurface)]));
        Assert.True(DocumentaryCertificationValidator.ForbiddenCapabilitiesAbsent([typeof(SafeSurface)]));
    }

    [Fact]
    public void Finding_rejects_noncanonical_domain_and_message_code()
    {
        var rule=DocumentaryCertificationRule.CorrelationChainMustBeExact;
        var id=$"{rule}.evidence";
        Assert.Throws<ArgumentException>(()=>new DocumentaryCertificationFinding(id,DocumentaryCertificationDomain.Identity,rule,DocumentaryCertificationSeverity.Error,"CERT-CORRELATION","evidence",0,Correlation));
        Assert.Throws<ArgumentException>(()=>new DocumentaryCertificationFinding(id,DocumentaryCertificationDomain.Correlation,rule,DocumentaryCertificationSeverity.Error,"WRONG","evidence",0,Correlation));
    }

    [Fact]
    public void Decision_rejects_same_finding_id_with_changed_fields()
    {
        var results=PassingResults();var rule=DocumentaryCertificationRule.CorrelationChainMustBeExact;
        var canonical=Finding(rule,0);results[(int)rule]=new(rule,DocumentaryCertificationDomain.Correlation,false,[canonical],(int)rule,Correlation);
        var changed=Finding(rule,1);
        Assert.Throws<ArgumentException>(()=>new DocumentaryCertificationDecision(DocumentaryCertificationStatus.NonCompliant,results,[changed],21,1,22));
    }

    [Fact]
    public void Summary_rejects_wrong_domain_order()
    {
        var domains=DocumentaryCertificationInventory.EvaluatedDomains.Reverse().ToArray();
        Assert.Throws<ArgumentException>(()=>new DocumentaryCertificationSummary("c","p","v","r","x",DocumentaryCertificationStatus.Certified,22,22,0,0,domains,Enum.GetValues<DocumentaryCertificationRule>(),DateTimeOffset.Parse("2026-07-26T00:00:00Z"),"tester",true));
        Assert.DoesNotContain(DocumentaryCertificationDomain.Determinism,DocumentaryCertificationInventory.EvaluatedDomains);
    }

    [Fact]
    public void Operation_validation_rejects_incorrect_method_specification()
    {
        Assert.False(DocumentaryCertificationValidator.OperationValid(typeof(SafeSurface),"Wrong",typeof(string),typeof(string)));
    }

    private static DocumentaryCertificationRuleResult[] PassingResults()=>DocumentaryCertificationInventory.Rules.Select((rule,index)=>new DocumentaryCertificationRuleResult(rule,DocumentaryCertificationInventory.DomainFor(rule),true,[],index,Correlation)).ToArray();
    private static DocumentaryCertificationFinding Finding(DocumentaryCertificationRule rule,int sequence)=>new($"{rule}.evidence",DocumentaryCertificationInventory.DomainFor(rule),rule,DocumentaryCertificationSeverity.Error,DocumentaryCertificationInventory.MessageCodeFor(rule),"evidence",sequence,Correlation);

    private sealed class SafeSurface { public string Run(string value)=>value; }
    private sealed class ForbiddenHttpSurface { public void Send(HttpClient client){} }
}
