using Astronomy.MediaFactory.ProductionAdapters;
using Xunit;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentaryVariantCompositionArchitectureTests
{
 [Fact] public void Adapter_is_disabled_by_default()=>Assert.False(new DocumentaryVariantCompositionAdapterOptions().Enabled);
}
