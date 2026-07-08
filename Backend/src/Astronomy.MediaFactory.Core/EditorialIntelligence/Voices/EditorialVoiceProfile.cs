namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Voices;
public sealed record EditorialVoiceProfile(string Name, string SentenceLength, IReadOnlyList<string> Tone, IReadOnlyList<string> Instructions);
