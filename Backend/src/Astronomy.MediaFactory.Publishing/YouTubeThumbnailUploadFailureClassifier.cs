using Google;
using System.Net;

namespace Astronomy.MediaFactory.Publishing;

internal static class YouTubeThumbnailUploadFailureClassifier
{
    public const string ThumbnailPermissionFailureCategory = "CustomThumbnailPermissionDenied";
    public const string ThumbnailPermissionRecommendedAction = "Verify the authenticated YouTube channel is eligible for custom thumbnails, or disable thumbnail uploads with Publishing:UploadThumbnail=false (or the YouTube per-asset thumbnail options) until eligibility is restored.";

    public static bool IsPermanentPermissionFailure(Exception exception)
    {
        var thumbnailException = exception as YouTubeThumbnailUploadException;
        var googleException = FindGoogleApiException(exception);
        if (googleException?.HttpStatusCode == HttpStatusCode.Forbidden && ContainsCustomThumbnailPermissionMessage(googleException.Message))
        {
            return true;
        }

        if (googleException?.Error is not null)
        {
            if (googleException.Error.Code == 403 && ContainsCustomThumbnailPermissionMessage(googleException.Error.Message))
            {
                return true;
            }

            if (googleException.Error.Errors?.Any(error =>
                    string.Equals(error.Domain, "youtube.thumbnail", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(error.Reason, "forbidden", StringComparison.OrdinalIgnoreCase)) == true)
            {
                return true;
            }
        }

        return thumbnailException is not null
            && (ContainsCustomThumbnailPermissionMessage(thumbnailException.Message)
                || ContainsCustomThumbnailPermissionMessage(thumbnailException.HttpErrorDetails)
                || ContainsCustomThumbnailPermissionMessage(thumbnailException.UploadException?.Message));
    }

    public static string BuildPermissionFailureMessage(string videoId)
        => $"YouTube custom thumbnail upload is not permitted for the authenticated channel while publishing video {videoId}. {ThumbnailPermissionRecommendedAction}";

    private static GoogleApiException? FindGoogleApiException(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is GoogleApiException googleApiException)
            {
                return googleApiException;
            }

            if (current is YouTubeThumbnailUploadException thumbnailException && thumbnailException.UploadException is not null)
            {
                var uploadException = FindGoogleApiException(thumbnailException.UploadException);
                if (uploadException is not null)
                {
                    return uploadException;
                }
            }
        }

        return null;
    }

    private static bool ContainsCustomThumbnailPermissionMessage(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains("custom video thumbnails", StringComparison.OrdinalIgnoreCase)
            && value.Contains("permission", StringComparison.OrdinalIgnoreCase);
}
