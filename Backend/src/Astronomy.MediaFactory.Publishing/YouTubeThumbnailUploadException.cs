namespace Astronomy.MediaFactory.Publishing;

public sealed class YouTubeThumbnailUploadException : InvalidOperationException
{
    public YouTubeThumbnailUploadException(
        string message,
        string uploadStatus,
        Exception? uploadException = null,
        string? responseBody = null,
        string? httpErrorDetails = null)
        : base(message, uploadException)
    {
        UploadStatus = uploadStatus;
        UploadException = uploadException;
        ResponseBody = responseBody;
        HttpErrorDetails = httpErrorDetails;
    }

    public string UploadStatus { get; }
    public Exception? UploadException { get; }
    public string? ResponseBody { get; }
    public string? HttpErrorDetails { get; }
}
