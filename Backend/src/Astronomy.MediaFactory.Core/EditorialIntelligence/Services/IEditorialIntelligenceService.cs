using System.Text.Json;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Contracts;
namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Services;
public interface IEditorialIntelligenceService { EditorialIntelligenceContract? CreateContract(string? eventId, string? eventName, string? eventType, JsonElement? eventMetadata); string BuildPromptGuidance(EditorialIntelligenceContract? contract); }
