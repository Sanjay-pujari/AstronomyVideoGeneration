using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

/// <summary>Immutable typed knowledge validation issue.</summary>
public sealed record AstronomyKnowledgeValidationIssue
{
    private const int MaxCodeLength = 160;
    private const int MaxMessageLength = 1000;
    private const int MaxPathLength = 500;
    private static readonly Regex IdentifierPattern = new(@"^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public AstronomyKnowledgeValidationIssue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family)
    {
        Code = ValidateIdentifier(code, nameof(code), MaxCodeLength);
        Severity = Enum.IsDefined(severity) ? severity : throw new ArgumentOutOfRangeException(nameof(severity), severity, "Validation severity must be defined.");
        Message = ValidateText(message, nameof(message), MaxMessageLength, "Validation message");
        Path = ValidatePath(path);
        RuleId = ValidateIdentifier(ruleId, nameof(ruleId), MaxCodeLength);
        Domain = Enum.IsDefined(domain) ? domain : throw new ArgumentOutOfRangeException(nameof(domain), domain, "Knowledge domain must be defined.");
        Family = Enum.IsDefined(family) ? family : throw new ArgumentOutOfRangeException(nameof(family), family, "Knowledge payload family must be defined.");
    }
    public string Code { get; }
    public AstronomyKnowledgeValidationSeverity Severity { get; }
    public string Message { get; }
    public string Path { get; }
    public string RuleId { get; }
    public AstronomyKnowledgeDomain Domain { get; }
    public AstronomyKnowledgePayloadFamily Family { get; }
    internal static string ValidateIdentifier(string value, string parameterName, int maxLength = MaxCodeLength)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > maxLength || trimmed != trimmed.ToLowerInvariant() || !IdentifierPattern.IsMatch(trimmed))
            throw new ArgumentException("Identifier must be lowercase and match the canonical validation identifier format.", parameterName);
        return trimmed;
    }
    private static string ValidateText(string value, string parameterName, int maxLength, string label)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName); var trimmed=value.Trim();
        if (trimmed.Length==0 || trimmed.Length>maxLength) throw new ArgumentException($"{label} is required and must not exceed {maxLength} characters.", parameterName);
        return trimmed;
    }
    private static string ValidatePath(string path)
    {
        var trimmed = ValidateText(path, nameof(path), MaxPathLength, "Validation path");
        if (!trimmed.StartsWith('$')) throw new ArgumentException("Validation path must start with '$'.", nameof(path));
        return trimmed;
    }
}
