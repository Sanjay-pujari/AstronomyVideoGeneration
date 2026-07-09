namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public interface IPromptComposer<in TInput, TOutput>
{
    TOutput Compose(TInput input);
}
