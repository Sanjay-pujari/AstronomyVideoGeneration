using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
namespace Astronomy.MediaFactory.AstroData.Services;
public sealed class TopicRankingService : ITopicRankingService
{
    public Task<IReadOnlyCollection<RankedTopic>> RankAsync(AstronomyContext context, ContentType contentType, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<RankedTopic>>(context.Events.OrderByDescending(x => x.Score).Select(x => new RankedTopic { ContentType = contentType, TopicTitle = x.ObjectName, Summary = x.Details, Score = x.Score }).ToList().AsReadOnly());
}
