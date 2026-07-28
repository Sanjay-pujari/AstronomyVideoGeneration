using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionAttemptContextFactoryTests
{
 [Fact]
 public void Attempt_context_preserves_execution_identity()
 {
  var now=new DateTimeOffset(2026,7,28,12,0,0,TimeSpan.Zero);
  var factory=new DocumentaryProductionAttemptContextFactory(new FixedClock(now));
  var execution=new DocumentaryProductionExecutionContext("execution-1","correlation-1",DocumentaryProductionExecutionMode.Certified,"/tmp/work",now.AddMinutes(-1),new Dictionary<string,string>());
  var attempt=factory.Create(execution,DocumentaryProductionOperationKind.SceneComposition,"asset-1","provider-1",2,TimeSpan.FromSeconds(30),"variant-1","scene-1");
  attempt.Should().Be(new DocumentaryProductionAttemptContext("execution-1","correlation-1",DocumentaryProductionOperationKind.SceneComposition,"asset-1","variant-1","scene-1",2,"provider-1",now,TimeSpan.FromSeconds(30)));
 }

 [Fact]
 public void Attempt_number_must_be_positive()
 {
  var factory=new DocumentaryProductionAttemptContextFactory(new FixedClock(DateTimeOffset.UnixEpoch));
  var execution=new DocumentaryProductionExecutionContext("execution","correlation",DocumentaryProductionExecutionMode.Certified,"/tmp",DateTimeOffset.UnixEpoch,new Dictionary<string,string>());
  Func<int,TimeSpan,Action> create=(attempt,timeout)=>()=>factory.Create(execution,DocumentaryProductionOperationKind.VisualGeneration,"asset","provider",attempt,timeout);
  create(0,TimeSpan.FromSeconds(1)).Should().Throw<ArgumentOutOfRangeException>();
  create(-1,TimeSpan.FromSeconds(1)).Should().Throw<ArgumentOutOfRangeException>();
  create(1,TimeSpan.Zero).Should().Throw<ArgumentOutOfRangeException>();
  create(1,TimeSpan.FromTicks(-1)).Should().Throw<ArgumentOutOfRangeException>();
 }

 private sealed class FixedClock(DateTimeOffset now):IDocumentaryProductionClock { public DateTimeOffset UtcNow=>now; }
}
