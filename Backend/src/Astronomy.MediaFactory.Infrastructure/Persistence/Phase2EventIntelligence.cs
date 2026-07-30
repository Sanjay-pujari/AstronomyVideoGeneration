using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ProductionEventFamilyResolver : IProductionEventFamilyResolver
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CONSTELLATION"]="CONSTELLATION", ["METEORSHOWER"]="METEOR_SHOWER", ["METEOR_SHOWER"]="METEOR_SHOWER",
        ["PLANETCONJUNCTION"]="PLANET_CONJUNCTION", ["PLANET_CONJUNCTION"]="PLANET_CONJUNCTION", ["CONJUNCTION"]="PLANET_CONJUNCTION",
        ["PLANETPAIRING"]="PLANET_PAIRING", ["PLANET_PAIRING"]="PLANET_PAIRING", ["CLOSEAPPROACH"]="PLANET_PAIRING",
        ["PLANETGROUPING"]="PLANET_GROUPING", ["PLANET_GROUPING"]="PLANET_GROUPING",
        ["NAMEDFULLMOON"]="NAMED_FULL_MOON", ["NAMED_FULL_MOON"]="NAMED_FULL_MOON", ["FULLMOON"]="NAMED_FULL_MOON",
        ["NEWMOON"]="NEW_MOON", ["NEW_MOON"]="NEW_MOON", ["LUNARECLIPSE"]="LUNAR_ECLIPSE", ["LUNAR_ECLIPSE"]="LUNAR_ECLIPSE",
        ["SOLARECLIPSE"]="SOLAR_ECLIPSE", ["SOLAR_ECLIPSE"]="SOLAR_ECLIPSE", ["COMET"]="COMET",
        ["DEEPSKYOBJECT"]="DEEP_SKY_OBJECT", ["DEEP_SKY_OBJECT"]="DEEP_SKY_OBJECT", ["DSO"]="DEEP_SKY_OBJECT"
    };
    public EventFamilyResolution Resolve(EventFamilyResolutionRequest request)
    {
        var key = Normalize(request.RequestedEventType);
        if (!Aliases.TryGetValue(key, out var family) && !string.IsNullOrWhiteSpace(request.Category)) Aliases.TryGetValue(Normalize(request.Category), out family);
        var known = family is not null;
        family ??= "UNKNOWN";
        return new(request.RequestedEventType, key, family, known ? [key] : [], [$"eventType:{request.RequestedEventType}", known ? $"alias:{key}->{family}" : "no-registered-alias"], known);
    }
    private static string Normalize(string? value) => new((value ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_').Select(char.ToUpperInvariant).ToArray());
}

public sealed class ProductionEventIntelligenceCapabilityResolver(IEnumerable<IProductionEventIntelligenceCapability> capabilities) : IProductionEventIntelligenceCapabilityResolver
{
    private readonly IReadOnlyList<IProductionEventIntelligenceCapability> _capabilities = capabilities.ToArray();
    public EventIntelligenceCapabilityResolution Resolve(EventFamilyResolution family)
    {
        var matches = _capabilities.Where(x => x.CanHandle(family) && !x.SupportedEventFamilies.Contains("UNKNOWN", StringComparer.OrdinalIgnoreCase)).OrderByDescending(x=>x.Priority).ThenBy(x=>x.CapabilityId, StringComparer.Ordinal).ToArray();
        if (matches.Length > 1 && matches[0].Priority == matches[1].Priority) throw new InvalidOperationException($"P2_CAPABILITY_PRIORITY_CONFLICT: {family.EventFamily}");
        if (matches.Length > 0) return new(family.RequestedEventType, family.EventFamily, matches[0].CapabilityId, matches[0].Version, false, null, false, family.ResolutionEvidence.Concat([$"capability:{matches[0].CapabilityId}"]).ToArray());
        if (family.IsKnownFamily) throw new InvalidOperationException($"P2_KNOWN_FAMILY_CAPABILITY_MISSING: {family.EventFamily}");
        var fallback = _capabilities.Where(x=>x.SupportedEventFamilies.Contains("UNKNOWN", StringComparer.OrdinalIgnoreCase)).OrderByDescending(x=>x.Priority).ThenBy(x=>x.CapabilityId, StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidOperationException("P2_UNKNOWN_FAMILY_FALLBACK_DISABLED");
        return new(family.RequestedEventType, family.EventFamily, fallback.CapabilityId, fallback.Version, true, "Unregistered astronomy event family; configured generic capability selected.", false, family.ResolutionEvidence.Concat([$"fallback:{fallback.CapabilityId}"]).ToArray());
    }
    public IProductionEventIntelligenceCapability GetCapability(EventIntelligenceCapabilityResolution resolution) => _capabilities.Single(x=>x.CapabilityId==resolution.CapabilityId && x.Version==resolution.CapabilityVersion);
}

public abstract class EventIntelligenceCapabilityBase(string id, params string[] families) : IProductionEventIntelligenceCapability
{
    public string CapabilityId { get; }=id; public string Version => "2.0"; public IReadOnlyCollection<string> SupportedEventFamilies { get; }=families; public virtual int Priority=>100;
    public bool CanHandle(EventFamilyResolution family)=>SupportedEventFamilies.Contains(family.EventFamily,StringComparer.OrdinalIgnoreCase);
    public abstract object BuildPayload(EventIntelligenceBuildContext c);
    public virtual Task<EventFamilyIntelligenceResult> BuildAsync(EventIntelligenceBuildContext c, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var i=c.BaseIntelligence; var source=Source(c);
        var claims=new[]{new CertifiedKnowledgeClaim($"{c.Family.EventFamily}:identity","Identity","ScientificIdentity",i.ScientificContext,null,null,[source.SourceId],null,0.8m,null,null,"Static","MachineCertified",c.Family.EventFamily)};
        return Task.FromResult(new EventFamilyIntelligenceResult(BuildPayload(c),claims,[source],i.ViewerInstructions,i.RequiredVisualObjects??[],i.RequiredNarrationFacts??[],i.ViewingSafetyRules??[],["existing-media-strategy-reused:"+c.MediaStrategy.EventType],i.QualityWarnings,[]));
    }
    protected virtual ProductionIntelligenceSource Source(EventIntelligenceBuildContext c)
    { var r=c.PipelineRequest.Request; return new("plan-source","ContentPlan",string.IsNullOrWhiteSpace(r.VerificationSource)?"ContentPlan":r.VerificationSource!,null,"Phase 1 verified plan",r.SourceExternalEventId,DateTimeOffset.UtcNow,r.StartUtc,r.EndUtc,"PlanAuthority",["identity","observation"],null,r.PeakUtc.HasValue?"Dynamic":"Static",r.SourceNotes); }
    public virtual EventFamilyValidationPolicy GetValidationPolicy(EventIntelligenceBuildContext c)=>new($"{CapabilityId}.policy","2.0",c.Family.EventFamily,Requirements(c),1m,0m);
    protected virtual IReadOnlyList<FieldRequirement> Requirements(EventIntelligenceBuildContext c)=>[Req("Intelligence.Title"),Req("Sources.Sources")];
    protected static FieldRequirement Req(string path, RequirementLevel level=RequirementLevel.Required, string code="P2_REQUIRED_FIELD_MISSING")=>new(path,level,null,"NotEmpty",code,$"{path} is required for this family.");
}
public sealed class ConstellationIntelligenceCapability():EventIntelligenceCapabilityBase("Constellation","CONSTELLATION") { public override object BuildPayload(EventIntelligenceBuildContext c)=>new ConstellationIntelligencePayload(c.BaseIntelligence.Title,c.BaseIntelligence.PrimaryObjects,c.BaseIntelligence.VisualMotifs,c.BaseIntelligence.BestViewingWindowLocal,c.BaseIntelligence.VisibilityRegion,c.BaseIntelligence.SecondaryObjects); protected override IReadOnlyList<FieldRequirement> Requirements(EventIntelligenceBuildContext c)=>[Req("Intelligence.Title"),Req("Result.FamilySpecificPayload.CanonicalIdentity"),Req("Intelligence.AngularSeparationDegrees",RequirementLevel.NotApplicable)]; }
public sealed class MeteorShowerIntelligenceCapability():EventIntelligenceCapabilityBase("MeteorShower","METEOR_SHOWER") { public override object BuildPayload(EventIntelligenceBuildContext c)=>new MeteorShowerIntelligencePayload(c.PipelineRequest.Request.RadiantVisibilityNote,c.BaseIntelligence.SecondaryObjects.FirstOrDefault(),c.PipelineRequest.Request.StartUtc,c.BaseIntelligence.PeakUtc,c.PipelineRequest.Request.EndUtc,c.BaseIntelligence.BestViewingWindowLocal,c.BaseIntelligence.MoonInterference); }
public sealed class PlanetaryAlignmentIntelligenceCapability():EventIntelligenceCapabilityBase("PlanetaryAlignment","PLANET_CONJUNCTION","PLANET_PAIRING") { public override object BuildPayload(EventIntelligenceBuildContext c)=>new PlanetaryAlignmentIntelligencePayload(c.BaseIntelligence.PrimaryObjects.Concat(c.BaseIntelligence.SecondaryObjects).ToArray(),c.BaseIntelligence.AngularSeparationDegrees,string.Join(", ",c.BaseIntelligence.RelativeObjectOrder??[]),c.BaseIntelligence.AltitudeDegrees,c.BaseIntelligence.SkyDirectionHint,c.BaseIntelligence.BestViewingWindowLocal); }
public sealed class PlanetGroupingIntelligenceCapability():EventIntelligenceCapabilityBase("PlanetGrouping","PLANET_GROUPING") { public override object BuildPayload(EventIntelligenceBuildContext c)=>new PlanetaryAlignmentIntelligencePayload(c.BaseIntelligence.PrimaryObjects.Concat(c.BaseIntelligence.SecondaryObjects).ToArray(),c.BaseIntelligence.AngularSeparationDegrees,string.Join(", ",c.BaseIntelligence.RelativeObjectOrder??[]),c.BaseIntelligence.AltitudeDegrees,c.BaseIntelligence.SkyDirectionHint,c.BaseIntelligence.BestViewingWindowLocal); }
public sealed class LunarEventIntelligenceCapability():EventIntelligenceCapabilityBase("LunarEvent","NAMED_FULL_MOON","NEW_MOON") { public override object BuildPayload(EventIntelligenceBuildContext c)=>new LunarEventIntelligencePayload(c.Family.EventFamily,c.BaseIntelligence.PeakUtc,c.BaseIntelligence.MoonIlluminationPercent,c.BaseIntelligence.BestViewingWindowLocal); }
public sealed class EclipseIntelligenceCapability():EventIntelligenceCapabilityBase("Eclipse","LUNAR_ECLIPSE","SOLAR_ECLIPSE") { public override object BuildPayload(EventIntelligenceBuildContext c)=>new EclipseIntelligencePayload(c.Family.EventFamily,c.BaseIntelligence.VisibilityRegion,c.BaseIntelligence.PeakUtc,c.BaseIntelligence.ViewingSafetyRules??[],c.PipelineRequest.Request.VerificationSource); protected override IReadOnlyList<FieldRequirement> Requirements(EventIntelligenceBuildContext c)=>c.Family.EventFamily=="SOLAR_ECLIPSE"?[Req("Intelligence.VisibilityRegion"),Req("Intelligence.ViewingSafetyRules",RequirementLevel.Required,"P2_SOLAR_ECLIPSE_SAFETY_REQUIRED"),Req("Sources.Sources")]:base.Requirements(c); }
public sealed class CometIntelligenceCapability():EventIntelligenceCapabilityBase("Comet","COMET") { public override object BuildPayload(EventIntelligenceBuildContext c)=>new GenericAstronomyIntelligencePayload(c.Family.EventFamily,c.BaseIntelligence.PrimaryObjects,c.BaseIntelligence.BestViewingWindowLocal); }
public sealed class DeepSkyObjectIntelligenceCapability():EventIntelligenceCapabilityBase("DeepSkyObject","DEEP_SKY_OBJECT") { public override object BuildPayload(EventIntelligenceBuildContext c)=>new GenericAstronomyIntelligencePayload(c.Family.EventFamily,c.BaseIntelligence.PrimaryObjects,c.BaseIntelligence.BestViewingWindowLocal); }
public sealed class GenericAstronomyIntelligenceCapability():EventIntelligenceCapabilityBase("GenericAstronomy","UNKNOWN") { public override int Priority=>0; public override object BuildPayload(EventIntelligenceBuildContext c)=>new GenericAstronomyIntelligencePayload(c.BaseIntelligence.EventType,c.BaseIntelligence.PrimaryObjects,c.BaseIntelligence.BestViewingWindowLocal); }

public sealed class ProductionEventIntelligenceValidator : IProductionEventIntelligenceValidator
{
    public Phase2SemanticValidationResult Validate(Phase2ValidationRequest r)
    {
        var errors=new List<string>(); var warnings=new List<string>(); var req=0;var reqOk=0;var rec=0;var recOk=0;var na=0;
        using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new{r.Intelligence,r.Result,r.Sources}));
        foreach(var f in r.Policy.Requirements) { if(f.RequirementLevel==RequirementLevel.NotApplicable){na++;continue;} var ok=Has(doc.RootElement,f.FieldPath); if(f.RequirementLevel is RequirementLevel.Required or RequirementLevel.ConditionallyRequired){req++;if(ok)reqOk++;else errors.Add($"{f.FailureCode}: {f.Description}");} else if(f.RequirementLevel==RequirementLevel.Recommended){rec++;if(ok)recOk++;else warnings.Add(f.Description);} }
        if(r.Family.IsKnownFamily&&r.Capability.FallbackUsed)errors.Add("P2_KNOWN_FAMILY_FALLBACK_PROHIBITED");
        var required=req==0?1m:reqOk/(decimal)req;var recommended=rec==0?1m:recOk/(decimal)rec;
        return new(errors.Count==0&&required>=r.Policy.MinimumRequiredCoverage,required,recommended,na,warnings,errors);
    }
    private static bool Has(JsonElement root,string path) { foreach(var part in path.Split('.')) { if(root.ValueKind!=JsonValueKind.Object)return false; var property=root.EnumerateObject().FirstOrDefault(x=>x.Name.Equals(part,StringComparison.OrdinalIgnoreCase));if(property.Name is null)return false;root=property.Value; } return root.ValueKind switch {JsonValueKind.Null=>false,JsonValueKind.String=>!string.IsNullOrWhiteSpace(root.GetString()),JsonValueKind.Array=>root.GetArrayLength()>0,_=>true}; }
}
public sealed class ProductionEventIntelligenceCertifier:IProductionEventIntelligenceCertifier { public Phase2CertificationResult Certify(Phase2CertificationRequest r)=>new(r.Validation.Passed,r.Validation.Passed?"Certified":"Rejected",r.Validation.Errors); }

