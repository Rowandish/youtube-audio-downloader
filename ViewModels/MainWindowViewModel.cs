using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Threading;
using YoutubeAudioDownloader.Infrastructure;
using YoutubeAudioDownloader.Models;
using YoutubeAudioDownloader.Services;

namespace YoutubeAudioDownloader.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly DownloadBatch _batch;
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _elapsedTimer;

    private string _urlInput = string.Empty;
    private AudioQualityOption _selectedQuality;
    private DownloadItem? _selectedItem;
    private string _elapsedTime = "00:00:00";

    public MainWindowViewModel(DownloadBatch batch)
    {
        _batch = batch;

        QualityOptions =
        [
            new AudioQualityOption(AudioQuality.High, "High"),
            new AudioQualityOption(AudioQuality.Medium, "Medium"),
            new AudioQualityOption(AudioQuality.Low, "Low")
        ];

        _selectedQuality = QualityOptions[2];
        _batch.SelectedQuality = _selectedQuality.Value;

        AddUrlCommand = new RelayCommand(AddUrls, () => !string.IsNullOrWhiteSpace(UrlInput));
        RemoveSelectedCommand = new RelayCommand(RemoveSelectedItem, () => IsIdle && SelectedItem is not null);
        BrowseOutputDirectoryCommand = new RelayCommand(BrowseOutputDirectory, () => IsIdle);
        StartDownloadsCommand = new AsyncRelayCommand(StartDownloadsAsync, CanStartDownloads);
        StopDownloadsCommand = new RelayCommand(_batch.Stop, () => IsDownloading);
        ClearErrorsCommand = new RelayCommand(_batch.ClearErrors, () => Errors.Count > 0);

        ((INotifyCollectionChanged)Items).CollectionChanged += OnItemsCollectionChanged;
        ((INotifyCollectionChanged)Errors).CollectionChanged += OnErrorsCollectionChanged;
        _batch.PropertyChanged += OnBatchPropertyChanged;

        _elapsedTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += OnElapsedTimerTick;
    }

    public ReadOnlyObservableCollection<DownloadItem> Items => _batch.Items;

    public ReadOnlyObservableCollection<string> Errors => _batch.Errors;

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
        get => _batch.OutputDirectory;
        set => _batch.OutputDirectory = value;
    }

    public AudioQualityOption SelectedQuality
    {
        get => _selectedQuality;
        set
        {
            if (SetProperty(ref _selectedQuality, value))
            {
                _batch.SelectedQuality = value.Value;
            }
        }
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

    public int ProcessedCount => _batch.ProcessedCount;

    public int TotalCount => _batch.TotalCount;

    public string ProcessedSummary => $"{ProcessedCount}/{TotalCount} processed";

    public double OverallProgress => _batch.OverallProgress;

    public string ElapsedTime
    {
        get => _elapsedTime;
        private set => SetProperty(ref _elapsedTime, value);
    }

    public string StatusMessage => _batch.StatusMessage;

    public bool IsDownloading => _batch.IsDownloading;

    public bool IsIdle => !IsDownloading;

    public void Dispose()
    {
        _elapsedTimer.Stop();
        _elapsedTimer.Tick -= OnElapsedTimerTick;
        ((INotifyCollectionChanged)Items).CollectionChanged -= OnItemsCollectionChanged;
        ((INotifyCollectionChanged)Errors).CollectionChanged -= OnErrorsCollectionChanged;
        _batch.PropertyChanged -= OnBatchPropertyChanged;
        _batch.Dispose();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyCommandStates();
    }

    private void OnErrorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ClearErrorsCommand.NotifyCanExecuteChanged();
    }

    private void OnBatchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);

        if (e.PropertyName is nameof(DownloadBatch.ProcessedCount) or nameof(DownloadBatch.TotalCount))
        {
            OnPropertyChanged(nameof(ProcessedSummary));
        }

        if (e.PropertyName == nameof(DownloadBatch.IsDownloading))
        {
            OnPropertyChanged(nameof(IsIdle));

            if (IsDownloading)
            {
                _stopwatch.Restart();
                _elapsedTimer.Start();
            }
            else
            {
                _elapsedTimer.Stop();
                _stopwatch.Stop();
            }

            NotifyCommandStates();
        }
    }

    private bool CanStartDownloads()
    {
        return IsIdle && Items.Any(item =>
            item.State is DownloadState.Pending or DownloadState.Queued or DownloadState.Failed or DownloadState.Stopped);
    }

    private void AddUrls()
    {
        if (_batch.AddUrls(UrlInput) > 0)
        {
            UrlInput = string.Empty;
        }
    }

    private void RemoveSelectedItem()
    {
        if (SelectedItem is null)
        {
            return;
        }

        _batch.Remove(SelectedItem);
        SelectedItem = null;
    }

    private void BrowseOutputDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select output folder",
            SelectedPath = Directory.Exists(OutputDirectory)
                ? OutputDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputDirectory = dialog.SelectedPath;
        }
    }

    private Task StartDownloadsAsync()
    {
        return _batch.StartAsync();
    }

    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        ElapsedTime = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
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
