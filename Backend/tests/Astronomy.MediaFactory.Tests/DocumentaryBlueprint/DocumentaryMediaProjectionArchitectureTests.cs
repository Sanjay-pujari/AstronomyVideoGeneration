using System.Reflection;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionArchitectureTests
{
 [Theory] [InlineData(typeof(DocumentaryMediaProjector),"Project",typeof(DocumentaryMediaProjectionRequest),typeof(DocumentaryMediaProjectionResult))] [InlineData(typeof(DocumentaryMediaProjectionSummarizer),"Summarize",typeof(DocumentaryMediaProject),typeof(DocumentaryMediaProjectionSummary))]
 public void Operations_are_public_sealed_synchronous_and_stateless(Type type,string operation,Type parameter,Type result)
 {Assert.True(type.IsPublic&&type.IsSealed);Assert.Empty(type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic));Assert.Single(type.GetConstructors());var method=Assert.Single(type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly));Assert.Equal(operation,method.Name);Assert.Equal(result,method.ReturnType);Assert.Equal(parameter,Assert.Single(method.GetParameters()).ParameterType);}
 [Fact] public void Production_projection_sources_contain_no_uninitialized_or_reflection_mutation_escape_hatches()
 {var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint"));var sources=Directory.GetFiles(root,"DocumentaryMediaProjection*.cs").Concat(Directory.GetFiles(root,"DocumentarySemanticMediaProjection.cs"));var forbidden=new[]{"RuntimeHelpers.GetUninitializedObject","FormatterServices","k__BackingField","BindingFlags","SetValue("};foreach(var source in sources){var text=File.ReadAllText(source);Assert.All(forbidden,x=>Assert.DoesNotContain(x,text,StringComparison.Ordinal));}}
}
