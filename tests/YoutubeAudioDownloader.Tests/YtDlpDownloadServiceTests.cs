using System.Diagnostics;
using System.Reflection;
using YoutubeAudioDownloader.Models;
using YoutubeAudioDownloader.Services;
using Xunit;

namespace YoutubeAudioDownloader.Tests;

public sealed class YtDlpDownloadServiceTests
{
    [Fact]
    public void BuildProcessStartInfo_ShouldPreferWebEmbeddedClientAndKeepAndroidVrFallback()
    {
        const string url = "https://www.youtube.com/watch?v=RuZIpHrXopk";
        var method = typeof(YtDlpDownloadService).GetMethod(
            "BuildProcessStartInfo",
            BindingFlags.NonPublic | BindingFlags.Static);

        var processStartInfo = Assert.IsType<ProcessStartInfo>(method?.Invoke(
            null,
            [url, "C:\\Downloads", AudioQuality.High]));
        var arguments = processStartInfo.ArgumentList.ToList();
        var extractorArgsIndex = arguments.IndexOf("--extractor-args");

        Assert.True(extractorArgsIndex >= 0, "Missing --extractor-args.");
        Assert.Equal(
            "youtube:player_client=web_embedded,android_vr",
            arguments[extractorArgsIndex + 1]);
    }
}
