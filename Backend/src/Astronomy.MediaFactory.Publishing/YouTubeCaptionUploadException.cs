namespace Astronomy.MediaFactory.Publishing;

/// <summary>Safe, credential-free diagnostics for a failed resumable caption upload.</summary>
public sealed class YouTubeCaptionUploadException : InvalidOperationException
{
    public YouTubeCaptionUploadException(string message, string uploadStatus, Exception? uploadException = null,
        string? responseBody = null, string? httpErrorDetails = null)
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
