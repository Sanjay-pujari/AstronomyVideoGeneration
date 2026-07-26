using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal static class DocumentaryCertificationValidator
{
    private static readonly Type[] PublicContracts = [
        typeof(DocumentaryProductionPackagePolicy),typeof(DocumentaryProductionPackageMetadata),typeof(DocumentaryProductionPackageRequest),
        typeof(DocumentaryProductionPackageManifestEntry),typeof(DocumentaryProductionPackageManifest),typeof(DocumentaryProductionPackage),
        typeof(DocumentaryProductionPackageAssemblyResult),typeof(DocumentaryProductionPackageSummary),
        typeof(DocumentaryProvenancePolicy),typeof(DocumentaryProvenanceMetadata),typeof(DocumentaryProvenanceRequest),
        typeof(DocumentaryProvenanceArtifactNode),typeof(DocumentaryProvenanceRelationshipEdge),typeof(DocumentaryProvenanceRecord),
        typeof(DocumentaryProvenanceBuildResult),typeof(DocumentaryProvenanceSummary),
        typeof(DocumentaryCertificationPolicy),typeof(DocumentaryCertificationMetadata),typeof(DocumentaryUpstreamCertificationEvidence),
        typeof(DocumentaryCertificationDocumentationEvidence),typeof(DocumentaryCertificationRequest),typeof(DocumentaryCertificationFinding),
        typeof(DocumentaryCertificationRuleResult),typeof(DocumentaryCertificationDecision),typeof(DocumentaryCertificationRecord),
        typeof(DocumentaryCertificationEvaluationResult),typeof(DocumentaryCertificationSummary)];

    private static readonly (Type Type,string Name,Type Parameter,Type Return)[] Operations = [
        (typeof(DocumentaryProductionPackageAssembler),"Assemble",typeof(DocumentaryProductionPackageRequest),typeof(DocumentaryProductionPackageAssemblyResult)),
        (typeof(DocumentaryProductionPackageSummarizer),"Summarize",typeof(DocumentaryProductionPackage),typeof(DocumentaryProductionPackageSummary)),
        (typeof(DocumentaryProvenanceBuilder),"Build",typeof(DocumentaryProvenanceRequest),typeof(DocumentaryProvenanceBuildResult)),
        (typeof(DocumentaryProvenanceSummarizer),"Summarize",typeof(DocumentaryProvenanceRecord),typeof(DocumentaryProvenanceSummary)),
        (typeof(DocumentaryCertificationEvaluator),"Evaluate",typeof(DocumentaryCertificationRequest),typeof(DocumentaryCertificationEvaluationResult)),
        (typeof(DocumentaryCertificationSummarizer),"Summarize",typeof(DocumentaryCertificationEvaluationResult),typeof(DocumentaryCertificationSummary))];

    // Exact architecture inventory: capabilities, not arbitrary substrings in the whole assembly.
    private static readonly string[] ForbiddenTypeNames = [
        "System.Net.Http.HttpClient","Microsoft.EntityFrameworkCore.DbContext","System.Linq.IQueryable",
        "System.IO.File","System.IO.FileStream","System.Security.Cryptography.HashAlgorithm",
        "System.Security.Cryptography.AsymmetricAlgorithm","System.Security.Cryptography.X509Certificates.X509Certificate",
        "Microsoft.Extensions.Hosting.IHostedService","Microsoft.Extensions.Hosting.BackgroundService"];

    internal static void ValidatePolicy(DocumentaryCertificationPolicy p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var controls=new[]{p.RequireCompleteProductionPackage,p.RequireCompleteProvenanceRecord,p.RequireDeterministicIdentity,
            p.RequireCanonicalManifest,p.RequireCanonicalArtifactInventory,p.RequireCanonicalRelationshipInventory,p.RequireCompleteLineage,
            p.RequireExactCorrelation,p.RequireDeterministicReconstruction,p.RequireImmutability,p.RequireOperationBoundaryCompliance,
            p.RequireForbiddenCapabilityCompliance,p.RequireDocumentationCompliance,p.RequireUpstreamCertification};
        if(controls.Any(x=>!x)||!p.RequiredDomains.SequenceEqual(DocumentaryCertificationInventory.Domains)||
           !p.RequiredRules.SequenceEqual(DocumentaryCertificationInventory.Rules)||p.PolicySchemaVersion!="1.0")
            throw new ArgumentException("Certification policy is invalid.");
    }

    internal static void ValidateEvidence(DocumentaryCertificationMetadata m,IReadOnlyList<DocumentaryUpstreamCertificationEvidence> upstream,IReadOnlyList<DocumentaryCertificationDocumentationEvidence> docs)
    {ArgumentNullException.ThrowIfNull(m);if(upstream.Count!=13||upstream.Select(x=>x.ObjectiveId).Distinct(StringComparer.Ordinal).Count()!=13||upstream.Select((x,i)=>x.Sequence==i&&x.ObjectiveId==DocumentaryCertificationInventory.Objectives[i]&&x.ObjectiveVersion=="1.0"&&x.IsCertified&&DocumentaryCertificationInventory.Eq(x.CorrelationId,m.CorrelationId)).Any(x=>!x))throw new ArgumentException("Upstream evidence is incomplete.");if(docs.Count!=4||docs.Select(x=>x.DocumentId).Distinct(StringComparer.Ordinal).Count()!=4||docs.Select((x,i)=>x.Sequence==i&&x.DocumentId==DocumentaryCertificationInventory.DocumentIds[i]&&x.DocumentVersion=="1.0"&&x.RequiredStatements.SequenceEqual(DocumentaryCertificationInventory.Statements[i])&&DocumentaryCertificationInventory.Eq(x.CorrelationId,m.CorrelationId)).Any(x=>!x))throw new ArgumentException("Documentation evidence is incomplete.");}
    internal static bool JsonRoundTrips<T>(T value){try{var o=new JsonSerializerOptions(JsonSerializerDefaults.Web);var json=JsonSerializer.Serialize(value,o);var copy=JsonSerializer.Deserialize<T>(json,o);return copy is not null&&json==JsonSerializer.Serialize(copy,o);}catch{return false;}}

    internal static bool OperationValid(Type type,string methodName,Type parameterType,Type returnType)
    {
        var constructors=type.GetConstructors(BindingFlags.Instance|BindingFlags.Public);
        var methods=type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly);
        if(!type.IsSealed||constructors.Length!=1||constructors[0].GetParameters().Length!=0||
           type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Length!=0||methods.Length!=1)return false;
        var method=methods[0];var parameters=method.GetParameters();var actualReturn=method.ReturnType;
        return method.Name==methodName&&!method.IsGenericMethod&&parameters.Length==1&&parameters[0].ParameterType==parameterType&&
            actualReturn==returnType&&!IsAsyncReturn(actualReturn);
    }
    private static bool IsAsyncReturn(Type type)=>type==typeof(Task)||type==typeof(ValueTask)||
        type.IsGenericType&&(type.GetGenericTypeDefinition()==typeof(Task<>)||type.GetGenericTypeDefinition()==typeof(ValueTask<>));
    internal static bool OperationsValid()=>Operations.All(x=>OperationValid(x.Type,x.Name,x.Parameter,x.Return));

    internal static bool ForbiddenCapabilitiesAbsent()=>ForbiddenCapabilitiesAbsent(PublicContracts.Concat(Operations.Select(x=>x.Type)));
    internal static bool ForbiddenCapabilitiesAbsent(IEnumerable<Type> inventory)
    {
        var inspected=inventory.SelectMany(PublicSurfaceTypes);
        return inspected.All(t=>!ForbiddenTypeNames.Contains((t.IsGenericType?t.GetGenericTypeDefinition():t).FullName,StringComparer.Ordinal));
    }
    private static IEnumerable<Type> PublicSurfaceTypes(Type type)
    {
        yield return type;
        foreach(var constructor in type.GetConstructors(BindingFlags.Public|BindingFlags.Instance))foreach(var parameter in constructor.GetParameters())foreach(var t in Expand(parameter.ParameterType))yield return t;
        foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)){foreach(var t in Expand(method.ReturnType))yield return t;foreach(var parameter in method.GetParameters())foreach(var t in Expand(parameter.ParameterType))yield return t;}
        foreach(var property in type.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static)){foreach(var t in Expand(property.PropertyType))yield return t;}
        foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static)){foreach(var t in Expand(field.FieldType))yield return t;}
    }
    private static IEnumerable<Type> Expand(Type type){yield return type;if(type.HasElementType&&type.GetElementType() is {} element)foreach(var t in Expand(element))yield return t;if(type.IsGenericType)foreach(var argument in type.GetGenericArguments())foreach(var t in Expand(argument))yield return t;}

    internal static bool Immutable()=>PublicContracts.All(type=>
        type.GetProperties(BindingFlags.Instance|BindingFlags.Public).All(property=>property.SetMethod is null&&
            (!IsCollection(property.PropertyType)||IsApprovedReadOnlyCollection(property.PropertyType)))&&
        type.GetFields(BindingFlags.Instance|BindingFlags.Public).All(field=>field.IsInitOnly));
    private static bool IsCollection(Type type)=>type!=typeof(string)&&typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    private static bool IsApprovedReadOnlyCollection(Type type)=>type.IsArray==false&&type.IsGenericType&&
        (type.GetGenericTypeDefinition()==typeof(IReadOnlyList<>)||type.GetGenericTypeDefinition()==typeof(IReadOnlyCollection<>)||type.GetGenericTypeDefinition()==typeof(ReadOnlyCollection<>));
}
