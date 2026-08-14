using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Publishing;

/// <summary>Resolves the Page -> linked Instagram publishing identity with one supported projection.</summary>
public sealed class MetaAccountIdentityResolver(HttpClient httpClient, ILogger logger)
{
    public const string PageFields = "id,name";
    public const string PageInstagramFields = "instagram_business_account";
    public const string InstagramFields = "id,username";
    private const string GraphEndpoint = MetaOAuthService.GraphEndpoint;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MetaAccountIdentity> ResolveAsync(string pageId, string pageAccessToken,
        string longLivedUserAccessToken, CancellationToken cancellationToken)
    {
        var page = await GetAsync<PageResponse>($"/{Uri.EscapeDataString(pageId)}", PageFields,
            longLivedUserAccessToken, "Meta Facebook page identity discovery", cancellationToken);
        if (string.IsNullOrWhiteSpace(page.Id))
            throw new InvalidOperationException("Meta Facebook page identity discovery did not return a page id.");

        var linked = await GetAsync<PageInstagramResponse>($"/{Uri.EscapeDataString(page.Id)}", PageInstagramFields,
            pageAccessToken, "Meta linked Instagram account discovery", cancellationToken);
        var instagramId = linked.InstagramBusinessAccount?.Id;
        if (string.IsNullOrWhiteSpace(instagramId))
            return new MetaAccountIdentity(page.Id, page.Name, null, null);

        var instagram = await GetAsync<InstagramResponse>($"/{Uri.EscapeDataString(instagramId)}", InstagramFields,
            longLivedUserAccessToken, "Meta Instagram identity discovery", cancellationToken);
        if (string.IsNullOrWhiteSpace(instagram.Id))
            throw new InvalidOperationException("Meta Instagram identity discovery did not return an account id.");
        if (!string.Equals(instagram.Id, instagramId, StringComparison.Ordinal))
            throw new InvalidOperationException("Meta Instagram identity response did not match the Page-linked account id.");

        return new MetaAccountIdentity(page.Id, page.Name, instagram.Id, instagram.Username);
    }

    private async Task<T> GetAsync<T>(string path, string fields, string accessToken, string operation,
        CancellationToken cancellationToken)
    {
        var url = BuildUri(GraphEndpoint + path, new Dictionary<string, string>
        {
            ["fields"] = fields,
            ["access_token"] = accessToken
        });
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await MetaGraphError.ReadAsync(response, cancellationToken);
            logger.LogWarning(
                "{Operation} failed: HTTP {Status}; type={ErrorType}; code={ErrorCode}; subcode={ErrorSubcode}; message={ErrorMessage}; fbtrace_id={TraceId}.",
                operation, error.HttpStatus, error.Type, error.Code, error.ErrorSubcode, error.Message, error.FbTraceId);
            throw new InvalidOperationException(error.ToSafeMessage(operation));
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"{operation} returned an empty response.");
    }

    private static string BuildUri(string endpoint, Dictionary<string, string> query)
    {
        var builder = new UriBuilder(endpoint)
        {
            Query = string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"))
        };
        return builder.Uri.ToString();
    }

    private sealed class PageResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
    }

    private sealed class PageInstagramResponse
    {
        [JsonPropertyName("instagram_business_account")] public InstagramReference? InstagramBusinessAccount { get; init; }
    }

    private sealed class InstagramReference { [JsonPropertyName("id")] public string? Id { get; init; } }

    private sealed class InstagramResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("username")] public string? Username { get; init; }
    }
}

public sealed record MetaAccountIdentity(string FacebookPageId, string? FacebookPageName,
    string? InstagramAccountId, string? InstagramUsername);
