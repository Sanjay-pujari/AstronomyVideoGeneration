using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

/// <summary>Small, step-oriented Graph client. It deliberately does not combine container creation and publishing.</summary>
public sealed class Rc2InstagramApiClient(HttpClient httpClient, IOptions<MetaOptions> options) : IRc2InstagramApiClient
{
    private const string Graph = "https://graph.facebook.com/v23.0";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> CreateImageContainerAsync(string imageUrl, string caption, CancellationToken ct)
    {
        var token = await LoadTokenAsync(ct);
        return await PostForIdAsync($"{Graph}/{Uri.EscapeDataString(token.InstagramBusinessAccountId!)}/media",
            new() { ["image_url"] = imageUrl, ["caption"] = caption, ["access_token"] = AccessToken(token) }, ct, ambiguous: false);
    }

    public async Task<string> GetContainerStatusAsync(string containerId, CancellationToken ct)
    {
        var token = await LoadTokenAsync(ct);
        var value = await GetAsync($"{Graph}/{Uri.EscapeDataString(containerId)}?fields=status_code&access_token={Uri.EscapeDataString(AccessToken(token))}", ct);
        return value.GetProperty("status_code").GetString() ?? "UNKNOWN";
    }

    public async Task<string> PublishContainerAsync(string containerId, CancellationToken ct)
    {
        var token = await LoadTokenAsync(ct);
        try
        {
            return await PostForIdAsync($"{Graph}/{Uri.EscapeDataString(token.InstagramBusinessAccountId!)}/media_publish",
                new() { ["creation_id"] = containerId, ["access_token"] = AccessToken(token) }, ct, ambiguous: true);
        }
        catch (InstagramPublishOutcomeUnknownException) { throw; }
    }

    public async Task<Rc2InstagramMedia?> GetMediaAsync(string mediaId, CancellationToken ct)
    {
        var token = await LoadTokenAsync(ct);
        var value = await GetAsync($"{Graph}/{Uri.EscapeDataString(mediaId)}?fields=id,media_type,owner,permalink&access_token={Uri.EscapeDataString(AccessToken(token))}", ct);
        var owner = value.TryGetProperty("owner", out var ownerValue) && ownerValue.TryGetProperty("id", out var ownerId) ? ownerId.GetString() : null;
        return new(value.GetProperty("id").GetString()!, value.TryGetProperty("media_type", out var mt) ? mt.GetString() : null,
            owner, value.TryGetProperty("permalink", out var url) ? url.GetString() : null);
    }

    private async Task<string> PostForIdAsync(string url, Dictionary<string, string> form, CancellationToken ct, bool ambiguous)
    {
        try
        {
            using var response = await httpClient.PostAsync(url, new FormUrlEncodedContent(form), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(MetaDiagnostic(response.StatusCode, body));
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("Meta returned no id.");
        }
        catch (HttpRequestException ex) when (ambiguous)
        {
            throw new InstagramPublishOutcomeUnknownException("Instagram media_publish response was not received; remote outcome is unknown.", ex);
        }
        catch (TaskCanceledException ex) when (ambiguous && !ct.IsCancellationRequested)
        {
            throw new InstagramPublishOutcomeUnknownException("Instagram media_publish timed out; remote outcome is unknown.", ex);
        }
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(MetaDiagnostic(response.StatusCode, body));
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    private async Task<MetaOAuthTokenFile> LoadTokenAsync(CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(options.Value.TokenFilePath)
            ? Path.Combine(AppContext.BaseDirectory, "meta-oauth-token.json") : Path.GetFullPath(options.Value.TokenFilePath);
        var token = JsonSerializer.Deserialize<MetaOAuthTokenFile>(await File.ReadAllTextAsync(path, ct), JsonOptions);
        if (token is null || string.IsNullOrWhiteSpace(token.InstagramBusinessAccountId) ||
            (string.IsNullOrWhiteSpace(token.FacebookPageAccessToken) && string.IsNullOrWhiteSpace(token.LongLivedUserAccessToken)))
            throw new InvalidOperationException("Meta OAuth token file is missing Instagram publishing details.");
        return token;
    }

    private static string AccessToken(MetaOAuthTokenFile token) => string.IsNullOrWhiteSpace(token.FacebookPageAccessToken)
        ? token.LongLivedUserAccessToken : token.FacebookPageAccessToken;

    private static string MetaDiagnostic(System.Net.HttpStatusCode status, string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var error = json.RootElement.GetProperty("error");
            string? Read(string name) => error.TryGetProperty(name, out var p) ? p.ToString() : null;
            return $"Meta Graph failed: http={(int)status}; type={Read("type")}; code={Read("code")}; subcode={Read("error_subcode")}; message={Read("message")}; fbtrace_id={Read("fbtrace_id")}";
        }
        catch (JsonException) { return $"Meta Graph failed with HTTP {(int)status}."; }
    }
}
