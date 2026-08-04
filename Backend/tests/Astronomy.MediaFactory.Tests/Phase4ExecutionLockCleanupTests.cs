using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase4ExecutionLockCleanupTests
{
    [Fact]
    public async Task Release_RemovesExclusivelyOwnedLockAndEmptyDirectory()
    {
        var root=Path.Combine(Path.GetTempPath(),"phase4-lock-"+Guid.NewGuid().ToString("N"));
        try
        {
            var executionLock=new Phase4ExecutionLock();
            await using(var held=await executionLock.AcquireAsync(root,"execution",CancellationToken.None))
                Assert.Single(Directory.EnumerateFiles(Path.Combine(root,".locks"),"*.phase-04.lock"));

            Assert.False(Directory.Exists(Path.Combine(root,".locks")));
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
