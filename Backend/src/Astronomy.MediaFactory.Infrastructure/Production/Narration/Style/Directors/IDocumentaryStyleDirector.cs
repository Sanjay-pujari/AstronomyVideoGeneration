using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Directors;

/// <summary>Builds documentary style contracts from approved production contracts.</summary>
public interface IDocumentaryStyleDirector
{
    /// <summary>Converts editorial, storyboard, and narration brief decisions into documentary writing decisions.</summary>
    Task<DocumentaryStyleContract> BuildAsync(EditorialContract editorialContract, CreativeStoryboard creativeStoryboard, NarrationBriefsV5 narrationBriefs, CancellationToken cancellationToken);
}
