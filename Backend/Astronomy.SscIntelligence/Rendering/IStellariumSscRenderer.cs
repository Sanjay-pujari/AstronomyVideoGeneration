using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Rendering;

public interface IStellariumSscRenderer
{
    SscRenderResult Render(SscRenderRequest request);
}
