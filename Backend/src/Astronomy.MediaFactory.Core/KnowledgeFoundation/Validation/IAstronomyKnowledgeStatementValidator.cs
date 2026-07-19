using Astronomy.MediaFactory.Core.AstronomyDomain.Validation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

public interface IAstronomyKnowledgeStatementValidator
{
    DomainValidationResult Validate(IAstronomyKnowledgeStatement statement);
}
