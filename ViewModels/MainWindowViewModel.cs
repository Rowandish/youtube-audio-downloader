using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using YoutubeAudioDownloader.Infrastructure;
using YoutubeAudioDownloader.Models;
using YoutubeAudioDownloader.Services;

namespace YoutubeAudioDownloader.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private const int MaxParallelDownloads = 3;

    private readonly YtDlpDownloadService _downloadService = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly System.Windows.Threading.DispatcherTimer _elapsedTimer;

    private CancellationTokenSource? _downloadCts;
    private readonly List<DownloadItem> _activeBatch = [];

    private string _urlInput = string.Empty;
    private string _outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    private AudioQualityOption _selectedQuality;
    private DownloadItem? _selectedItem;
    private int _processedCount;
    private int _totalCount;
    private double _overallProgress;
    private string _elapsedTime = "00:00:00";
    private string _statusMessage = "Ready";
    private bool _isDownloading;

    public MainWindowViewModel()
    {
        QualityOptions =
        [
            new AudioQualityOption(AudioQuality.High, "High"),
            new AudioQualityOption(AudioQuality.Medium, "Medium"),
            new AudioQualityOption(AudioQuality.Low, "Low")
        ];

        _selectedQuality = QualityOptions[2];

        AddUrlCommand = new RelayCommand(AddUrls, () => !string.IsNullOrWhiteSpace(UrlInput));
        RemoveSelectedCommand = new RelayCommand(RemoveSelectedItem, () => IsIdle && SelectedItem is not null);
        BrowseOutputDirectoryCommand = new RelayCommand(BrowseOutputDirectory, () => IsIdle);
        StartDownloadsCommand = new AsyncRelayCommand(StartDownloadsAsync, CanStartDownloads);
        StopDownloadsCommand = new RelayCommand(StopDownloads, () => IsDownloading);
        ClearErrorsCommand = new RelayCommand(ClearErrors, () => Errors.Count > 0);

        Items.CollectionChanged += OnItemsCollectionChanged;
        Errors.CollectionChanged += (_, _) => ClearErrorsCommand.NotifyCanExecuteChanged();

        _elapsedTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(1),
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) =>
            {
                ElapsedTime = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
            },
            System.Windows.Application.Current.Dispatcher);
    }

    public ObservableCollection<DownloadItem> Items { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    public IReadOnlyList<AudioQualityOption> QualityOptions { get; }

    public RelayCommand AddUrlCommand { get; }

    public RelayCommand RemoveSelectedCommand { get; }

    public RelayCommand BrowseOutputDirectoryCommand { get; }

    public AsyncRelayCommand StartDownloadsCommand { get; }

    public RelayCommand StopDownloadsCommand { get; }

    public RelayCommand ClearErrorsCommand { get; }

    public string UrlInput
    {
        get => _urlInput;
        set
        {
            if (SetProperty(ref _urlInput, value))
            {
                AddUrlCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set => SetProperty(ref _outputDirectory, value);
    }

    public AudioQualityOption SelectedQuality
    {
        get => _selectedQuality;
        set => SetProperty(ref _selectedQuality, value);
    }

    public DownloadItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                RemoveSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int ProcessedCount
    {
        get => _processedCount;
        private set
        {
            if (SetProperty(ref _processedCount, value))
            {
                OnPropertyChanged(nameof(ProcessedSummary));
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(ProcessedSummary));
            }
        }
    }

    public string ProcessedSummary => $"{ProcessedCount}/{TotalCount} processed";

    public double OverallProgress
    {
        get => _overallProgress;
        private set => SetProperty(ref _overallProgress, value);
    }

    public string ElapsedTime
    {
        get => _elapsedTime;
        private set => SetProperty(ref _elapsedTime, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                NotifyCommandStates();
            }
        }
    }

    public bool IsIdle => !IsDownloading;

    public void Dispose()
    {
        _elapsedTimer.Stop();
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyCommandStates();
    }

    private bool CanStartDownloads()
    {
        return IsIdle && Items.Any(item => item.State is DownloadState.Pending or DownloadState.Queued or DownloadState.Failed or DownloadState.Stopped);
    }

    private void AddUrls()
    {
        var rawUrls = UrlInput.Split(
            ['\r', '\n', ';', ',', ' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var added = 0;

        foreach (var url in rawUrls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsValidYoutubeUrl(url))
            {
                Errors.Add($"Invalid URL: {url}");
                continue;
            }

            if (Items.Any(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var newItem = new DownloadItem(url);

            if (IsDownloading)
            {
                newItem.State = DownloadState.Queued;
                newItem.StatusMessage = "Queued";
                _activeBatch.Add(newItem);
                TotalCount++;
                UpdateOverallProgress();
            }

            Items.Add(newItem);
            added++;
        }

        if (added > 0)
        {
            StatusMessage = $"Added {added} URL(s).";
            UrlInput = string.Empty;
        }
        else
        {
            StatusMessage = "No URLs added.";
        }
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

    private void RemoveSelectedItem()
    {
        if (SelectedItem is null)
        {
            return;
        }

        Items.Remove(SelectedItem);
        SelectedItem = null;
        StatusMessage = "Item removed.";
    }

    private void BrowseOutputDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select output folder",
            InitialDirectory = Directory.Exists(OutputDirectory)
                ? OutputDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };

        if (dialog.ShowDialog() == true)
        {
            OutputDirectory = dialog.FolderName;
        }
    }

    private async Task StartDownloadsAsync()
    {
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
            Errors.Add($"Unable to use output folder: {ex.Message}");
            return;
        }

        var batch = Items
            .Where(item => item.State is DownloadState.Pending or DownloadState.Queued or DownloadState.Failed or DownloadState.Stopped)
            .ToList();

        if (batch.Count == 0)
        {
            StatusMessage = "No items available for download.";
            return;
        }

        Errors.Clear();
        _activeBatch.Clear();

        foreach (var item in batch)
        {
            item.Progress = 0d;
            item.State = DownloadState.Queued;
            item.StatusMessage = "Queued";
            item.ErrorMessage = string.Empty;
            _activeBatch.Add(item);
        }

        ProcessedCount = 0;
        TotalCount = _activeBatch.Count;
        OverallProgress = 0d;
        StatusMessage = "Downloads started.";

        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();

        IsDownloading = true;
        _stopwatch.Restart();
        _elapsedTimer.Start();

        try
        {
            var runningTasks = new List<Task>(MaxParallelDownloads);
            var cancellationToken = _downloadCts.Token;

            while (true)
            {
                while (!cancellationToken.IsCancellationRequested && runningTasks.Count < MaxParallelDownloads)
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
                : Errors.Count > 0
                    ? "Completed with errors."
                    : "Downloads completed.";
        }
        finally
        {
            _elapsedTimer.Stop();
            _stopwatch.Stop();
            IsDownloading = false;
            UpdateOverallProgress();
        }
    }

    private async Task DownloadItemAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            item.State = DownloadState.Downloading;
            item.StatusMessage = "Downloading";

            var progress = new Progress<double>(value =>
            {
                item.Progress = value;
                UpdateOverallProgress();
            });

            var status = new Progress<string>(message =>
            {
                item.StatusMessage = message;
            });

            await _downloadService.DownloadAudioAsync(
                item.Url,
                OutputDirectory,
                SelectedQuality.Value,
                progress,
                status,
                cancellationToken);

            item.Progress = 100d;
            item.State = DownloadState.Completed;
            item.StatusMessage = "Completed";
        }
        catch (OperationCanceledException)
        {
            item.State = DownloadState.Stopped;
            item.StatusMessage = "Stopped";
        }
        catch (Exception ex)
        {
            item.State = DownloadState.Failed;
            item.StatusMessage = "Error";
            item.ErrorMessage = ex.Message;
            Errors.Add($"{DateTime.Now:HH:mm:ss} | {item.Url} | {ex.Message}");
        }
        finally
        {
            ProcessedCount++;
            UpdateOverallProgress();
        }
    }

    private void MarkQueuedItemsAsStopped()
    {
        foreach (var item in _activeBatch.Where(item => item.State == DownloadState.Queued))
        {
            item.State = DownloadState.Stopped;
            item.StatusMessage = "Stopped";
            item.Progress = 0d;
            ProcessedCount++;
        }

        UpdateOverallProgress();
    }

    private void StopDownloads()
    {
        if (!IsDownloading)
        {
            return;
        }

        StatusMessage = "Stopping downloads...";
        _downloadCts?.Cancel();
    }

    private void ClearErrors()
    {
        Errors.Clear();
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

    private void NotifyCommandStates()
    {
        AddUrlCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        BrowseOutputDirectoryCommand.NotifyCanExecuteChanged();
        StartDownloadsCommand.NotifyCanExecuteChanged();
        StopDownloadsCommand.NotifyCanExecuteChanged();
    }
}
