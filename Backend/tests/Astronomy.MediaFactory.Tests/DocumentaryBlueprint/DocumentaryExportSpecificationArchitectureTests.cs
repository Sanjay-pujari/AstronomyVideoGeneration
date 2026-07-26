using System.Reflection;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryExportSpecificationArchitectureTests
{
    private static readonly Type[] Inventory=[typeof(DocumentaryExportSpecificationStatus),typeof(DocumentaryExportSpecificationRejectionReason),typeof(DocumentaryExportProfile),typeof(DocumentaryExportItemType),typeof(DocumentaryExportItemRequirement),typeof(DocumentaryExportContentType),typeof(DocumentaryExportEncoding),typeof(DocumentaryExportSpecificationPolicy),typeof(DocumentaryExportSpecificationMetadata),typeof(DocumentaryExportSpecificationRequest),typeof(DocumentaryExportItemDependency),typeof(DocumentaryExportSpecificationItem),typeof(DocumentaryExportSpecificationManifest),typeof(DocumentaryExportSpecification),typeof(DocumentaryExportSpecificationBuildResult),typeof(DocumentaryExportSpecificationSummary),typeof(DocumentaryExportSpecificationBuilder),typeof(DocumentaryExportSpecificationSummarizer)];
    private static readonly string[] Forbidden=["File","FilePath","Directory","Folder","FolderPath","Stream","Archive","Zip","Compression","Upload","Download","Storage","Blob","Bucket","Container","Http","Url","Uri","Endpoint","Repository","Database","Queue","Scheduler","Cron","Publish","Publisher","Hash","Checksum","Signature","Certificate","Encryption","Prompt","Provider","ModelName","Audio","Video","Image","Subtitle","Srt","Vtt","Render","GraphDatabase","AuditService"];

    [Theory][InlineData(typeof(DocumentaryExportSpecificationBuilder),"Build",typeof(DocumentaryExportSpecificationRequest),typeof(DocumentaryExportSpecificationBuildResult))][InlineData(typeof(DocumentaryExportSpecificationSummarizer),"Summarize",typeof(DocumentaryExportSpecification),typeof(DocumentaryExportSpecificationSummary))]
    public void Operation_boundary_is_exact(Type type,string operation,Type parameter,Type result)
    {Assert.True(type.IsPublic);Assert.True(type.IsSealed);Assert.Single(type.GetConstructors(),x=>x.GetParameters().Length==0);Assert.Empty(type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic));var method=Assert.Single(type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly));Assert.Equal(operation,method.Name);Assert.False(method.IsGenericMethod);Assert.Equal(parameter,Assert.Single(method.GetParameters()).ParameterType);Assert.Equal(result,method.ReturnType);Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType));}

    [Fact] public void Fixed_public_inventory_has_no_physical_export_capability()
    {
        foreach(var type in Inventory){var surfaces=new List<string>{type.Name};foreach(var constructor in type.GetConstructors())foreach(var p in constructor.GetParameters()){surfaces.Add(p.Name??"");Collect(p.ParameterType,surfaces);}foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)){surfaces.Add(method.Name);Collect(method.ReturnType,surfaces);foreach(var p in method.GetParameters())Collect(p.ParameterType,surfaces);}foreach(var property in type.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)){surfaces.Add(property.Name);Collect(property.PropertyType,surfaces);}Assert.DoesNotContain(surfaces.SelectMany(x=>Forbidden.Where(f=>ContainsIdentifierTerm(x,f))),_=>true);}
    }
    private static void Collect(Type type,List<string> values){values.Add(type.Name);if(type.IsGenericType)foreach(var argument in type.GetGenericArguments())Collect(argument,values);}
    private static bool ContainsIdentifierTerm(string identifier,string forbidden)
    {
        var words=Regex.Matches(identifier,@"[A-Z]+(?=[A-Z][a-z]|\d|$)|[A-Z]?[a-z]+|\d+").Select(x=>x.Value).ToArray();
        var terms=Regex.Matches(forbidden,@"[A-Z]+(?=[A-Z][a-z]|\d|$)|[A-Z]?[a-z]+|\d+").Select(x=>x.Value).ToArray();
        return Enumerable.Range(0,Math.Max(0,words.Length-terms.Length+1)).Any(index=>words.Skip(index).Take(terms.Length).SequenceEqual(terms,StringComparer.OrdinalIgnoreCase));
    }

    [Fact] public void Foundation_documentation_contains_all_certified_boundaries()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"../../../../../../docs/documentary-export-specification-foundation.md");var text=File.ReadAllText(Path.GetFullPath(path));string[] statements=["O2.15 does not create physical export files.","O2.15 does not create directories or archives.","O2.15 does not write structured JSON to disk.","O2.15 does not compress export content.","O2.15 does not upload or publish export content.","O2.15 does not use cloud storage.","O2.15 does not calculate hashes or checksums.","O2.15 does not create certificates or digital signatures.","O2.15 does not encrypt export content.","O2.15 does not invoke an external exporter.","O2.15 does not modify the certification record, provenance record, or production package.","O2.15 does not schedule export workflows.","10 canonical items","23 canonical dependencies","CertifiedKnowledgePackage","StructuredJson","deterministic specification identity","deterministic manifest identity","deterministic item identity","deterministic dependency identity","logical versus physical export distinction"];Assert.All(statements,x=>Assert.Contains(x,text,StringComparison.Ordinal));
    }
}
