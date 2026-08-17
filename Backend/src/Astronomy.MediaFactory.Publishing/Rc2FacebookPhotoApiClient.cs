using System.Net.Http.Headers;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

/// <summary>Graph client for synchronous Facebook Page photo creation and authoritative lookup.</summary>
public sealed class Rc2FacebookPhotoApiClient(HttpClient httpClient, IOptions<MetaOptions> options) : IRc2FacebookPhotoApiClient
{
    private const string Graph = "https://graph.facebook.com/v23.0";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Rc2FacebookPhotoCreateResult> CreatePagePhotoAsync(string imagePath, string message, CancellationToken ct)
    {
        var token = await LoadPageTokenAsync(ct);
        using var form = new MultipartFormDataContent();
        await using var file = File.OpenRead(imagePath);
        using var source = new StreamContent(file);
        source.Headers.ContentType = new MediaTypeHeaderValue(ContentType(imagePath));
        form.Add(source, "source", Path.GetFileName(imagePath));
        form.Add(new StringContent(message), "message");
        form.Add(new StringContent(token.FacebookPageAccessToken), "access_token");
        try
        {
            using var response = await httpClient.PostAsync($"{Graph}/{Uri.EscapeDataString(token.FacebookPageId)}/photos", form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(MetaDiagnostic(response.StatusCode, body));
            using var json = JsonDocument.Parse(body);
            var id = json.RootElement.TryGetProperty("id", out var value) ? value.GetString() : null;
            var postId = json.RootElement.TryGetProperty("post_id", out var post) ? post.GetString() : null;
            return new(id ?? throw new FacebookPhotoCreateOutcomeUnknownException(
                "Facebook Page photo create returned no authoritative photo ID."), postId);
        }
        catch (HttpRequestException ex)
        { throw new FacebookPhotoCreateOutcomeUnknownException("Facebook Page photo response was not received; remote outcome is unknown.", ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        { throw new FacebookPhotoCreateOutcomeUnknownException("Facebook Page photo request timed out; remote outcome is unknown.", ex); }
    }

    public async Task<Rc2FacebookPhoto?> GetPhotoAsync(string photoId, CancellationToken ct)
    {
        var token = await LoadPageTokenAsync(ct);
        var fields = "id,from,permalink_url,images";
        using var response = await httpClient.GetAsync($"{Graph}/{Uri.EscapeDataString(photoId)}?fields={fields}&access_token={Uri.EscapeDataString(token.FacebookPageAccessToken)}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(MetaDiagnostic(response.StatusCode, body));
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var from = root.TryGetProperty("from", out var owner) ? owner : default;
        return new(root.GetProperty("id").GetString()!,
            from.ValueKind == JsonValueKind.Object && from.TryGetProperty("id", out var pageId) ? pageId.GetString() : null,
            from.ValueKind == JsonValueKind.Object && from.TryGetProperty("name", out var pageName) ? pageName.GetString() : null,
            root.TryGetProperty("permalink_url", out var url) ? url.GetString() : null,
            root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0);
    }

    private async Task<MetaOAuthTokenFile> LoadPageTokenAsync(CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(options.Value.TokenFilePath)
            ? Path.Combine(AppContext.BaseDirectory, "meta-oauth-token.json") : Path.GetFullPath(options.Value.TokenFilePath);
        var token = JsonSerializer.Deserialize<MetaOAuthTokenFile>(await File.ReadAllTextAsync(path, ct), JsonOptions);
        var expectedId = options.Value.ExpectedFacebookPageId.Trim();
        var expectedName = options.Value.ExpectedFacebookPageName.Trim();
        if (token is null || string.IsNullOrWhiteSpace(token.FacebookPageAccessToken) ||
            string.IsNullOrWhiteSpace(token.FacebookPageId) || token.FacebookPageId != expectedId ||
            (!string.IsNullOrWhiteSpace(expectedName) && !string.Equals(token.FacebookPageName, expectedName, StringComparison.Ordinal)))
            throw new InvalidOperationException("Meta OAuth token file does not contain the governed Facebook Page credential.");
        return token;
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream" };

    private static string MetaDiagnostic(System.Net.HttpStatusCode status, string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body); var error = json.RootElement.GetProperty("error");
            string? Read(string name) => error.TryGetProperty(name, out var p) ? p.ToString() : null;
            return $"Meta Graph failed: http={(int)status}; type={Read("type")}; code={Read("code")}; subcode={Read("error_subcode")}; message={Read("message")}; fbtrace_id={Read("fbtrace_id")}";
        }
        catch (JsonException) { return $"Meta Graph failed with HTTP {(int)status}."; }
    }
}
