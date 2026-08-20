using System.Collections.ObjectModel;
using System.IO;
using YoutubeAudioDownloader.Infrastructure;
using YoutubeAudioDownloader.Models;

namespace YoutubeAudioDownloader.Services;

public sealed class DownloadBatch : ObservableObject, IDisposable
{
    private const int MaxParallelDownloads = 3;

    private readonly IAudioDownloadExecutor _executor;
    private readonly List<DownloadItem> _activeBatch = [];
    private readonly ObservableCollection<DownloadItem> _items = [];
    private readonly ObservableCollection<string> _errors = [];

    private CancellationTokenSource? _downloadCts;
    private string _outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    private AudioQuality _selectedQuality = AudioQuality.Low;
    private int _processedCount;
    private int _totalCount;
    private double _overallProgress;
    private string _statusMessage = "Ready";
    private bool _isDownloading;

    public DownloadBatch(IAudioDownloadExecutor executor)
    {
        _executor = executor;
        Items = new ReadOnlyObservableCollection<DownloadItem>(_items);
        Errors = new ReadOnlyObservableCollection<string>(_errors);
    }

    public ReadOnlyObservableCollection<DownloadItem> Items { get; }

    public ReadOnlyObservableCollection<string> Errors { get; }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set => SetProperty(ref _outputDirectory, value);
    }

    public AudioQuality SelectedQuality
    {
        get => _selectedQuality;
        set => SetProperty(ref _selectedQuality, value);
    }

    public int ProcessedCount
    {
        get => _processedCount;
        private set => SetProperty(ref _processedCount, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        private set => SetProperty(ref _totalCount, value);
    }

    public double OverallProgress
    {
        get => _overallProgress;
        private set => SetProperty(ref _overallProgress, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set => SetProperty(ref _isDownloading, value);
    }

    public int AddUrls(string rawInput)
    {
        var rawUrls = rawInput.Split(
            ['\r', '\n', ';', ',', ' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var added = 0;

        foreach (var url in rawUrls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsValidYoutubeUrl(url))
            {
                _errors.Add($"Invalid URL: {url}");
                continue;
            }

            if (_items.Any(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var newItem = new DownloadItem(url);

            if (IsDownloading)
            {
                newItem.Queue();
                _activeBatch.Add(newItem);
                TotalCount++;
                UpdateOverallProgress();
            }

            _items.Add(newItem);
            added++;
        }

        StatusMessage = added > 0
            ? $"Added {added} URL(s)."
            : "No URLs added.";

        return added;
    }

    public void Remove(DownloadItem item)
    {
        if (IsDownloading)
        {
            return;
        }

        _items.Remove(item);
        StatusMessage = "Item removed.";
    }

    public async Task StartAsync()
    {
        if (IsDownloading)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusMessage = "Specify an output folder.";
            return;
        }

        try
        {
            Directory.CreateDirectory(OutputDirectory);
        }
        catch (Exception ex)
        {
            StatusMessage = "Invalid output folder.";
            _errors.Add($"Unable to use output folder: {ex.Message}");
            return;
        }

        var itemsToDownload = _items
            .Where(item => item.State is DownloadState.Pending or DownloadState.Queued or DownloadState.Failed or DownloadState.Stopped)
            .ToList();

        if (itemsToDownload.Count == 0)
        {
            StatusMessage = "No items available for download.";
            return;
        }

        _errors.Clear();
        _activeBatch.Clear();

        foreach (var item in itemsToDownload)
        {
            item.Queue();
            _activeBatch.Add(item);
        }

        ProcessedCount = 0;
        TotalCount = _activeBatch.Count;
        OverallProgress = 0d;
        StatusMessage = "Downloads started.";

        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();

        IsDownloading = true;

        try
        {
            var runningTasks = new List<Task>(MaxParallelDownloads);
            var cancellationToken = _downloadCts.Token;

            while (true)
            {
                while (!cancellationToken.IsCancellationRequested &&
                       runningTasks.Count < MaxParallelDownloads)
                {
                    var nextItem = _activeBatch.FirstOrDefault(item => item.State == DownloadState.Queued);

                    if (nextItem is null)
                    {
                        break;
                    }

                    runningTasks.Add(DownloadItemAsync(nextItem, cancellationToken));
                }

                if (runningTasks.Count == 0)
                {
                    break;
                }

                var completedTask = await Task.WhenAny(runningTasks);
                runningTasks.Remove(completedTask);
                await completedTask;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                MarkQueuedItemsAsStopped();
            }

            StatusMessage = cancellationToken.IsCancellationRequested
                ? "Downloads stopped."
                : _errors.Count > 0
                    ? "Completed with errors."
                    : "Downloads completed.";
        }
        finally
        {
            IsDownloading = false;
            UpdateOverallProgress();
        }
    }

    public void Stop()
    {
        if (!IsDownloading)
        {
            return;
        }

        StatusMessage = "Stopping downloads...";
        _downloadCts?.Cancel();
    }

    public void ClearErrors()
    {
        _errors.Clear();
    }

    public void Dispose()
    {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
    }

    private static bool IsValidYoutubeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        var host = parsedUri.Host.ToLowerInvariant();

        return host.Contains("youtube.com", StringComparison.Ordinal) ||
               host.Contains("youtu.be", StringComparison.Ordinal);
    }

    private async Task DownloadItemAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        var acceptsUpdates = 1;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            item.Start();

            var updates = new Progress<AudioDownloadUpdate>(update =>
            {
                if (Volatile.Read(ref acceptsUpdates) == 0 ||
                    item.State != DownloadState.Downloading)
                {
                    return;
                }

                switch (update)
                {
                    case DownloadProgressUpdate progress:
                        item.ReportProgress(progress.Value);
                        UpdateOverallProgress();
                        break;

                    case DownloadStatusUpdate status:
                        item.ReportStatus(status.Message);
                        break;
                }
            });

            var request = new AudioDownloadRequest(
                item.Url,
                OutputDirectory,
                SelectedQuality);
            var result = await _executor.ExecuteAsync(request, updates, cancellationToken);
            Interlocked.Exchange(ref acceptsUpdates, 0);

            if (result.Succeeded)
            {
                item.Complete();
            }
            else
            {
                item.Fail(result.ErrorMessage!);
                _errors.Add($"{DateTime.Now:HH:mm:ss} | {item.Url} | {result.ErrorMessage}");
            }
        }
        catch (OperationCanceledException)
        {
            Interlocked.Exchange(ref acceptsUpdates, 0);
            item.Stop();
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref acceptsUpdates, 0);
            item.Fail(ex.Message);
            _errors.Add($"{DateTime.Now:HH:mm:ss} | {item.Url} | {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref acceptsUpdates, 0);
            IncrementProcessedCount();
            UpdateOverallProgress();
        }
    }

    private void MarkQueuedItemsAsStopped()
    {
        foreach (var item in _activeBatch.Where(item => item.State == DownloadState.Queued))
        {
            item.Stop();
            item.ReportProgress(0d);
            IncrementProcessedCount();
        }

        UpdateOverallProgress();
    }

    private void IncrementProcessedCount()
    {
        Interlocked.Increment(ref _processedCount);
        OnPropertyChanged(nameof(ProcessedCount));
    }

    private void UpdateOverallProgress()
    {
        if (_activeBatch.Count == 0)
        {
            OverallProgress = 0d;
            return;
        }

        var totalProgress = _activeBatch.Sum(item =>
            item.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Stopped
                ? 100d
                : item.Progress);

        OverallProgress = totalProgress / _activeBatch.Count;
    }
}
