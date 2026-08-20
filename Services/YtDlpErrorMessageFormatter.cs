namespace YoutubeAudioDownloader.Services;

internal static class YtDlpErrorMessageFormatter
{
    public static string Format(string error, string? ytDlpVersion)
    {
        if (!error.Contains("403", StringComparison.Ordinal))
        {
            return error;
        }

        var version = string.IsNullOrWhiteSpace(ytDlpVersion) ? "unavailable" : ytDlpVersion;

        return $"{error}{Environment.NewLine}" +
               $"yt-dlp version: {version}.{Environment.NewLine}" +
               "YouTube denied this request (HTTP 403). First, update yt-dlp with: yt-dlp -U. " +
               "If it is already current, check authentication, cookies, or a temporary IP restriction.";
    }
}
