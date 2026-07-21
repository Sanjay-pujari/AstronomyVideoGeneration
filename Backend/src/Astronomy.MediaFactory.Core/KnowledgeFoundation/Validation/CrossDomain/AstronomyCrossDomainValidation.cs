namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;

public sealed record AstronomyKnowledgeValidationIssue(string Code, string Message, string? Field = null);

public sealed record AstronomyCrossDomainValidationPayload(
    string? EntityName = null,
    DateTimeOffset? EventStartUtc = null,
    DateTimeOffset? EventEndUtc = null,
    DateTimeOffset? ObservationUtc = null,
    double? AltitudeDegrees = null,
    double? RightAscensionHours = null,
    double? DeclinationDegrees = null);

public interface IAstronomyCrossDomainValidationRule
{
    string Code { get; }
    IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyCrossDomainValidationPayload payload);
}

public sealed class AstronomyCrossDomainValidationRule : IAstronomyCrossDomainValidationRule
{
    private readonly Func<AstronomyCrossDomainValidationPayload, IEnumerable<AstronomyKnowledgeValidationIssue>> _validate;

    public AstronomyCrossDomainValidationRule(
        string code,
        Func<AstronomyCrossDomainValidationPayload, IEnumerable<AstronomyKnowledgeValidationIssue>> validate)
    {
        Code = string.IsNullOrWhiteSpace(code) ? throw new ArgumentException("Rule code is required.", nameof(code)) : code;
        _validate = validate ?? throw new ArgumentNullException(nameof(validate));
    }

    public string Code { get; }

    public IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyCrossDomainValidationPayload payload) => _validate(payload);
}

public sealed class AstronomyCrossDomainKnowledgeValidator
{
    private static readonly IReadOnlyDictionary<string, int> ProductionRuleOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["cross-domain.entity.consistency"] = 0,
        ["cross-domain.event-temporal.consistency"] = 1,
        ["cross-domain.observation-visibility.consistency"] = 2,
        ["cross-domain.orbital-positional.consistency"] = 3,
    };

    private readonly IReadOnlyList<IAstronomyCrossDomainValidationRule> _rules;

    public AstronomyCrossDomainKnowledgeValidator(IEnumerable<IAstronomyCrossDomainValidationRule>? rules = null)
    {
        _rules = (rules ?? CreateProductionRules()).ToArray();
    }

    public IReadOnlyList<AstronomyKnowledgeValidationIssue> Validate(AstronomyCrossDomainValidationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var issues = new List<AstronomyKnowledgeValidationIssue>();
        foreach (var rule in _rules.OrderBy(rule => ResolveRuleOrder(rule.Code)))
        {
            issues.AddRange(rule.Validate(payload));
        }

        return issues;
    }

    public Task<IReadOnlyList<AstronomyKnowledgeValidationIssue>> ValidateAsync(
        AstronomyCrossDomainValidationPayload payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Validate(payload));
    }

    public static IReadOnlyList<IAstronomyCrossDomainValidationRule> CreateProductionRules() =>
    [
        new AstronomyCrossDomainValidationRule("cross-domain.entity.consistency", ValidateEntityConsistency),
        new AstronomyCrossDomainValidationRule("cross-domain.event-temporal.consistency", ValidateEventTemporalConsistency),
        new AstronomyCrossDomainValidationRule("cross-domain.observation-visibility.consistency", ValidateObservationVisibilityConsistency),
        new AstronomyCrossDomainValidationRule("cross-domain.orbital-positional.consistency", ValidateOrbitalPositionalConsistency),
    ];

    private static int ResolveRuleOrder(string code)
        => ProductionRuleOrder.TryGetValue(code, out var order) ? order : int.MaxValue;

    private static IEnumerable<AstronomyKnowledgeValidationIssue> ValidateEntityConsistency(AstronomyCrossDomainValidationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.EntityName))
        {
            yield return new AstronomyKnowledgeValidationIssue("cross-domain.entity.consistency", "Entity name is required.", nameof(payload.EntityName));
        }
    }

    private static IEnumerable<AstronomyKnowledgeValidationIssue> ValidateEventTemporalConsistency(AstronomyCrossDomainValidationPayload payload)
    {
        if (payload.EventStartUtc is null)
        {
            yield return new AstronomyKnowledgeValidationIssue("cross-domain.event-temporal.consistency", "Event start time is required.", nameof(payload.EventStartUtc));
        }

        if (payload.EventStartUtc is not null && payload.EventEndUtc is not null && payload.EventEndUtc < payload.EventStartUtc)
        {
            yield return new AstronomyKnowledgeValidationIssue("cross-domain.event-temporal.consistency", "Event end time cannot be before event start time.", nameof(payload.EventEndUtc));
        }
    }

    private static IEnumerable<AstronomyKnowledgeValidationIssue> ValidateObservationVisibilityConsistency(AstronomyCrossDomainValidationPayload payload)
    {
        if (payload.AltitudeDegrees is < 0 or > 90)
        {
            yield return new AstronomyKnowledgeValidationIssue("cross-domain.observation-visibility.consistency", "Altitude must be between 0 and 90 degrees for visible observations.", nameof(payload.AltitudeDegrees));
        }
    }

    private static IEnumerable<AstronomyKnowledgeValidationIssue> ValidateOrbitalPositionalConsistency(AstronomyCrossDomainValidationPayload payload)
    {
        if (payload.RightAscensionHours is < 0 or >= 24 || payload.DeclinationDegrees is < -90 or > 90)
        {
            yield return new AstronomyKnowledgeValidationIssue("cross-domain.orbital-positional.consistency", "Equatorial coordinates are outside valid bounds.", nameof(payload.RightAscensionHours));
        }
    }
}
