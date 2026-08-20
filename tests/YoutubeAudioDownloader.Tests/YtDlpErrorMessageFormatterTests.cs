using Xunit;
using YoutubeAudioDownloader.Services;

namespace YoutubeAudioDownloader.Tests;

public sealed class YtDlpErrorMessageFormatterTests
{
    [Fact]
    public void Format_ShouldSuggestUpdatingAndShowVersion_WhenYtDlpReportsHttp403()
    {
        var result = YtDlpErrorMessageFormatter.Format(
            "ERROR: unable to download video data: HTTP Error 403: Forbidden",
            "2026.08.12");

        Assert.Contains("HTTP 403", result);
        Assert.Contains("yt-dlp version: 2026.08.12", result);
        Assert.Contains("yt-dlp -U", result);
        Assert.Contains("authentication, cookies, or a temporary IP restriction", result);
    }

    [Fact]
    public void Format_ShouldPreserveOriginalError_WhenTheErrorIsNotHttp403()
    {
        const string error = "ERROR: Video unavailable";

        var result = YtDlpErrorMessageFormatter.Format(error, "2026.08.12");

        Assert.Equal(error, result);
    }
}
