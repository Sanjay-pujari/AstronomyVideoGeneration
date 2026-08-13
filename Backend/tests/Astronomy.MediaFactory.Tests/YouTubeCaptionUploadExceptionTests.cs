using Astronomy.MediaFactory.Publishing;

namespace Astronomy.MediaFactory.Tests;

public sealed class YouTubeCaptionUploadExceptionTests
{
    [Fact]
    public void Safe_caption_failure_preserves_provider_diagnostics_without_credentials()
    {
        var provider = new InvalidOperationException("403 insufficientPermissions: insufficient authentication scopes");
        var error = new YouTubeCaptionUploadException(
            "Status=Failed; ExceptionType=Google.GoogleApiException; ProviderError=403 insufficientPermissions",
            "Failed", provider, null,
            "StatusCode=403 (Forbidden); reason=insufficientPermissions; message=insufficient authentication scopes");

        Assert.Contains("insufficientPermissions", error.Message);
        Assert.Contains("StatusCode=403", error.HttpErrorDetails);
        Assert.Equal("Failed", error.UploadStatus);
        Assert.DoesNotContain("access_token", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
