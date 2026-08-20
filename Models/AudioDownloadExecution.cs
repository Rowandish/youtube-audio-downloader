namespace YoutubeAudioDownloader.Models;

public sealed record AudioDownloadRequest(
    string Url,
    string OutputDirectory,
    AudioQuality Quality);

public abstract record AudioDownloadUpdate;

public sealed record DownloadProgressUpdate(double Value) : AudioDownloadUpdate;

public sealed record DownloadStatusUpdate(string Message) : AudioDownloadUpdate;

public sealed record AudioDownloadResult(bool Succeeded, string? ErrorMessage)
{
    public static AudioDownloadResult Success() => new(true, null);

    public static AudioDownloadResult Failure(string errorMessage) => new(false, errorMessage);
}
