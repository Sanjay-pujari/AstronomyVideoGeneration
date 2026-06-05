using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Api;

public static class JsonEndpointBodyReader
{
    private const string InvalidJsonMessage = "Request body must be a single valid JSON object.";
    private const string EmptyJsonMessage = "Request body is required.";
    private const string InvalidJsonContentTypeMessage = "Request content type must be application/json.";

    public static async Task<JsonEndpointBodyReadResult<T>> ReadRequiredAsync<T>(
        HttpRequest request,
        string parameterName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await request.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            if (value is null)
            {
                return JsonEndpointBodyReadResult<T>.Invalid(Results.BadRequest(new
                {
                    message = EmptyJsonMessage,
                    parameter = parameterName
                }));
            }

            return JsonEndpointBodyReadResult<T>.Valid(value);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid JSON request body for {ParameterName}.", parameterName);
            return JsonEndpointBodyReadResult<T>.Invalid(Results.BadRequest(new
            {
                message = InvalidJsonMessage,
                parameter = parameterName,
                detail = ex.Message
            }));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Invalid JSON content type for {ParameterName}.", parameterName);
            return JsonEndpointBodyReadResult<T>.Invalid(Results.BadRequest(new
            {
                message = InvalidJsonContentTypeMessage,
                parameter = parameterName,
                detail = ex.Message
            }));
        }
    }
}

public sealed record JsonEndpointBodyReadResult<T>(T? Value, IResult? ErrorResult)
{
    public bool HasError => ErrorResult is not null;

    public static JsonEndpointBodyReadResult<T> Valid(T value) => new(value, null);

    public static JsonEndpointBodyReadResult<T> Invalid(IResult errorResult) => new(default, errorResult);
}
