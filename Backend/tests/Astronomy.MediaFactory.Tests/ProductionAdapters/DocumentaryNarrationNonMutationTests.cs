using System.Text.Json;
using Astronomy.MediaFactory.ProductionAdapters;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryNarrationNonMutationTests
{
    [Fact] public void Resolver_does_not_mutate_request_plan_block_or_options(){var request=DocumentaryNarrationTestFixtures.Request();var options=DocumentaryNarrationTestFixtures.SpeechOptions();var requestBefore=JsonSerializer.Serialize(request);var optionsBefore=JsonSerializer.Serialize(options);new DocumentaryNarrationVoiceResolver(Options.Create(options)).Resolve(request);Assert.Equal(requestBefore,JsonSerializer.Serialize(request));Assert.Equal(optionsBefore,JsonSerializer.Serialize(options));}
}
