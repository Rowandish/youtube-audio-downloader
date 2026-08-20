using YoutubeAudioDownloader.Models;
using Xunit;

namespace YoutubeAudioDownloader.Tests;

public sealed class DownloadItemTests
{
    [Fact]
    public void Queue_ShouldResetRetryableItem()
    {
        var item = new DownloadItem("https://youtu.be/example");
        item.Fail("old error");

        item.Queue();

        Assert.Equal(DownloadState.Queued, item.State);
        Assert.Equal(0d, item.Progress);
        Assert.Equal("Queued", item.StatusMessage);
        Assert.Empty(item.ErrorMessage);
        Assert.True(item.ShowProgress);
    }

    [Fact]
    public void Start_ShouldSetDownloadingStateAndShowProgress()
    {
        var item = new DownloadItem("https://youtu.be/example");
        item.Queue();

        item.Start();

        Assert.Equal(DownloadState.Downloading, item.State);
        Assert.Equal("Downloading", item.StatusMessage);
        Assert.True(item.ShowProgress);
    }

    [Theory]
    [InlineData(-10d, 0d)]
    [InlineData(45.5d, 45.5d)]
    [InlineData(150d, 100d)]
    public void ReportProgress_ShouldClampToDownloadRange(double reportedProgress, double expectedProgress)
    {
        var item = new DownloadItem("https://youtu.be/example");

        item.ReportProgress(reportedProgress);

        Assert.Equal(expectedProgress, item.Progress);
    }

    [Fact]
    public void ReportStatus_ShouldUpdateDownloadStatus()
    {
        var item = new DownloadItem("https://youtu.be/example");
        item.Queue();

        item.ReportStatus("Extracting audio");

        Assert.Equal(DownloadState.Queued, item.State);
        Assert.Equal("Extracting audio", item.StatusMessage);
    }

    [Fact]
    public void Complete_ShouldSetTerminalStateAndHideProgress()
    {
        var item = new DownloadItem("https://youtu.be/example");
        item.Queue();
        item.Start();
        item.ReportProgress(60d);

        item.Complete();

        Assert.Equal(DownloadState.Completed, item.State);
        Assert.Equal(100d, item.Progress);
        Assert.Equal("Completed", item.StatusMessage);
        Assert.False(item.ShowProgress);
    }

    [Fact]
    public void Fail_ShouldSetErrorStateAndHideProgress()
    {
        var item = new DownloadItem("https://youtu.be/example");
        item.Queue();
        item.Start();

        item.Fail("Network unavailable");

        Assert.Equal(DownloadState.Failed, item.State);
        Assert.Equal("Error", item.StatusMessage);
        Assert.Equal("Network unavailable", item.ErrorMessage);
        Assert.False(item.ShowProgress);
    }

    [Fact]
    public void Stop_ShouldSetStoppedStateAndHideProgress()
    {
        var item = new DownloadItem("https://youtu.be/example");
        item.Queue();
        item.Start();

        item.Stop();

        Assert.Equal(DownloadState.Stopped, item.State);
        Assert.Equal("Stopped", item.StatusMessage);
        Assert.False(item.ShowProgress);
    }

    [Fact]
    public void ReportProgressAtOneHundredWhileDownloading_ShouldHideProgressForExtraction()
    {
        var item = new DownloadItem("https://youtu.be/example");
        item.Queue();
        item.Start();

        item.ReportProgress(100d);

        Assert.Equal(DownloadState.Downloading, item.State);
        Assert.False(item.ShowProgress);
    }
}