public sealed class ProductionEventIntelligencePhaseService(IEventProductionIntelligenceAdapter adapter, IMediaEventStrategyResolver strategies, IProductionEventFamilyResolver families, IProductionEventIntelligenceCapabilityResolver capabilities, IProductionEventIntelligenceValidator validator, IProductionEventIntelligenceCertifier certifier):IProductionEventIntelligencePhaseService
{
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private static readonly string[] CanonicalNames=["production-event-intelligence.json","certified-knowledge-context.json","observation-context.json","source-registry.json","production-intelligence-diagnostics.json"];
    public async Task<Phase2ExecutionOutcome> ExecuteAsync(Phase2ExecutionRequest request,CancellationToken token)
    {
        var root=Path.GetFullPath(request.OutputRoot);var canonical=Path.Combine(root,"02-intelligence");
        if(!request.OverwriteExisting&&TryReadValid(canonical,out var existing)) return new("P2_REUSED",true,false,false,existing!,CanonicalNames.Select(x=>Path.Combine(canonical,x)).Append(Path.Combine(root,"plan-input","production-event-intelligence.json")).ToArray(),[]);
        Recover(root);
        var executionId=Guid.NewGuid().ToString("D");var transactionId=Guid.NewGuid().ToString("D");var authorityId=Guid.NewGuid().ToString("D");
        var family=families.Resolve(new(request.PipelineRequest.Request.EventType,request.PipelineRequest.Request.Title,request.PipelineRequest.Request.Category));var resolution=capabilities.Resolve(family);
        var intelligence=adapter.Normalize(request.PipelineRequest);var strategy=strategies.Resolve(intelligence.EventType,intelligence.Title);var capability=capabilities.GetCapability(resolution);var buildContext=new EventIntelligenceBuildContext(request.PipelineRequest,intelligence,family,strategy,executionId,transactionId);var result=await capability.BuildAsync(buildContext,token);
        var observation=new ProductionObservationContext(new(request.PipelineRequest.Request.RegionId,null,request.PipelineRequest.Request.TimeZone,request.PipelineRequest.Request.RegionId.Equals("GLOBAL",StringComparison.OrdinalIgnoreCase)),new(request.PipelineRequest.Request.StartUtc,intelligence.PeakUtc,request.PipelineRequest.Request.EndUtc,intelligence.BestViewingWindowLocal),new(intelligence.SkyDirectionHint,intelligence.AltitudeDegrees,intelligence.ObservationInfo?.VisibilityStatus??"Unspecified",intelligence.MoonInterference,intelligence.ViewingSafetyRules??[],0.8m,intelligence.QualityWarnings),result.SourceReferences.Select(x=>new ObservationCalculationReference(x.SourceId,x.ProviderId,x.ProviderVersion)).ToArray(),result.FamilySpecificPayload);
        var sources=new ProductionIntelligenceSourceRegistry("2.0",result.SourceReferences);var knowledge=new CertifiedKnowledgeContext("2.0",request.PipelineRequest.Request.PlanId.ToString("D"),executionId,family.EventFamily,result.KnowledgeClaims,new("Pending",result.KnowledgeClaims.Count,0,[]));
        var validation=validator.Validate(new(family,resolution,capability.GetValidationPolicy(buildContext),intelligence,result,observation,sources));var certification=certifier.Certify(new(validation,knowledge));if(!certification.Passed)throw new InvalidOperationException(string.Join(" | ",certification.Errors));knowledge=knowledge with{Certification=new("Certified",knowledge.Claims.Count,0,validation.Warnings)};
        var phase1=ReadPhase1Lineage(root);var refs=new Phase2ArtifactReferences("02-intelligence/certified-knowledge-context.json","02-intelligence/observation-context.json","02-intelligence/source-registry.json","02-intelligence/production-intelligence-diagnostics.json","plan-input/production-event-intelligence.json");var lineage=new Phase2Lineage(request.PipelineRequest.Request.PlanId.ToString("D"),executionId,transactionId,phase1.checksum,phase1.transaction,Hash(JsonSerializer.Serialize(request.PipelineRequest.Request)));
        var summary=new Phase2ValidationSummary(validation.RequiredCoverage,validation.RecommendedCoverage,validation.NotApplicableCount,validation.Passed,certification.Passed,validation.Warnings,validation.Errors);var metadata=new Phase2AuthorityMetadata("2.0","O2.ORCH.ALIGN.2A",authorityId,lineage.PlanId,executionId,transactionId,DateTimeOffset.UtcNow,request.PipelineRequest.Request.Language,request.PipelineRequest.Request.RegionId,intelligence.StrategyId??strategy.EventType,"Valid",certification.Status,"");var authority=new ProductionEventIntelligenceAuthority(metadata,new(intelligence.EventType,family.NormalizedEventType,family.EventFamily,intelligence.Title,intelligence.PrimaryObjects),resolution,intelligence,result.FamilySpecificPayload,refs,summary,lineage);authority=authority with{Metadata=metadata with{AuthoritySemanticChecksum=SemanticHash(authority)}};
        var staging=Path.Combine(root,$".02-intelligence-staging-{transactionId}");var backup=Path.Combine(root,$".02-intelligence-backup-{transactionId}");Directory.CreateDirectory(staging);
        try { await Write(Path.Combine(staging,CanonicalNames[0]),authority,token);await Write(Path.Combine(staging,CanonicalNames[1]),knowledge,token);await Write(Path.Combine(staging,CanonicalNames[2]),observation,token);await Write(Path.Combine(staging,CanonicalNames[3]),sources,token);await Write(Path.Combine(staging,CanonicalNames[4]),new{schemaVersion="2.0",family=family.EventFamily,capability=resolution,validation,certification,warnings=result.Warnings,diagnostics=result.Diagnostics},token);ValidateStaged(staging,authority);if(Directory.Exists(canonical))Directory.Move(canonical,backup);Directory.Move(staging,canonical);Directory.CreateDirectory(Path.Combine(root,"plan-input"));await Write(Path.Combine(root,"plan-input","production-event-intelligence.json"),authority.Intelligence,token);if(Directory.Exists(backup))Directory.Delete(backup,true); }
        catch { if(Directory.Exists(staging))Directory.Delete(staging,true);if(!Directory.Exists(canonical)&&Directory.Exists(backup))Directory.Move(backup,canonical);throw; }
        return new("P2_COMMITTED",false,false,Directory.Exists(backup),authority,CanonicalNames.Select(x=>Path.Combine(canonical,x)).Append(Path.Combine(root,"plan-input","production-event-intelligence.json")).ToArray(),validation.Warnings);
    }
    private static async Task Write(string path,object value,CancellationToken t){await File.WriteAllTextAsync(path,JsonSerializer.Serialize(value,JsonOptions),t);}
    private static void ValidateStaged(string root,ProductionEventIntelligenceAuthority expected){foreach(var n in CanonicalNames){var p=Path.Combine(root,n);if(!File.Exists(p))throw new InvalidOperationException("P2_STAGED_SET_INCOMPLETE: "+n);using var _=JsonDocument.Parse(File.ReadAllText(p));}var actual=JsonSerializer.Deserialize<ProductionEventIntelligenceAuthority>(File.ReadAllText(Path.Combine(root,CanonicalNames[0])),JsonOptions)??throw new InvalidOperationException("P2_AUTHORITY_INVALID");if(actual.Metadata.AuthoritySemanticChecksum!=SemanticHash(actual)||actual.Metadata.AuthorityId!=expected.Metadata.AuthorityId)throw new InvalidOperationException("P2_SEMANTIC_CHECKSUM_MISMATCH");}
    private static bool TryReadValid(string root,out ProductionEventIntelligenceAuthority? a){a=null;try{if(CanonicalNames.Any(x=>!File.Exists(Path.Combine(root,x))))return false;a=JsonSerializer.Deserialize<ProductionEventIntelligenceAuthority>(File.ReadAllText(Path.Combine(root,CanonicalNames[0])),JsonOptions);return a is not null&&a.ValidationSummary.CertificationPassed&&a.Metadata.AuthoritySemanticChecksum==SemanticHash(a);}catch{return false;}}
    private static void Recover(string root){foreach(var d in Directory.EnumerateDirectories(root,".02-intelligence-staging-*")){if(Directory.GetLastWriteTimeUtc(d)<DateTime.UtcNow.AddMinutes(-1))Directory.Delete(d,true);}var canonical=Path.Combine(root,"02-intelligence");if(Directory.Exists(canonical))return;var backup=Directory.EnumerateDirectories(root,".02-intelligence-backup-*").OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();if(backup is not null&&TryReadValid(backup,out _))Directory.Move(backup,canonical);}
    private static (string checksum,string? transaction) ReadPhase1Lineage(string root){var p=Path.Combine(root,"01-plan","execution-context.json");if(!File.Exists(p))return("unavailable",null);using var d=JsonDocument.Parse(File.ReadAllText(p));var x=d.RootElement;string? transaction=x.TryGetProperty("transactionId",out var t)?t.GetString():null;return(Hash(File.ReadAllText(p)),transaction);}
    private static string SemanticHash(ProductionEventIntelligenceAuthority a)=>Hash(JsonSerializer.Serialize(a with{Metadata=a.Metadata with{AuthoritySemanticChecksum=""}},JsonOptions));private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
