using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using YoutubeAudioDownloader.Models;
using YoutubeAudioDownloader.Services;
using Xunit;

namespace YoutubeAudioDownloader.Tests;

public sealed class DownloadBatchTests
{
    [Fact]
    public void Items_ShouldExposeReadOnlyObservableView()
    {
        var batch = new DownloadBatch(new ControlledAudioDownloadExecutor());
        var property = typeof(DownloadBatch).GetProperty(nameof(DownloadBatch.Items));

        Assert.NotNull(property);
        Assert.Equal(typeof(ReadOnlyObservableCollection<DownloadItem>), property!.PropertyType);
        var items = Assert.IsType<ReadOnlyObservableCollection<DownloadItem>>(batch.Items);
        var collection = (ICollection<DownloadItem>)items;
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            collection.Add(new DownloadItem("https://youtu.be/external")));
    }

    [Fact]
    public void Errors_ShouldExposeReadOnlyObservableView()
    {
        var batch = new DownloadBatch(new ControlledAudioDownloadExecutor());
        var property = typeof(DownloadBatch).GetProperty(nameof(DownloadBatch.Errors));

        Assert.NotNull(property);
        Assert.Equal(typeof(ReadOnlyObservableCollection<string>), property!.PropertyType);
        var errors = Assert.IsType<ReadOnlyObservableCollection<string>>(batch.Errors);
        var collection = (ICollection<string>)errors;
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add("external error"));
    }

    [Fact]
    public void AddUrls_ShouldRejectInvalidAndCaseInsensitiveDuplicateUrls()
    {
        var batch = new DownloadBatch(new ControlledAudioDownloadExecutor());

        batch.AddUrls("not-a-url\nhttps://youtu.be/Example\nHTTPS://YOUTU.BE/example");

        var item = Assert.Single(batch.Items);
        Assert.Equal("https://youtu.be/Example", item.Url);
        Assert.Equal("Invalid URL: not-a-url", Assert.Single(batch.Errors));
        Assert.Equal("Added 1 URL(s).", batch.StatusMessage);

        batch.AddUrls("https://YOUTU.BE/EXAMPLE");

        Assert.Single(batch.Items);
        Assert.Equal("No URLs added.", batch.StatusMessage);
    }

    [Fact]
    public async Task StartAsync_ShouldRunAtMostThreeAttemptsAndCompleteWithTerminalProgress()
    {
        var executor = new ControlledAudioDownloadExecutor();
        var batch = CreateBatch(executor, 4);

        var run = batch.StartAsync();
        await WaitUntilAsync(() => executor.AttemptCount == 3);

        Assert.True(batch.IsDownloading);
        Assert.Equal("Downloads started.", batch.StatusMessage);
        Assert.Equal(0, batch.ProcessedCount);
        Assert.Equal(4, batch.TotalCount);
        Assert.Equal(3, executor.MaximumActiveCount);
        Assert.Equal(3, batch.Items.Count(item => item.State == DownloadState.Downloading));
        Assert.Equal(DownloadState.Queued, batch.Items[3].State);

        executor.GetAttempt(0).ReportProgress(25d);
        executor.GetAttempt(0).ReportStatus("Extracting audio");
        await WaitUntilAsync(() => batch.Items[0].StatusMessage == "Extracting audio");

        Assert.Equal(25d, batch.Items[0].Progress);
        Assert.Equal(6.25d, batch.OverallProgress);

        executor.GetAttempt(0).CompleteSuccessfully();
        await WaitUntilAsync(() => executor.AttemptCount == 4);

        Assert.Equal(3, executor.MaximumActiveCount);

        executor.CompleteAllSuccessfully();
        await run;

        Assert.False(batch.IsDownloading);
        Assert.Equal("Downloads completed.", batch.StatusMessage);
        Assert.Equal(4, batch.ProcessedCount);
        Assert.Equal(4, batch.TotalCount);
        Assert.Equal(100d, batch.OverallProgress);
        Assert.All(batch.Items, item =>
        {
            Assert.Equal(DownloadState.Completed, item.State);
            Assert.Equal(100d, item.Progress);
        });
        Assert.Empty(batch.Errors);
    }

    [Fact]
    public async Task StartAsync_ShouldIgnoreReentrantCallAndKeepOriginalCancellationOwnership()
    {
        var executor = new ControlledAudioDownloadExecutor();
        var batch = CreateBatch(executor, 5);

        var firstRun = batch.StartAsync();
        await WaitUntilAsync(() => executor.AttemptCount == 3);

        var secondRun = batch.StartAsync();

        try
        {
            await WaitUntilAsync(() => secondRun.IsCompleted || executor.AttemptCount > 3);

            Assert.True(secondRun.IsCompleted, "A re-entrant start should return without starting another scheduler.");
            Assert.Equal(3, executor.AttemptCount);
            Assert.Equal(3, executor.MaximumActiveCount);
            Assert.Equal(5, batch.TotalCount);

            batch.Stop();
            await firstRun.WaitAsync(TimeSpan.FromSeconds(5));
            await secondRun;

            Assert.Equal("Downloads stopped.", batch.StatusMessage);
            Assert.Equal(5, batch.ProcessedCount);
            Assert.All(batch.Items, item => Assert.Equal(DownloadState.Stopped, item.State));
        }
        finally
        {
            executor.CompleteAllSuccessfully();
            await Task.WhenAll(firstRun, secondRun).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Remove_ShouldKeepItem_WhenBatchIsDownloading()
    {
        var executor = new ControlledAudioDownloadExecutor();
        var batch = CreateBatch(executor, 1);
        var item = batch.Items[0];
        var run = batch.StartAsync();
        await WaitUntilAsync(() => executor.AttemptCount == 1);

        try
        {
            batch.Remove(item);

            Assert.Same(item, Assert.Single(batch.Items));
            Assert.Equal("Downloads started.", batch.StatusMessage);
        }
        finally
        {
            batch.Stop();
            executor.CompleteAllSuccessfully();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task StartAsync_ShouldContinueAfterFailureAndReportCompletedWithErrors()
    {
        var executor = new ControlledAudioDownloadExecutor();
        var batch = CreateBatch(executor, 4);
        var failedUrl = batch.Items[0].Url;

        var run = batch.StartAsync();
        await WaitUntilAsync(() => executor.AttemptCount == 3);

        executor.GetAttempt(0).CompleteWithFailure("Download failed");
        await WaitUntilAsync(() => executor.AttemptCount == 4);

        executor.CompleteAllSuccessfully();
        await run;

        Assert.Equal(DownloadState.Failed, batch.Items[0].State);
        Assert.Equal("Download failed", batch.Items[0].ErrorMessage);
        var error = Assert.Single(batch.Errors);
        Assert.Matches($"^\\d{{2}}:\\d{{2}}:\\d{{2}} \\| {Regex.Escape(failedUrl)} \\| Download failed$", error);
        Assert.Equal("Completed with errors.", batch.StatusMessage);
        Assert.Equal(4, batch.ProcessedCount);
        Assert.Equal(100d, batch.OverallProgress);
    }

    [Fact]
    public async Task AddUrls_ShouldJoinActiveBatchAndReadCurrentSettingsWhenAttemptStarts()
    {
        var executor = new ControlledAudioDownloadExecutor();
        var batch = CreateBatch(executor, 4);
        var initialDirectory = Path.Combine(Path.GetTempPath(), "Initial");
        var changedDirectory = Path.Combine(Path.GetTempPath(), "Changed");
        batch.OutputDirectory = initialDirectory;
        batch.SelectedQuality = AudioQuality.Low;

        var run = batch.StartAsync();
        await WaitUntilAsync(() => executor.AttemptCount == 3);

        Assert.All(executor.Attempts, attempt =>
        {
            Assert.Equal(initialDirectory, attempt.Request.OutputDirectory);
            Assert.Equal(AudioQuality.Low, attempt.Request.Quality);
        });

        batch.OutputDirectory = changedDirectory;
        batch.SelectedQuality = AudioQuality.High;
        batch.AddUrls("https://youtu.be/late");

        Assert.Equal("Added 1 URL(s).", batch.StatusMessage);
        Assert.Equal(5, batch.TotalCount);
        Assert.Equal(DownloadState.Queued, batch.Items[4].State);

        executor.GetAttempt(0).CompleteSuccessfully();
        await WaitUntilAsync(() => executor.AttemptCount == 4);
        executor.GetAttempt(1).CompleteSuccessfully();
        await WaitUntilAsync(() => executor.AttemptCount == 5);

        Assert.All(executor.Attempts.Skip(3), attempt =>
        {
            Assert.Equal(changedDirectory, attempt.Request.OutputDirectory);
            Assert.Equal(AudioQuality.High, attempt.Request.Quality);
        });

        executor.CompleteAllSuccessfully();
        await run;

        Assert.Equal(5, batch.ProcessedCount);
        Assert.Equal(5, batch.TotalCount);
        Assert.Equal(100d, batch.OverallProgress);
        Assert.Equal("Downloads completed.", batch.StatusMessage);
    }

    [Fact]
    public async Task Stop_ShouldCancelActiveAttemptsAndStopQueuedItemsWithoutErrors()
    {
        var executor = new ControlledAudioDownloadExecutor();
        var batch = CreateBatch(executor, 5);

        var run = batch.StartAsync();
        await WaitUntilAsync(() => executor.AttemptCount == 3);

        executor.GetAttempt(0).ReportProgress(40d);
        await WaitUntilAsync(() => batch.Items[0].Progress == 40d);

        batch.Stop();

        Assert.Equal("Stopping downloads...", batch.StatusMessage);
        await run;

        Assert.Equal("Downloads stopped.", batch.StatusMessage);
        Assert.False(batch.IsDownloading);
        Assert.Equal(5, batch.ProcessedCount);
        Assert.Equal(5, batch.TotalCount);
        Assert.Equal(100d, batch.OverallProgress);
        Assert.Empty(batch.Errors);
        Assert.All(batch.Items, item => Assert.Equal(DownloadState.Stopped, item.State));
        Assert.Equal(40d, batch.Items[0].Progress);
        Assert.Equal(0d, batch.Items[3].Progress);
        Assert.Equal(0d, batch.Items[4].Progress);
    }

    [Fact]
    public async Task StartAsync_ShouldRetryFailedItemsAndResetTheirAttemptState()
    {
        var executor = new ControlledAudioDownloadExecutor();
        var batch = CreateBatch(executor, 1);

        var firstRun = batch.StartAsync();
        await WaitUntilAsync(() => executor.AttemptCount == 1);
        executor.GetAttempt(0).ReportProgress(55d);
        await WaitUntilAsync(() => batch.Items[0].Progress == 55d);
        executor.GetAttempt(0).CompleteWithFailure("First failure");
        await firstRun;

        Assert.Equal(DownloadState.Failed, batch.Items[0].State);
        Assert.Equal("Completed with errors.", batch.StatusMessage);
        Assert.Single(batch.Errors);

        var secondRun = batch.StartAsync();
        await WaitUntilAsync(() => executor.AttemptCount == 2);

        Assert.Equal(DownloadState.Downloading, batch.Items[0].State);
        Assert.Equal(0d, batch.Items[0].Progress);
        Assert.Empty(batch.Items[0].ErrorMessage);
        Assert.Empty(batch.Errors);
        Assert.Equal(0, batch.ProcessedCount);
        Assert.Equal(1, batch.TotalCount);

        executor.GetAttempt(1).CompleteSuccessfully();
        await secondRun;

        Assert.Equal(DownloadState.Completed, batch.Items[0].State);
        Assert.Equal("Downloads completed.", batch.StatusMessage);
        Assert.Equal(1, batch.ProcessedCount);
        Assert.Equal(100d, batch.OverallProgress);
    }

    [Fact]
    public void StartAsync_ShouldIgnoreQueuedUpdatesAfterAttemptCompletes()
    {
        var previousContext = SynchronizationContext.Current;
        var queuedContext = new QueuedSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(queuedContext);

        try
        {
            var batch = new DownloadBatch(new ImmediatelyCompletingReportingExecutor())
            {
                OutputDirectory = Path.GetTempPath()
            };
            batch.AddUrls("https://youtu.be/one");

            var run = batch.StartAsync();

            Assert.True(run.IsCompletedSuccessfully);
            Assert.Equal(DownloadState.Completed, batch.Items[0].State);
            Assert.Equal(100d, batch.Items[0].Progress);
            Assert.Equal("Completed", batch.Items[0].StatusMessage);

            queuedContext.Drain();

            Assert.Equal(DownloadState.Completed, batch.Items[0].State);
            Assert.Equal(100d, batch.Items[0].Progress);
            Assert.Equal("Completed", batch.Items[0].StatusMessage);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Fact]
    public async Task StartAsync_ShouldPreserveEmptyDirectoryAndEmptyBatchMessages()
    {
        var executor = new ControlledAudioDownloadExecutor();
        var batch = new DownloadBatch(executor)
        {
            OutputDirectory = string.Empty
        };
        batch.AddUrls("https://youtu.be/one");

        await batch.StartAsync();

        Assert.Equal("Specify an output folder.", batch.StatusMessage);
        Assert.Equal(0, executor.AttemptCount);

        batch.OutputDirectory = Path.GetTempPath();
        batch.Remove(batch.Items[0]);

        Assert.Equal("Item removed.", batch.StatusMessage);

        await batch.StartAsync();

        Assert.Equal("No items available for download.", batch.StatusMessage);
        Assert.Equal(0, executor.AttemptCount);
    }

    [Fact]
    public void ClearErrors_ShouldRemoveIntakeErrors()
    {
        var batch = new DownloadBatch(new ControlledAudioDownloadExecutor());
        batch.AddUrls("invalid");
        Assert.Single(batch.Errors);

        batch.ClearErrors();

        Assert.Empty(batch.Errors);
    }

    private static DownloadBatch CreateBatch(ControlledAudioDownloadExecutor executor, int itemCount)
    {
        var batch = new DownloadBatch(executor)
        {
            OutputDirectory = Path.GetTempPath()
        };

        batch.AddUrls(string.Join('\n', Enumerable.Range(1, itemCount).Select(index => $"https://youtu.be/item-{index}")));
        return batch;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < timeout, "Timed out waiting for the expected batch state.");
            await Task.Delay(10);
        }
    }

    private sealed class ControlledAudioDownloadExecutor : IAudioDownloadExecutor
    {
        private readonly ConcurrentQueue<ControlledAttempt> _attempts = new();
        private int _activeCount;
        private int _maximumActiveCount;

        public IReadOnlyList<ControlledAttempt> Attempts => _attempts.ToArray();

        public int AttemptCount => _attempts.Count;

        public int MaximumActiveCount => Volatile.Read(ref _maximumActiveCount);

        public async Task<AudioDownloadResult> ExecuteAsync(
            AudioDownloadRequest request,
            IProgress<AudioDownloadUpdate> updates,
            CancellationToken cancellationToken)
        {
            var attempt = new ControlledAttempt(request, updates);
            _attempts.Enqueue(attempt);

            var activeCount = Interlocked.Increment(ref _activeCount);
            UpdateMaximumActiveCount(activeCount);

            using var cancellationRegistration = cancellationToken.Register(
                () => attempt.Cancel(cancellationToken));

            try
            {
                return await attempt.Completion.Task;
            }
            finally
            {
                Interlocked.Decrement(ref _activeCount);
            }
        }

        public ControlledAttempt GetAttempt(int index) => Attempts[index];

        public void CompleteAllSuccessfully()
        {
            foreach (var attempt in Attempts)
            {
                attempt.CompleteSuccessfully();
            }
        }

        private void UpdateMaximumActiveCount(int activeCount)
        {
            while (true)
            {
                var currentMaximum = Volatile.Read(ref _maximumActiveCount);

                if (currentMaximum >= activeCount ||
                    Interlocked.CompareExchange(ref _maximumActiveCount, activeCount, currentMaximum) == currentMaximum)
                {
                    return;
                }
            }
        }
    }

    private sealed class ImmediatelyCompletingReportingExecutor : IAudioDownloadExecutor
    {
        public Task<AudioDownloadResult> ExecuteAsync(
            AudioDownloadRequest request,
            IProgress<AudioDownloadUpdate> updates,
            CancellationToken cancellationToken)
        {
            IProgress<AudioDownloadUpdate> asynchronouslyForwardedUpdates =
                new Progress<AudioDownloadUpdate>(updates.Report);
            asynchronouslyForwardedUpdates.Report(new DownloadProgressUpdate(10d));
            asynchronouslyForwardedUpdates.Report(new DownloadStatusUpdate("Late status"));

            return Task.FromResult(AudioDownloadResult.Success());
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _callbacks.Enqueue((callback, state));
        }

        public void Drain()
        {
            while (_callbacks.TryDequeue(out var work))
            {
                work.Callback(work.State);
            }
        }
    }

    private sealed class ControlledAttempt
    {
        public ControlledAttempt(
            AudioDownloadRequest request,
            IProgress<AudioDownloadUpdate> updates)
        {
            Request = request;
            Updates = updates;
        }

        public AudioDownloadRequest Request { get; }

        public IProgress<AudioDownloadUpdate> Updates { get; }

        public TaskCompletionSource<AudioDownloadResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReportProgress(double value) =>
            Updates.Report(new DownloadProgressUpdate(value));

        public void ReportStatus(string message) =>
            Updates.Report(new DownloadStatusUpdate(message));

        public void CompleteSuccessfully() =>
            Completion.TrySetResult(AudioDownloadResult.Success());

        public void CompleteWithFailure(string errorMessage) =>
            Completion.TrySetResult(AudioDownloadResult.Failure(errorMessage));

        public void Cancel(CancellationToken cancellationToken) =>
            Completion.TrySetCanceled(cancellationToken);
    }
}
