using System.Reflection;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionPipelineExecutionServiceTests
{
    [Fact]
    public void FirstNonEmpty_ReturnsEmptyString_WhenAllCandidatesAreMissing()
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("FirstNonEmpty", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, new object?[] { new string?[] { null, "", "   " } });

        Assert.Equal(string.Empty, result);
    }
}
